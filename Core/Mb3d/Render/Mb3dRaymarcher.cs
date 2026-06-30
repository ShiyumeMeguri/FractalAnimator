namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>The outcome of marching one ray.</summary>
public struct Mb3dHit
{
    public bool IsHit;
    public Vec3d Position;
    public double Depth;          // mZZ in step units
    public int IterationCount;
    public Vec3d Normal;          // world-space, unit length (outward)
}

/// <summary>
/// Single-ray distance-estimate marcher, mirroring MandCalc (CalcThread.pas:160-258): step by
/// DE*ratio (clamped), correct overshoot into the set, slow down when DE drops fast, optionally
/// binary-search the surface, then compute a finite-difference normal (RMCalculateNormals,
/// Calc.pas:794-831). DEstop is constant along the ray for these files (mctDEstopFactor = 0).
/// </summary>
public sealed class Mb3dRaymarcher
{
    private readonly Mb3dSceneParameters _scene;
    private readonly Mb3dCamera _camera;

    public Mb3dRaymarcher(Mb3dSceneParameters scene)
    {
        _scene = scene;
        _camera = new Mb3dCamera(scene);
    }

    private double DE(Vec3d p, out int itResult, out int maxItResult)
    {
        var r = Mb3dDistanceEstimator.Estimate(_scene, p.X, p.Y, p.Z);
        itResult = r.IterationCount;
        maxItResult = r.MaxIterationResult;
        return r.Distance;
    }

    public Mb3dHit March(int ix, int iy)
    {
        Vec3d origin = _camera.Origin(ix, iy);
        Vec3d dir = _camera.Direction(ix, iy);
        Vec3d pos = origin;
        double mZZ = 0;
        double msDEstop = _scene.DEstop;
        double rsfMul = 1;

        double dist = DE(pos, out int itResult, out int maxItResult);
        if (itResult >= maxItResult || dist < msDEstop)
            return Finish(pos, mZZ, itResult, dir);

        double lastStep = dist * _scene.ZStepDiv;
        do
        {
            if (itResult >= maxItResult)
            {
                double back = -0.5 * lastStep;
                mZZ += back;
                pos += dir * back;
                dist = DE(pos, out itResult, out maxItResult);
                lastStep = -back;
            }

            if (itResult < _scene.MinIterations || (itResult < maxItResult && dist >= msDEstop))
            {
                double lastDE = dist;
                double step = Math.Max(0.11, (dist - _scene.DEsub * msDEstop) * _scene.ZStepDiv * rsfMul);
                double maxStep = Math.Max(msDEstop, 0.4) * _scene.MaxStepFactor;
                if (maxStep < step) step = maxStep;
                lastStep = step;
                mZZ += step;
                pos += dir * step;
                dist = DE(pos, out itResult, out maxItResult);
                if (dist > lastDE + lastStep) dist = lastDE + lastStep;
                if (lastDE > dist + 1e-30)
                {
                    double ratio = lastStep / (lastDE - dist);
                    rsfMul = ratio < 1 ? Math.Max(0.5, ratio) : 1;
                }
                else rsfMul = 1;
            }
            else
            {
                if (_scene.DEAddSteps != 0)
                    BinarySearch(ref pos, ref mZZ, ref dist, ref itResult, ref maxItResult, lastStep, dir, msDEstop);
                return Finish(pos, mZZ, itResult, dir);
            }
        }
        while (mZZ <= _scene.ZEnd);

        return new Mb3dHit { IsHit = false, Depth = mZZ };
    }

    // RMdoBinSearch (Calc.pas:1672): secant/bisection toward |DE - msDEstop| < 0.001.
    private void BinarySearch(ref Vec3d pos, ref double mZZ, ref double dist, ref int itResult,
        ref int maxItResult, double lastStep, Vec3d dir, double msDEstop)
    {
        int remaining = _scene.DEAddSteps;
        double step = lastStep * -0.5;
        while (Math.Abs(dist - msDEstop) > 0.001)
        {
            mZZ += step;
            pos += dir * step;
            if (--remaining <= 0) break;
            dist = DE(pos, out itResult, out maxItResult);
            if (itResult >= maxItResult) step = -Math.Abs(step);
            else step = dist < msDEstop ? Math.Abs(step) * -0.55 : Math.Abs(step) * 0.55;
        }
    }

    private Mb3dHit Finish(Vec3d pos, double mZZ, int itResult, Vec3d dir)
    {
        double noffset = Math.Min(1.0, _scene.DEstop) * 0.15;
        var g = _scene.VGrads;
        var axis0 = new Vec3d(g[0], g[1], g[2]);
        var axis1 = new Vec3d(g[3], g[4], g[5]);
        var axis2 = new Vec3d(g[6], g[7], g[8]);

        double n2 = (DE(pos + axis2 * noffset, out _, out _) - DE(pos - axis2 * noffset, out _, out _)) * 0.5;
        double n0 = (DE(pos + axis0 * noffset, out _, out _) - DE(pos - axis0 * noffset, out _, out _)) * 0.5;
        double n1 = (DE(pos + axis1 * noffset, out _, out _) - DE(pos - axis1 * noffset, out _, out _)) * 0.5;

        // Lift the camera-frame gradient (n0,n1,n2) into world space via the unit camera axes.
        Vec3d u0 = axis0.Normalized(), u1 = axis1.Normalized(), u2 = axis2.Normalized();
        Vec3d normal = (u0 * n0 + u1 * n1 + u2 * n2).Normalized();
        // The DE gradient points outward (toward increasing distance); face it toward the camera.
        if (Vec3d.Dot(normal, dir) > 0) normal = normal * -1;

        return new Mb3dHit { IsHit = true, Position = pos, Depth = mZZ, IterationCount = itResult, Normal = normal };
    }
}
