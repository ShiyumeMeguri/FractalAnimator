namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>
/// Numeric central/forward-difference distance estimate, mirroring the non-custom-DE branch of
/// <c>CalcDEfull</c> (Calc.pas:546-603): iterate at the point for the base squared radius, then
/// sample the field at <c>+GradientOffset</c> along world X/Y/Z at a <i>fixed</i> iteration count,
/// and combine into <c>DE = Rout*ln(Rout)*scale / (sqrt(sum of squared deltas) + eps)</c>.
/// </summary>
public static class Mb3dDistanceEstimator
{
    public readonly record struct Result(double Distance, int IterationCount, int MaxIterationResult);

    public static Result Estimate(Mb3dSceneParameters scene, double cx, double cy, double cz)
    {
        double clampFloor = scene.DEstop * 0.25;
        double rBase = Mb3dIterator.Iterate(scene, cx, cy, cz, scene.MaxIterations, scene.EscapeRadiusSquared, out int itBase);
        int maxItResult = scene.MaxIterations;

        if (rBase < 1e-200)
            return new Result(clampFloor, itBase, maxItResult);

        // Gradient samples run a fixed number of iterations (the base count) with a huge escape radius
        // so they never bail early (CalcDEfull sets It3Dex.MaxIt := base count, It3Dex.RStop := Rstop3D).
        double off = scene.GradientOffset;
        double rx = Mb3dIterator.Iterate(scene, cx + off, cy, cz, itBase, scene.Rstop3D, out _);
        double ry = Mb3dIterator.Iterate(scene, cx, cy + off, cz, itBase, scene.Rstop3D, out _);
        double rz = Mb3dIterator.Iterate(scene, cx, cy, cz + off, itBase, scene.Rstop3D, out _);

        double dx = rBase - rx, dy = rBase - ry, dz = rBase - rz;
        double gradient = Math.Sqrt(dx * dx + dy * dy + dz * dz) + scene.DenomEpsilon;
        double distance = rBase * Math.Log(rBase) * scene.NumericDEscale / gradient;
        if (distance < clampFloor) distance = clampFloor;

        return new Result(distance, itBase, maxItResult);
    }
}
