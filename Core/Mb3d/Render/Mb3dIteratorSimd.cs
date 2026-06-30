using System.Runtime.Intrinsics;

namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>
/// Four-wide fixed-count hybrid iteration driver: the SIMD analogue of <see cref="Mb3dIterator"/> for
/// the case the numeric distance estimate needs — a <i>fixed</i> iteration count with no early escape
/// (the gradient samples in <c>CalcDEfull</c> set a huge RStop so they always run the full count).
///
/// It packs up to four independent start points into the four lanes and runs the exact same slot
/// schedule as the scalar driver, calling the branchless SIMD formulas. Because there is no per-lane
/// escape, the lanes never diverge in control flow, so one pass advances all four points and the final
/// squared radius of each lane equals the scalar result for that point.
/// </summary>
public static class Mb3dIteratorSimd
{
    /// <summary>True when the SIMD formulas can run; mirrors <see cref="Mb3dFormulasSimd.IsSupported"/>.</summary>
    public static bool IsSupported => Mb3dFormulasSimd.IsSupported;

    /// <summary>
    /// Runs the hybrid schedule for exactly <paramref name="fixedIterations"/> iterations on four points
    /// (lane 0..3), returning each lane's final squared radius <c>Rout = x^2 + y^2 + z^2</c>. The
    /// per-pixel constant C is taken from the start point of each lane for the Mandelbrot case, or from
    /// the scene's Julia constant (broadcast) for the Julia case — exactly as the scalar driver does.
    /// </summary>
    public static Vector256<double> IterateFixed(Mb3dSceneParameters scene,
        Vector256<double> startX, Vector256<double> startY, Vector256<double> startZ, int fixedIterations)
    {
        var state = new Mb3dFormulaStateSimd
        {
            X = startX, Y = startY, Z = startZ,
            W = Vector256.Create(1.0),
            FirstIteration = Vector256<double>.Zero,
            VaryScale = Vector256<double>.Zero,
        };
        if (scene.IsJulia)
        {
            state.Cx = Vector256.Create(scene.Jx);
            state.Cy = Vector256.Create(scene.Jy);
            state.Cz = Vector256.Create(scene.Jz);
        }
        else
        {
            state.Cx = startX; state.Cy = startY; state.Cz = startZ;
        }

        if (fixedIterations <= 0)
            return startX * startX + startY * startY + startZ * startZ;

        var slots = scene.Slots;
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
            var slot = slots[n];
            switch (slot.Kind)
            {
                case Mb3dFormulaKind.AmazingBox: Mb3dFormulasSimd.AmazingBox(ref state, slot.Parameters); break;
                case Mb3dFormulaKind.AmazingSurf: Mb3dFormulasSimd.AmazingSurf(ref state, slot.Parameters); break;
                case Mb3dFormulaKind.SinY: Mb3dFormulasSimd.SinY(ref state, slot.Parameters); break;
                case Mb3dFormulaKind.ReciprocalZ3b: Mb3dFormulasSimd.ReciprocalZ3b(ref state, slot.Parameters); break;
                case Mb3dFormulaKind.JCube3: Mb3dFormulasSimd.JCube3(ref state, slot.Parameters); break;
            }
            btmp--;
            count++;
            if (count >= fixedIterations) break;
        }

        return state.X * state.X + state.Y * state.Y + state.Z * state.Z;
    }
}
