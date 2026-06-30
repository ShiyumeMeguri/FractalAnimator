namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>Identifies which formula a hybrid slot runs (switch dispatch avoids per-iteration delegate calls).</summary>
public enum Mb3dFormulaKind
{
    None,
    AmazingBox,
    AmazingSurf,
    SinY,
    ReciprocalZ3b,
    JCube3,
}

/// <summary>One active hybrid slot: its iteration weight, packed parameters, and which formula it runs.</summary>
public sealed class Mb3dRenderFormula
{
    public int IterationCount;
    public Mb3dFormulaParams Parameters = null!;
    public Mb3dFormulaKind Kind;
}

/// <summary>
/// All per-frame render constants derived from a <see cref="Mb3dMandHeader"/> + its hybrid addon,
/// mirroring <c>GetMCTparasFromHeader</c> (HeaderTrafos.pas:513) and the camera setup
/// (HeaderTrafos.pas:850-965). Holds the camera basis, march thresholds, and the resolved formula
/// stack so the iterator / DE / raymarcher can run without touching the raw header again.
/// </summary>
public sealed class Mb3dSceneParameters
{
    public int Width;
    public int Height;

    // Camera (world space).
    public double StepWidth;                 // 2.1345 / (Zoom * Width)
    public double FovY;                      // radians
    public double PlanarOpticZ;              // theta / tan(theta)
    public double FovXOffset;                // 0.5 * Width (non-stereo)
    public double FovXMul;                   // FovY / Height
    public int CameraOptic;                  // 0 angular, 1 rectilinear, 2 panorama
    public readonly double[] VGrads = new double[9];  // rows rescaled to StepWidth length
    public readonly double[] Ystart = new double[3];

    // March thresholds.
    public double DEstop;                    // = msDEstop (constant; no depth growth for these files)
    public double ZStepDiv;                  // sZstepDiv
    public double MaxStepFactor;             // mctMH04ZSD
    public double DEsub;                     // msDEsub
    public int DEAddSteps;                   // binary-search refinement steps
    public double ZEnd;                      // (dZend - dZstart) / StepWidth, march length bound
    public bool FirstStepRandom;
    public double Zcorr;                      // depth-buffer nonlinearity (CalcPPZvals)
    public double ZcMul;                      // depth-buffer scale (CalcPPZvals)

    // Distance estimate (numeric gradient path).
    public double GradientOffset;            // world-space sampling offset = g0 * StepWidth
    public double NumericDEscale;            // g0
    public double DenomEpsilon;              // g0 * 0.06
    public double Rstop3D;                   // RStop^4 * 64 (escape during gradient sampling)

    // DE-based ambient occlusion (DEAO; bCalcAmbShadowAutomatic type 3). See Mb3dAmbientOcclusion.
    public bool AmbShadowEnabled;            // (CalcAmbShadowAutomatic and 1) <> 0
    public int AmbShadowQuality;             // (CalcAmbShadowAutomatic shr 4) and 3 -> ray pattern
    public bool AmbShadowFirstStepRandom;    // (CalcAmbShadowAutomatic and 128) <> 0 (FSR)
    public double DEAOmaxL;                   // sDEAOmaxL: max ray length multiplier (header.DEAOmaxL)
    public double AmbShadowAmplitude;         // sAmbShad = (TBpos[11] and $FF) / 53 (HeaderTrafos.pas:1317)
    public bool VaryDEstop;                   // bVaryDEstop = (VaryDEstopOnFOV <> 0)
    public double DEstopFactor;               // mctDEstopFactor: per-depth DEstop growth (GetDEstopFactor)

    // Iteration.
    public int MaxIterations;
    public int MinIterations;
    public double EscapeRadiusSquared;       // It3D.RStop = Sqr(header.RStop)
    public int HybridStart;
    public int HybridEnd;
    public int HybridRepeatFrom;
    public readonly Mb3dRenderFormula[] Slots = new Mb3dRenderFormula[6];

    // Julia.
    public bool IsJulia;
    public double Jx, Jy, Jz;

    // Lighting + color (raw parsed block; the shader reads ambient/lights/colors from here).
    public Mb3dLighting Lighting = null!;

    private const double Pid180 = Math.PI / 180.0;

    /// <summary>
    /// Builds the render constants. <paramref name="renderWidth"/>/<paramref name="renderHeight"/>
    /// override the header dimensions so the same view can be rendered at any resolution (the world
    /// extent is resolution-independent because Width*StepWidth is constant). Pass 0 to use the
    /// header's own size.
    /// </summary>
    public static Mb3dSceneParameters FromHeader(Mb3dMandHeader header, Mb3dHybridAddon addon,
        int renderWidth = 0, int renderHeight = 0)
    {
        int width = renderWidth > 0 ? renderWidth : header.Width;
        int height = renderHeight > 0 ? renderHeight : header.Height;
        var scene = new Mb3dSceneParameters { Width = width, Height = height };

        scene.StepWidth = 2.1345 / (header.Zoom * width);                        // DivUtils.pas:1029
        scene.CameraOptic = Math.Min(2, header.PlanarOptic & 3);
        scene.FovY = scene.CameraOptic == 2 ? Math.PI : header.FOVy * Pid180;
        double theta = Math.Min(1.5, Math.Max(0.01, scene.FovY * 0.5));
        scene.PlanarOpticZ = Math.Cos(theta) * theta / Math.Sin(theta);
        scene.FovXOffset = 0.5 * width;                                          // CalcXoff = 0.5 (non-stereo)
        scene.FovXMul = scene.CameraOptic == 2 ? (2 * Math.PI) / width : scene.FovY / height;

        // Camera basis: each hVGrads row rescaled to StepWidth length (NormaliseMatrixTo).
        for (var row = 0; row < 3; row++)
        {
            double m0 = header.VGrads[row * 3 + 0], m1 = header.VGrads[row * 3 + 1], m2 = header.VGrads[row * 3 + 2];
            double scale = scene.StepWidth / Math.Sqrt(m0 * m0 + m1 * m1 + m2 * m2);
            scene.VGrads[row * 3 + 0] = m0 * scale;
            scene.VGrads[row * 3 + 1] = m1 * scale;
            scene.VGrads[row * 3 + 2] = m2 * scale;
        }
        double z1 = (header.ZStart - header.ZMid) / scene.StepWidth;
        double halfW = scene.CameraOptic == 2 ? 0 : width * 0.5;
        double halfH = scene.CameraOptic == 2 ? 0 : height * 0.5;
        scene.Ystart[0] = header.XMid + z1 * scene.VGrads[6] - halfH * scene.VGrads[3] - halfW * scene.VGrads[0];
        scene.Ystart[1] = header.YMid + z1 * scene.VGrads[7] - halfH * scene.VGrads[4] - halfW * scene.VGrads[1];
        scene.Ystart[2] = header.ZMid + z1 * scene.VGrads[8] - halfH * scene.VGrads[5] - halfW * scene.VGrads[2];

        scene.DEstop = Math.Max(0.0001, header.DEstop);
        scene.ZStepDiv = Math.Max(0.0001, header.ZStepDiv);
        scene.MaxStepFactor = Math.Max(width, height) * 0.5
                              * Math.Sqrt(scene.ZStepDiv + 0.001) * Math.Max(0.01, header.RaystepLimiter);
        scene.DEsub = (header.Options & 4) == 0 ? 0 : Math.Min(0.9, Math.Sqrt(scene.ZStepDiv));
        scene.DEAddSteps = header.StepsAfterDEStop;
        scene.ZEnd = (header.ZEnd - header.ZStart) / scene.StepWidth;
        scene.FirstStepRandom = (header.Options & 1) != 0;

        // Depth-buffer mapping for fog (CalcPPZvals, DivUtils.pas:1016).
        scene.Zcorr = Math.Sin(Math.Max(scene.FovY, 1.0) / height);
        scene.ZcMul = 32767.0 * 256.0 / (Math.Sqrt(scene.ZEnd * scene.Zcorr + 1) - 0.999999999);

        double g0 = Math.Min(scene.DEstop * 0.1, 0.004);
        scene.NumericDEscale = g0;
        scene.DenomEpsilon = g0 * 0.06;
        scene.GradientOffset = g0 * scene.StepWidth;

        scene.MaxIterations = header.Iterations;
        scene.MinIterations = Math.Min(header.Iterations, header.MinimumIterations);
        double dRstop = header.RStop * header.RStop;
        scene.EscapeRadiusSquared = dRstop;
        scene.Rstop3D = dRstop * dRstop * 64.0;

        scene.IsJulia = header.IsJulia != 0;
        scene.Jx = header.Jx; scene.Jy = header.Jy; scene.Jz = header.Jz;
        scene.Lighting = header.Light;

        // DEAO constants (HeaderTrafos.pas:567-569, 1317; CalcAmbShadowDE.pas:63-69).
        scene.AmbShadowEnabled = (header.CalcAmbShadowAutomatic & 1) != 0;
        scene.AmbShadowQuality = (header.CalcAmbShadowAutomatic >> 4) & 3;
        scene.AmbShadowFirstStepRandom = (header.CalcAmbShadowAutomatic & 128) != 0;
        scene.DEAOmaxL = header.DEAOmaxL;
        scene.AmbShadowAmplitude = (header.Light.TrackbarPos[8] & 0xFF) / 53.0;  // TBpos[11] = TrackbarPos[8]
        scene.VaryDEstop = header.VaryDEstopOnFOV != 0;
        // mctDEstopFactor = GetDEstopFactor (HeaderTrafos.pas:58-72): stepwidth growth per unit depth.
        scene.DEstopFactor = 0;
        if (scene.VaryDEstop)
        {
            double ze = Math.Max(1e-16, scene.ZEnd);
            double sin001 = Math.Sin(0.01);
            double x1 = scene.CameraOptic == 2
                ? 0.01 * height / (sin001 * Math.PI)
                : 0.01 * height / (sin001 * Math.Max(1.0 / 65535.0, header.FOVy * Pid180));
            double x2 = scene.StepWidth * (x1 + ze) / x1;
            scene.DEstopFactor = (x2 - scene.StepWidth) / (scene.StepWidth * Math.Max(1.0 / 6.0, ze));
        }

        // Hybrid slot resolution (alt mode). nHybrid[n] = iterationCount; inactive slots are skipped.
        int lastActive = 0;
        for (var i = 0; i < 6; i++)
        {
            var slot = addon.Slots[i];
            bool active = slot.IterationCount > 0;
            scene.Slots[i] = new Mb3dRenderFormula
            {
                IterationCount = slot.IterationCount,
                // Inactive slots are never executed, so skip packing their parameters (which may use
                // option types not needed by any active formula, e.g. _Rotate4d's 4x4 rotation).
                Parameters = active
                    ? Mb3dFormulaParams.Build(slot.OptionTypes, slot.OptionValues, slot.OptionCount)
                    : Mb3dFormulaParams.Build([], [], 0),
                Kind = active ? ResolveFormula(slot) : Mb3dFormulaKind.None,
            };
            if (active) lastActive = i;
        }
        scene.HybridStart = 0;
        scene.HybridEnd = (addon.HybridOption1 & 7) != 0 ? addon.HybridOption1 & 7 : lastActive;
        scene.HybridRepeatFrom = addon.HybridOption1 >> 4;
        if (scene.HybridEnd < lastActive) scene.HybridEnd = lastActive;
        if (scene.HybridRepeatFrom > scene.HybridEnd) scene.HybridRepeatFrom = 0;

        return scene;
    }

    private static Mb3dFormulaKind ResolveFormula(Mb3dFormulaSlot slot)
    {
        // Built-in #4 is "Amazing Box"; the rest are matched by name (CustomFtoStr style).
        string key = slot.Name.Replace(" ", "").ToUpperInvariant();
        return key switch
        {
            "AMAZINGBOX" => Mb3dFormulaKind.AmazingBox,
            "AMAZINGSURF" => Mb3dFormulaKind.AmazingSurf,
            "_SINY" => Mb3dFormulaKind.SinY,
            "_RECIPROCALZ3B" => Mb3dFormulaKind.ReciprocalZ3b,
            "JCUBE3" => Mb3dFormulaKind.JCube3,
            _ => Mb3dFormulaKind.None, // unsupported -> inactive
        };
    }
}
