namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>
/// The alt-hybrid iteration driver, mirroring <c>doHybridPas</c> / <c>doHybridPasDE</c>
/// (formulas.pas:3690). Slots run as sequential blocks: slot n runs its <c>IterationCount</c> times,
/// then the next active slot, wrapping to <c>HybridRepeatFrom</c> past <c>HybridEnd</c>, until the
/// total iteration count reaches <paramref name="maxIterations"/> or the orbit escapes
/// (<c>|z|^2 &gt; escapeRadiusSquared</c>). Returns the final squared radius (<c>Rout</c>).
/// </summary>
public static class Mb3dIterator
{
    public static double Iterate(Mb3dSceneParameters scene, double startX, double startY, double startZ,
        int maxIterations, double escapeRadiusSquared, out int iterationCount)
    {
        var state = new Mb3dFormulaState
        {
            X = startX, Y = startY, Z = startZ, W = 1.0,
            FirstIteration = 0, VaryScale = 0,
        };
        // doHybridPas: x := C always; J := Julia constant (Julia) or C (Mandelbrot). Formulas that add
        // C read it from state.Cx/Cy/Cz.
        if (scene.IsJulia) { state.Cx = scene.Jx; state.Cy = scene.Jy; state.Cz = scene.Jz; }
        else { state.Cx = startX; state.Cy = startY; state.Cz = startZ; }

        var slots = scene.Slots;
        double rout = startX * startX + startY * startY + startZ * startZ;
        int n = scene.HybridStart;
        int btmp = slots[n].IterationCount;
        int count = 0;

        while (true)
        {
            while (btmp <= 0)
            {
                n++;
                if (n > scene.HybridEnd) n = scene.HybridRepeatFrom;
                btmp = slots[n].IterationCount;
            }
            slots[n].Step(ref state, slots[n].Parameters);
            btmp--;
            count++;
            rout = state.X * state.X + state.Y * state.Y + state.Z * state.Z;
            if (count >= maxIterations || rout > escapeRadiusSquared) break;
        }

        iterationCount = count;
        return rout;
    }
}
