namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>
/// DE-based ambient occlusion (DEAO), a faithful port of the production AO path in
/// <c>TCalcAmbShadowDEThreadGeneral.Execute</c> (CalcAmbShadowDE.pas:333-570) plus the
/// <c>calcAmbshadow</c> 0..16383 -&gt; scalar conversion (PaintThread.pas:199-239).
///
/// Per surface point it builds a hemisphere of <c>RayCount</c> rays around the surface normal
/// (one straight ray plus <c>Quality</c> rings), marches the world-space distance estimate along
/// each, and accumulates how unobstructed each ray is into <c>AmbShadow</c> (0 = fully open,
/// 16383 = fully occluded). The ray frame mirrors MB3D exactly: a rotation built from the normal
/// (<c>MakeRotQuatFromSNormals</c> -&gt; <c>CreateSMatrixFromQuat</c>) maps the cone into the normal
/// frame (<c>RotateSVectorReverseS</c>), then the StepWidth-scaled camera basis VGrads maps it into
/// world space (<c>RotateSVectorS</c>). Only the outside-rendering path is implemented
/// (bInsideRendering = bIsIFS = False, the case for normal external renders); AOdither = 0.
///
/// Returns <c>dAmbSh</c> in (0,1]: 1 = unoccluded, smaller = more occluded.
/// </summary>
public static class Mb3dAmbientOcclusion
{
    private const double Pi = Math.PI;
    private const double PiM2 = Math.PI * 2.0;
    private const double Pid180 = Math.PI / 180.0;
    private const double S05 = 0.5;
    private const double S1d3 = 1.0 / 3.0;
    private const double S002 = 0.02;
    private const double S03 = 0.3;
    private const double DNearOne = 0.9999999;
    private const double DNearMinusOne = -0.9999999;

    // Quality 3 builds 1 + sum_y Round(sin(y*ABR)*2pi/ABR) rays; the array bound in MB3D is 0..32.
    private const int MaxRays = 33;

    public static double Compute(in Mb3dHit hit, Mb3dSceneParameters scene)
    {
        if (!hit.IsHit || !scene.AmbShadowEnabled)
            return 1.0;

        ushort ambShadow = ComputeAmbShadow(hit, scene);
        return CalcAmbShadow(ambShadow, scene.AmbShadowAmplitude);
    }

    /// <summary>
    /// The per-point DEAO march, returning the 0..16383 occlusion word MB3D stores in
    /// <c>PsiLight.AmbShadow</c> (CalcAmbShadowDE.pas:570).
    /// </summary>
    private static ushort ComputeAmbShadow(in Mb3dHit hit, Mb3dSceneParameters scene)
    {
        int quality = scene.AmbShadowQuality;

        Span<SVec> rotM = stackalloc SVec[MaxRays];
        Span<int> rowCount = stackalloc int[MaxRays];
        Span<double> minRA = stackalloc double[MaxRays];
        Span<double> sAdd = stackalloc double[MaxRays];

        // --- Build the cone of ray directions in the local hemisphere (CalcAmbShadowDE.pas:337-372).
        int rayCount;
        double dStepMul, dMinADif, correctionWeight;
        SMatrix3 rotW;
        if (quality == 0)
        {
            double abr0 = 60 * Pid180;
            rayCount = 0;
            for (var i = 0; i < 3; i++)
            {
                BuildRotMatrixS(0, 0.5 * abr0, i * PiM2 * S1d3, out rotW);
                rotM[rayCount] = ScaleSVec(RowZ(rotW), -1);
                rayCount++;
            }
            dStepMul = 1.8;
            dMinADif = -1;
            correctionWeight = S03;
        }
        else
        {
            BuildRotMatrixS(0, 0, 0, out rotW);   // one straight ray in the normal direction
            rotM[0] = ScaleSVec(RowZ(rotW), -1);
            rayCount = 1;
            double abrRows = Pi * S05 / (quality + 0.9);   // angle between rows
            for (var y = 1; y <= quality; y++)
            {
                double rowAngle = y * abrRows;              // angle of row relative to the straight ray
                int inRow = (int)Math.Round(Math.Sin(rowAngle) * Pi * 2 / abrRows);   // ray count in this row
                rowCount[y] = inRow;
                for (var x = 0; x < inRow; x++)
                {
                    BuildRotMatrixS(0, rowAngle, x * 2 * Pi / inRow, out rotW);
                    rotM[rayCount] = ScaleSVec(RowZ(rotW), -1);
                    rayCount++;
                }
            }
            dStepMul = 1 + Math.Sin(abrRows);
            dMinADif = Math.Cos(abrRows * 1.2);
            correctionWeight = quality == 1 ? 0.2 : 0.1666;
        }

        double sMaxD = scene.DEAOmaxL * S05 * Math.Sqrt((double)scene.Height * scene.Height + (double)scene.Width * scene.Width);

        // --- Per-point thresholds. The C# marcher already refined the surface, so hit.Depth is the
        // converged mZZ; mirror the post-bin-search msDEstop (CalcAmbShadowDE.pas:427-437).
        double zz = hit.Depth;
        double msDEstop = Math.Min(scene.DEstop * (1 + Math.Abs(zz) * scene.DEstopFactor), 1e6);
        double stepAO = msDEstop / scene.DEstop;
        double maxDist = sMaxD * Math.Sqrt(stepAO);
        // bVaryDEstop && not bInsideRendering -> msDEstop /= Sqr(DStepMul); else msDEstop = DEstop / Sqr(DStepMul).
        msDEstop = scene.VaryDEstop ? msDEstop / (dStepMul * dStepMul) : scene.DEstop / (dStepMul * dStepMul);

        double deMul = Math.Sqrt(rayCount * S05);
        double abr = 1.2 / ArcSinSafe(1 / deMul);                 // recip. angle, used by the overlap correction
        double mDd10 = 0.1 / (maxDist * deMul);

        // --- Rotation from the surface normal: cone(local) -> normal frame -> world (via VGrads).
        SVec normal = new(hit.Normal.X, hit.Normal.Y, hit.Normal.Z);
        Normalise(ref normal);
        Quaternion quat = MakeRotQuatFromSNormals(normal);
        CreateSMatrixFromQuat(quat, out rotW);

        var g = scene.VGrads;   // RotV = NormaliseMatrixToS(StepWidth, VGrads); scene.VGrads already is this.
        bool firstStepRandom = scene.AmbShadowFirstStepRandom;
        // Deterministic per-point RNG so the FSR jitter is reproducible (MB3D uses a thread RNG it
        // averages over the image). Seeded from the hit so neighbouring pixels differ.
        var rng = firstStepRandom ? new Random(HashSeed(hit.Position)) : null;

        // --- March each ray and record how open it is (CalcAmbShadowDE.pas:495-539).
        for (var i = 0; i < rayCount; i++)
        {
            SVec dir = rotM[i];
            RotateSVectorReverseS(ref dir, rotW);   // rotate hemisphere vec into the normal frame
            RotateSVectorWorld(ref dir, g);         // rotate into world space with VGrads (RotateSVectorS)

            double t = stepAO * dStepMul;
            double sTmp = 1;
            bool ended = false;
            bool firstStep = firstStepRandom;
            do
            {
                if (firstStep)
                {
                    firstStep = false;
                    t *= rng!.NextDouble() * 1.5 + S05;
                }
                else if (t > maxDist)
                {
                    t = maxDist;       // clamp so every ray reaches the same max distance (avoids artifacts)
                    ended = true;
                }

                var c = new Vec3d(hit.Position.X + dir.X * t, hit.Position.Y + dir.Y * t, hit.Position.Z + dir.Z * t);
                double de = Mb3dDistanceEstimator.Estimate(scene, c.X, c.Y, c.Z).Distance;

                // Outside-rendering accumulation.
                double candidate = (de - msDEstop + t * mDd10) / t;
                if (candidate < sTmp) sTmp = candidate;
                if (sTmp < S002) break;
                t += Math.Max(de, t * dStepMul);
            }
            while (!ended);

            minRA[i] = Math.Max(0, sTmp) * deMul;
        }

        // --- Overlap correction + accumulation (outside path, CalcAmbShadowDE.pas:548-568).
        double dAmount = 0;
        for (var i = 0; i < rayCount; i++)
        {
            SVec vi = rotM[i];
            sAdd[i] = 0;
            if (minRA[i] < 1)
            {
                double maxAdd = 1 - minRA[i];
                for (var j = 0; j < rayCount; j++)
                {
                    if (j == i) continue;
                    double dot = DotSVec(vi, rotM[j]);
                    if (dot > dMinADif)
                    {
                        double overlap = minRA[j] - ArcCosSafe(dot) * abr + 1;
                        if (overlap > 0) sAdd[i] += Math.Min(maxAdd, overlap) * correctionWeight;
                    }
                }
            }
        }
        for (var i = 0; i < rayCount; i++)
            dAmount += Math.Min(1, sAdd[i] + minRA[i]);

        int ambShadow = (int)Math.Round(16383 * (1 - dAmount / rayCount));
        if (ambShadow < 0) ambShadow = 0;
        if (ambShadow > 16383) ambShadow = 16383;
        return (ushort)ambShadow;
    }

    /// <summary>
    /// Converts the 0..16383 occlusion word into the scalar <c>dAmbSh</c> (1 = unoccluded), faithfully
    /// porting <c>calcAmbshadow</c> (PaintThread.pas:199-239). For amplitude &gt; 1 it interpolates
    /// between the linear value and its square; otherwise it just scales by amplitude.
    /// </summary>
    private static double CalcAmbShadow(ushort ambShadow, double amplitude)
    {
        const double inv16383 = 1.0 / 16383.0;
        double shadow = ambShadow >= 16383 ? 1.0 : ambShadow * inv16383;
        if (amplitude > 1)
        {
            double dAmbSh = 1 - shadow;
            return dAmbSh + (amplitude - 1) * (dAmbSh * dAmbSh - dAmbSh);
        }
        return 1 - amplitude * shadow;
    }

    // ----- Local 3-vector / matrix / quaternion types and the exact MB3D helpers they back. -----

    private struct SVec
    {
        public double X, Y, Z;
        public SVec(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    // Row-major 3x3, matching TSMatrix3 (M[row,col]); only the 3x3 block is used here.
    private struct SMatrix3
    {
        public double M00, M01, M02;
        public double M10, M11, M12;
        public double M20, M21, M22;
    }

    private struct Quaternion
    {
        public double X, Y, Z, W;
    }

    private static SVec RowZ(in SMatrix3 m) => new(m.M20, m.M21, m.M22);   // TSVec(RotW[2]) = row 2
    private static SVec ScaleSVec(in SVec v, double s) => new(v.X * s, v.Y * s, v.Z * s);
    private static double DotSVec(in SVec a, in SVec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static void Normalise(ref SVec v)
    {
        double len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        if (len > 0) { v.X /= len; v.Y /= len; v.Z /= len; }
    }

    // Math3D.pas:2497 BuildRotMatrixS (Euler XYZ).
    private static void BuildRotMatrixS(double xa, double ya, double za, out SMatrix3 m)
    {
        double sinX = Math.Sin(xa), cosX = Math.Cos(xa);
        double sinY = Math.Sin(ya), cosY = Math.Cos(ya);
        double sinZ = Math.Sin(za), cosZ = Math.Cos(za);
        m = new SMatrix3
        {
            M00 = cosY * cosZ,
            M01 = -cosY * sinZ,
            M02 = sinY,
            M10 = sinX * sinY * cosZ + cosX * sinZ,
            M11 = cosX * cosZ - sinX * sinY * sinZ,
            M12 = -sinX * cosY,
            M20 = sinX * sinZ - cosX * sinY * cosZ,
            M21 = cosX * sinY * sinZ + sinX * cosZ,
            M22 = cosX * cosY,
        };
    }

    // Math3D.pas:3083 MakeRotQuatFromSNormals: quaternion rotating (0,0,-1) onto NVec.
    private static Quaternion MakeRotQuatFromSNormals(in SVec nVec)
    {
        double half = ArcCosSafe(-nVec.Z) * 0.5;
        double sinA = Math.Sin(half), cosA = Math.Cos(half);
        double n = Math.Sqrt(nVec.Y * nVec.Y + nVec.X * nVec.X);
        var q = new Quaternion { Z = 0, W = cosA };
        if (n < 1e-25)
        {
            q.X = 0;
            q.Y = 0;
        }
        else
        {
            sinA /= n;
            q.X = -nVec.Y * sinA;
            q.Y = nVec.X * sinA;
        }
        return q;
    }

    // Math3D.pas:3153 CreateSMatrixFromQuat.
    private static void CreateSMatrixFromQuat(in Quaternion q, out SMatrix3 m)
    {
        m = new SMatrix3
        {
            M00 = 1.0 - 2.0 * (q.Y * q.Y + q.Z * q.Z),
            M01 = 2.0 * (q.X * q.Y + q.Z * q.W),
            M02 = 2.0 * (q.X * q.Z - q.Y * q.W),
            M10 = 2.0 * (q.X * q.Y - q.Z * q.W),
            M11 = 1.0 - 2.0 * (q.X * q.X + q.Z * q.Z),
            M12 = 2.0 * (q.Z * q.Y + q.X * q.W),
            M20 = 2.0 * (q.X * q.Z + q.Y * q.W),
            M21 = 2.0 * (q.Y * q.Z - q.X * q.W),
            M22 = 1.0 - 2.0 * (q.X * q.X + q.Y * q.Y),
        };
    }

    // Math3D.pas:1813 RotateSVectorReverseS: out[i] = sum_j M[i,j]*V[j]  (M . V).
    private static void RotateSVectorReverseS(ref SVec v, in SMatrix3 m)
    {
        double x = v.X * m.M00 + v.Y * m.M01 + v.Z * m.M02;
        double y = v.X * m.M10 + v.Y * m.M11 + v.Z * m.M12;
        double z = v.X * m.M20 + v.Y * m.M21 + v.Z * m.M22;
        v.X = x; v.Y = y; v.Z = z;
    }

    // Math3D.pas:1690 RotateSVectorS: out[j] = sum_i M[i,j]*V[i]  (M^T . V). Here M = RotV = VGrads
    // (row-major g[i*3+j]); identical to Mb3dCamera.CameraToWorld.
    private static void RotateSVectorWorld(ref SVec v, double[] g)
    {
        double x = v.X * g[0] + v.Y * g[3] + v.Z * g[6];
        double y = v.X * g[1] + v.Y * g[4] + v.Z * g[7];
        double z = v.X * g[2] + v.Y * g[5] + v.Z * g[8];
        v.X = x; v.Y = y; v.Z = z;
    }

    // Math3D.pas:1139 ArcCosSafe / ArcSinSafe.
    private static double ArcCosSafe(double d) =>
        d > DNearOne ? 0 : d < DNearMinusOne ? Pi : Math.Acos(d);

    private static double ArcSinSafe(double d) =>
        d > DNearOne ? Pi * 0.5 : d < DNearMinusOne ? Pi * -0.5 : Math.Asin(d);

    private static int HashSeed(in Vec3d p)
    {
        unchecked
        {
            long h = BitConverter.DoubleToInt64Bits(p.X);
            h = h * 1099511628211L ^ BitConverter.DoubleToInt64Bits(p.Y);
            h = h * 1099511628211L ^ BitConverter.DoubleToInt64Bits(p.Z);
            return (int)(h ^ (h >> 32));
        }
    }
}
