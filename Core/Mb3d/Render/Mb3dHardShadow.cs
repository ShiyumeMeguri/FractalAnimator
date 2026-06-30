namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>
/// Directional hard / soft shadows, a faithful port of <c>THardShadowCalcThread.calcHSsoft</c>
/// (CalcHardShadow.pas:132-258). For one HS-enabled light it marches a single ray through the DE
/// field from the surface point toward the light: if the ray reaches the DE surface before it
/// escapes, the point is shadowed. The returned value is the <i>soft</i> shadow factor MB3D packs
/// into <c>PsiLight.Shadow shr 10</c> (0..63) and the shader later applies as
/// <c>(Shadow shr 10) * (1/63)</c> (PaintThread.pas:522): 1 = fully lit, 0 = fully shadowed.
///
/// Frame handling: <see cref="Mb3dRenderLight.Direction"/> is the unit surface-&gt;light vector in
/// the camera/Vgrads frame (the same frame as <see cref="Mb3dHit.Normal"/>). MB3D builds its shadow
/// vector as <c>RotateVectorReverse(NormaliseSVectorTo(-StepWidth, LN), unitVGrads)</c>
/// (HeaderTrafos.pas:990); <see cref="Mb3dCamera.CameraToWorld"/> is exactly
/// <c>RotateVectorReverse</c> against the StepWidth-scaled basis, so applying it to the unit light
/// direction yields a world vector of length StepWidth pointing toward the light. The ray steps in
/// those StepWidth units, matching the marcher's <c>mZZ</c> step convention.
/// </summary>
public static class Mb3dHardShadow
{
    private const double S011 = 0.11;   // s011: minimum step width (TypeDefinitions.pas:1078)
    private const double S05 = 0.5;     // s05  (TypeDefinitions.pas:1082)
    private const double S1em30 = 1e-30;

    /// <summary>
    /// Returns the soft-shadow factor in [0,1] for one HS-enabled light at <paramref name="hit"/>
    /// (1 = lit, 0 = fully shadowed). Marches one ray; cost is one DE march per call.
    /// </summary>
    public static double Compute(in Mb3dHit hit, in Mb3dRenderLight light, Mb3dSceneParameters scene)
    {
        if (!hit.IsHit) return 1.0;

        // World-space shadow direction (surface->light, length StepWidth). See class remarks.
        var camera = GetCamera(scene);
        Vec3d hsVec = camera.CameraToWorld(light.Direction);   // length ~= StepWidth, toward the light

        // Geometric self-shadow: if the surface faces away from the light it is its own shadow caster.
        // MB3D tests Dot(NVec, -lightDir) > 0 (CalcHardShadow.pas:212); equivalently Dot(N, lightDir) <= 0.
        // Both the normal and the light direction live in the camera frame, so the test is frame-free.
        if (Vec3d.Dot(hit.Normal, light.Direction) <= 0)
            return 0.0;

        // --- Per-point thresholds, mirroring calcHSsoft setup (CalcHardShadow.pas:147-194). -------
        // ZRSmul: penumbra hardness. Global (non-positional) lights use 80 / SoftShadowRadius.
        double zrsMul = 80.0 / scene.SoftShadowRadius;

        double mZZ = hit.Depth;                              // converged surface depth (step units)
        double fovTerm = Math.Max(0.0, scene.FovY) / Math.Max(1, scene.Height);
        // MaxLHS (CalcHardShadow.pas:191): the per-pixel row factor (iMandWidth + y) is approximated
        // with the mid-screen row (y ~= Height/2) since the shadow ray's fixed signature has no column;
        // the depth term Min(mZZ, ZEnd*0.4) is still exact per pixel. MaxLHS is a soft cap + the
        // far-distance penumbra normaliser, not a hard correctness gate.
        double rowFactor = scene.Width + scene.Height * 0.5;
        double maxLHS = rowFactor * 0.6
                        * (1 + 0.5 * Math.Min(mZZ, scene.ZEnd * 0.4) * fovTerm)
                        * scene.HardShadowMaxLength;
        if (maxLHS <= 0) return 1.0;

        double dMaxL = maxLHS;

        // Back the ray start up by 0.1 step units along the VIEW ray (toward the camera), exactly as
        // MB3D (CalcHardShadow.pas:187-188): mZZ -= 0.1; C1 -= mVgradsFOV*0.1. This lifts the just-hit
        // surface a hair in front of the shadow-ray origin so the first DE sample (taken below, but NOT
        // occlusion-tested) is clear, then the full dMaxL budget is marched toward the light. hit.ViewVec
        // is the camera-frame view direction; CameraToWorld gives mVgradsFOV (world, length StepWidth).
        // This is the faithful self-shadow-acne avoidance; do NOT offset along the light direction.
        Vec3d viewWorld = camera.CameraToWorld(hit.ViewVec);
        Vec3d pos = new(
            hit.Position.X - viewWorld.X * 0.1,
            hit.Position.Y - viewWorld.Y * 0.1,
            hit.Position.Z - viewWorld.Z * 0.1);
        double mZZpullback = mZZ - 0.1;

        // ZZ2 tracks the marched depth so the (variable) DEstop can grow with depth, exactly as the
        // primary marcher. ZZ2mul is how fast depth changes per unit travel along the shadow ray
        // (CalcHardShadow.pas:207): -dot(hsVecUnit, viewDirUnit).
        double zz2 = mZZpullback;
        double zz2mul = -DotNormalized(hsVec, viewWorld);
        double dT1 = dMaxL;   // full budget, no subtraction (CalcHardShadow.pas:194)

        // --- March the shadow ray, tracking the minimum (DE - DEstop)/distance ratio (penumbra). The
        // first DE sample (here) is the loop's starting DE and is NOT tested for occlusion: the
        // occlusion test runs only after the first step toward the light (CalcHardShadow.pas:224-236). ---
        double zrSoft = 1.0;
        bool occluded = false;
        double msDEstop = scene.DEstop * (1 + Math.Abs(zz2) * scene.DEstopFactor);
        double rStepFactorDiff = 2.0;
        double maxStepCap = Math.Max(msDEstop, 0.4) * scene.MaxStepFactor;   // MaxCS(msDEstop,0.4)*mctMH04ZSD

        var de = Mb3dDistanceEstimator.Estimate(scene, pos.X, pos.Y, pos.Z);
        double dTmp = de.Distance;
        do
        {
            double lastDE = dTmp;
            // Step width (CalcHardShadow.pas:227): DE-scaled, clamped to [s011, maxStepCap].
            double stepW = (dTmp - scene.DEsub * msDEstop) * scene.ZStepDiv * rStepFactorDiff;
            if (stepW < S011) stepW = S011;
            if (stepW > maxStepCap) stepW = maxStepCap;

            dT1 -= stepW;
            pos = new Vec3d(pos.X + hsVec.X * stepW, pos.Y + hsVec.Y * stepW, pos.Z + hsVec.Z * stepW);
            zz2 += stepW * zz2mul;
            msDEstop = scene.DEstop * (1 + Math.Abs(zz2) * scene.DEstopFactor);
            maxStepCap = Math.Max(msDEstop, 0.4) * scene.MaxStepFactor;

            de = Mb3dDistanceEstimator.Estimate(scene, pos.X, pos.Y, pos.Z);
            dTmp = de.Distance;

            // Penumbra accumulation (CalcHardShadow.pas:234). travelled = dMaxL - dT1. Only meaningful
            // in soft mode, but cheap enough to always track.
            double travelled = dMaxL - dT1;
            double penumbra = (dTmp - msDEstop) * zrsMul / (travelled + S011)
                              + Pow8(travelled / maxLHS);
            if (penumbra < zrSoft) zrSoft = penumbra;

            // Surface reached while still inside the ray's range -> the light is occluded here. MB3D's
            // hard path flags the shadow only when dT1 is still > 0 at the hit (CalcHardShadow.pas:429).
            // (de.MaxIterationResult is the constant scene.MaxIterations for the numeric-DE path.)
            if (de.IterationCount >= de.MaxIterationResult || dTmp <= msDEstop)
            {
                if (dT1 > 0) occluded = true;
                break;
            }

            // Overshoot correction + step-rate adaption (CalcHardShadow.pas:237-244).
            if (dTmp > lastDE + stepW) dTmp = lastDE + stepW;
            if (lastDE > dTmp + S1em30)
            {
                double sT2 = stepW / (lastDE - dTmp);
                rStepFactorDiff = sT2 < 1 ? Math.Max(S05, sT2) : 1.0;
            }
            else rStepFactorDiff = 1.0;
        }
        while (dT1 >= 0);

        // Hard mode (Calc1HSsoft bit clear): the shadow is binary and total. The shader fully removes a
        // hard-shadowed light (PaintThread.pas:519 'if noHS then ...' skipped), so return 0 = remove.
        if (!scene.SoftShadow)
            return occluded ? 0.0 : 1.0;

        // Soft mode: the light always contributes, scaled by the penumbra factor MB3D stores as
        // (Shadow shr 10)/63 (PaintThread.pas:522). Clamp01(ZRsoft); MB3D quantises to /63, we keep
        // full precision in [0,1].
        if (zrSoft < 0) zrSoft = 0;
        if (zrSoft > 1) zrSoft = 1;
        return zrSoft;
    }

    // -DotOfVectorsNormalize equivalent: dot of the two vectors each normalised to unit length.
    private static double DotNormalized(Vec3d a, Vec3d b)
    {
        double la = a.Length(), lb = b.Length();
        if (la < S1em30 || lb < S1em30) return 0;
        return Vec3d.Dot(a, b) / (la * lb);
    }

    private static double Pow8(double v)
    {
        double v2 = v * v;       // Sqr(Sqr(Sqr(x)))
        double v4 = v2 * v2;
        return v4 * v4;
    }

    // One camera per scene is enough (it only reads scene constants). Cached so Compute stays cheap
    // when called once per light per pixel.
    [ThreadStatic] private static Mb3dCamera? _cachedCamera;
    [ThreadStatic] private static Mb3dSceneParameters? _cachedScene;

    private static Mb3dCamera GetCamera(Mb3dSceneParameters scene)
    {
        if (!ReferenceEquals(_cachedScene, scene))
        {
            _cachedScene = scene;
            _cachedCamera = new Mb3dCamera(scene);
        }
        return _cachedCamera!;
    }
}
