using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>
/// Mutable per-iteration formula state for four points at once. Each <see cref="Vector256{Double}"/>
/// packs the same field of four independent orbits (lane 0..3), so a single SIMD formula call advances
/// four points in lockstep. This is the four-wide analogue of <see cref="Mb3dFormulaState"/>:
/// X,Y,Z,W are the iterated vector + running derivative, Cx,Cy,Cz the added constant, and VaryScale /
/// FirstIteration the Amazing Surf scale-vary carry. FirstIteration is a per-lane mask (all-zero =
/// not yet initialised, all-ones = initialised) so the one-time init can be done branchlessly.
/// </summary>
public struct Mb3dFormulaStateSimd
{
    public Vector256<double> X, Y, Z, W;     // the iterated vector + running derivative (W)
    public Vector256<double> Cx, Cy, Cz;     // the constant added by main formulas (It3D.J1/J2/J3)
    public Vector256<double> VaryScale;      // It3D.VaryScale: persistent scale for "scale vary"
    public Vector256<double> FirstIteration; // It3D.bFirstIt as a per-lane mask (0 = not yet inited)
}

/// <summary>
/// Four-wide SIMD (<see cref="Vector256{Double}"/>, four lanes) translations of the verified scalar
/// formulas in <see cref="Mb3dFormulas"/>. Each method is mathematically identical to its scalar twin
/// (which is the bit-exact x87 assembly port) but processes four independent points at once. Every
/// conditional in the scalar code is reproduced branchlessly with <see cref="Vector256"/> compares and
/// <see cref="Vector256.ConditionalSelect"/>; the formula parameters (<c>p.F64/F32/I32</c>) are scalar
/// (the same for all four lanes) and are broadcast with <see cref="Vector256.Create(double)"/>.
///
/// Lane convention: lane i is point i. The math is per-lane and never crosses lanes, so the four
/// results equal the four corresponding scalar results lane for lane.
/// </summary>
public static class Mb3dFormulasSimd
{
    /// <summary>True when the four-wide path can run; otherwise callers fall back to the scalar formulas.</summary>
    public static bool IsSupported => Avx.IsSupported;

    private static readonly Vector256<double> One = Vector256.Create(1.0);
    private static readonly Vector256<double> SignBit = Vector256.Create(-0.0); // 0x8000_0000_0000_0000 per lane

    /// <summary>Branchless absolute value: clear the sign bit of every lane.</summary>
    private static Vector256<double> Abs(Vector256<double> v) => Vector256.AndNot(v, SignBit);

    /// <summary>
    /// Box-fold one axis the way the scalar code does:
    /// <c>|c + fold| - |c - fold| - c</c>. <paramref name="fold"/> is the broadcast limit.
    /// </summary>
    private static Vector256<double> BoxFold(Vector256<double> c, Vector256<double> fold) =>
        Abs(c + fold) - Abs(c - fold) - c;

    /// <summary>
    /// Four-wide "Amazing Box" (#4 / HybridCubeDE), mirroring <see cref="Mb3dFormulas.AmazingBox"/>.
    /// Box fold each axis, sphere fold by MinR/Scale (branchless three-way select), scale, then add C.
    /// </summary>
    public static void AmazingBox(ref Mb3dFormulaStateSimd s, Mb3dFormulaParams p)
    {
        var fold = Vector256.Create(p.F64(-0x28));          // [base-40]
        Vector256<double> xf = BoxFold(s.X, fold);
        Vector256<double> yf = BoxFold(s.Y, fold);
        Vector256<double> zf = BoxFold(s.Z, fold);
        Vector256<double> r2 = xf * xf + yf * yf + zf * zf;

        var minR2 = Vector256.Create(p.F64(-0x20));         // [base-32] = MinR^2
        var scaleOverMinR2 = Vector256.Create(p.F64(-0x18)); // [base-24] = Scale/MinR^2
        var scale = Vector256.Create(p.F64(-0x10));         // [base-16] = Scale
        Vector256<double> mul = SphereFoldMul(r2, minR2, scaleOverMinR2, scale, scale / r2);

        s.W *= mul;
        s.X = xf * mul + s.Cx;
        s.Y = yf * mul + s.Cy;
        s.Z = zf * mul + s.Cz;
    }

    /// <summary>
    /// The shared sphere-fold three-way select used by Amazing Box / Amazing Surf, reproducing the
    /// scalar <c>if (r2 &lt; minR2) lo; else if (1 &lt; r2) hi; else mid</c> branchlessly. The selection
    /// order matches the scalar exactly: the <c>r2 &lt; minR2</c> case wins first, then <c>1 &lt; r2</c>,
    /// otherwise the <c>mid</c> value (typically <c>scale / r2</c>).
    /// </summary>
    private static Vector256<double> SphereFoldMul(
        Vector256<double> r2, Vector256<double> minR2,
        Vector256<double> lo, Vector256<double> hi, Vector256<double> mid)
    {
        // else-if chain built inside-out: start from mid, override with hi where 1 < r2, then override
        // with lo where r2 < minR2 (lo has top priority, exactly like the scalar's first branch).
        Vector256<double> result = Vector256.ConditionalSelect(Vector256.GreaterThan(r2, One), hi, mid);
        result = Vector256.ConditionalSelect(Vector256.LessThan(r2, minR2), lo, result);
        return result;
    }

    /// <summary>
    /// Four-wide "_SinY", mirroring <see cref="Mb3dFormulas.SinY"/>. The component picked by
    /// <c>Index</c> is a scalar parameter (the same for all lanes), so the component selection is a
    /// normal switch, not a per-lane blend; only the sine math is four-wide.
    /// </summary>
    public static void SinY(ref Mb3dFormulaStateSimd s, Mb3dFormulaParams p)
    {
        int index = p.I32(-0x0C) & 3;                       // [base-12] = Index (scalar, all lanes)
        var off1 = Vector256.Create((double)p.F32(-0x10));
        var scale1 = Vector256.Create((double)p.F32(-0x14));
        var scale2 = Vector256.Create((double)p.F32(-0x18));
        var off2 = Vector256.Create((double)p.F32(-0x1C));

        Vector256<double> c = index switch { 0 => s.X, 1 => s.Y, 2 => s.Z, _ => s.W };
        c = Sin((c - off1) * scale1) * scale2 + off2;
        switch (index)
        {
            case 0: s.X = c; break;
            case 1: s.Y = c; break;
            case 2: s.Z = c; break;
            default: s.W = c; break;
        }
    }

    /// <summary>
    /// Four-wide "_reciprocalZ3b", mirroring <see cref="Mb3dFormulas.ReciprocalZ3b"/>:
    /// <c>z' = sign(z) * (1/Lim1 + 1/Lim2 - 1/(|z*mul1| + Lim1) - 1/(|z*z*mul2| + Lim2))</c>. The
    /// original sign of z is restored by re-inserting its sign bit (the scalar's xor of the high byte).
    /// </summary>
    public static void ReciprocalZ3b(ref Mb3dFormulaStateSimd s, Mb3dFormulaParams p)
    {
        Vector256<double> z = s.Z;
        var lim1 = Vector256.Create(p.F64(-0x10));          // [base-16] = Limiter1
        var invLim1 = Vector256.Create(p.F64(-0x18));       // [base-24] = 1/Limiter1
        var lim2 = Vector256.Create(p.F64(-0x20));          // [base-32] = Limiter2
        var invLim2 = Vector256.Create(p.F64(-0x28));       // [base-40] = 1/Limiter2
        var mul1 = Vector256.Create(p.F64(-0x30));          // [base-48] = mul1
        var mul2 = Vector256.Create(p.F64(-0x38));          // [base-56] = mul2

        Vector256<double> v = invLim1 + invLim2
                              - One / (Abs(z * mul1) + lim1)
                              - One / (Abs(z * z * mul2) + lim2);

        // Re-apply the sign of the original z: keep |v| but take z's sign bit, exactly like the
        // scalar's `double.IsNegative(z) ? -v : v` (which is "abs(v) signed like z").
        Vector256<double> sign = Vector256.BitwiseAnd(z, SignBit);
        s.Z = Vector256.BitwiseOr(Abs(v), sign);
    }

    /// <summary>
    /// Four-wide "Amazing Surf", mirroring <see cref="Mb3dFormulas.AmazingSurf"/>. Tglad-fold x and y
    /// (z untouched), invert by a sphere or cylinder radius, scale (with the per-iteration scale-vary
    /// carry), add C, then apply the 3x3 rotation. The one-time scale-vary init and the sphere/cylinder
    /// choice are both per-lane-identical scalar parameters, but the init is still done branchlessly via
    /// the FirstIteration mask so the carried VaryScale matches the scalar lane for lane.
    /// </summary>
    public static void AmazingSurf(ref Mb3dFormulaStateSimd s, Mb3dFormulaParams p)
    {
        // Scale vary, carried across iterations. On the very first call FirstIteration is all-zero, so
        // ConditionalSelect picks the freshly broadcast Scale; afterwards it keeps the carried value.
        var scaleInit = Vector256.Create(p.F64(-0x10));     // Scale
        s.VaryScale = Vector256.ConditionalSelect(s.FirstIteration, s.VaryScale, scaleInit);
        s.FirstIteration = Vector256<double>.AllBitsSet;
        var scaleVary = Vector256.Create(p.F64(-0x54));     // Scale vary
        s.VaryScale += scaleVary * (Abs(s.VaryScale) - One);

        var fold = Vector256.Create(p.F64(-0x28));
        Vector256<double> xf = BoxFold(s.X, fold);
        Vector256<double> yf = BoxFold(s.Y, fold);

        Vector256<double> rr = xf * xf + yf * yf;
        if (p.I32(-0x58) == 0) rr += s.Z * s.Z;             // 0 = sphere (add z^2), else cylinder

        var minR2 = Vector256.Create(p.F64(-0x20));
        var scaleOverMinR2 = Vector256.Create(p.F64(-0x18)); // Scale/MinR^2 (static)
        Vector256<double> mul = SphereFoldMul(rr, minR2, scaleOverMinR2, s.VaryScale, s.VaryScale / rr);

        s.W *= mul;
        Vector256<double> nx = xf * mul + s.Cx;
        Vector256<double> ny = yf * mul + s.Cy;
        Vector256<double> nz = s.Z * mul + s.Cz;

        // Rotation matrix (row-major M00..M22); output = M * n. Scalars broadcast to all lanes.
        var m00 = Vector256.Create((double)p.F32(-0x2c));
        var m01 = Vector256.Create((double)p.F32(-0x30));
        var m02 = Vector256.Create((double)p.F32(-0x34));
        var m10 = Vector256.Create((double)p.F32(-0x38));
        var m11 = Vector256.Create((double)p.F32(-0x3c));
        var m12 = Vector256.Create((double)p.F32(-0x40));
        var m20 = Vector256.Create((double)p.F32(-0x44));
        var m21 = Vector256.Create((double)p.F32(-0x48));
        var m22 = Vector256.Create((double)p.F32(-0x4c));
        s.X = nx * m00 + ny * m01 + nz * m02;
        s.Y = nx * m10 + ny * m11 + nz * m12;
        s.Z = nx * m20 + ny * m21 + nz * m22;
    }

    /// <summary>
    /// Four-wide "JCube3", mirroring <see cref="Mb3dFormulas.JCube3"/>. Take |x|,|y|,|z|, run the exact
    /// 3-element compare/exchange sort network from the assembly (each swap is a branchless
    /// compare-exchange), then apply one of two affine folds chosen by a per-lane mask. The constants
    /// A1/A2/A3/BETA are scalar (built once, broadcast); only the sort, the branch decision, and the
    /// fold are per-lane.
    /// </summary>
    public static void JCube3(ref Mb3dFormulaStateSimd s, Mb3dFormulaParams p)
    {
        Vector256<double> a = Abs(s.X), b = Abs(s.Y), c = Abs(s.Z);

        // Sort network: exact transcription of the fcom/fxch sequence (st0=a, st1=b, st2=c). Each
        // scalar swap `if (!(cond)) (x,y) = (y,x)` becomes a branchless compare-exchange: swap the lanes
        // where the swap condition holds. The scalar condition is the negation of a strict compare, so
        // we compute the strict-compare mask and swap where it is FALSE (ConditionalSelect picks the
        // "true" operand where the mask bits are set).
        CompareExchangeWhereFalse(ref a, ref b, Vector256.LessThan(a, b)); // swap when !(a < b)
        CompareExchangeWhereFalse(ref a, ref c, Vector256.GreaterThanOrEqual(a, c)); // swap when !(a >= c)
        (a, b) = (b, a);                                                   // unconditional fxch st1
        CompareExchangeWhereFalse(ref a, ref c, Vector256.LessThan(a, c)); // swap when !(a < c)

        var alpha = Vector256.Create(p.F64(-0x10));
        var invAlpha = Vector256.Create(p.F64(-0x18));
        var gScale = Vector256.Create(p.F64(-0x50));
        // Locals exactly as the x87 prologue builds them (see the scalar version for the derivation):
        // A1 = Alpha + (GScale - 1), A2 = A1/Alpha, A3 = 1/A2, BETA = 1 - A3*GScale.
        Vector256<double> a1 = alpha + (gScale - One);
        Vector256<double> a2 = a1 * invAlpha;
        Vector256<double> a3 = One / a2;
        Vector256<double> beta = One - a3 * gScale;

        // d7 (C2/A1 fold) is taken when A3 < s0 OR s1 < s0 + beta. Build the per-lane decision mask.
        Vector256<double> branchC2 = Vector256.BitwiseOr(
            Vector256.LessThan(a3, a),
            Vector256.LessThan(b, a + beta));

        // Both affine folds computed for all lanes, then blended by the decision mask. C2 path uses
        // scale A1 around centre C2; C1 path uses scale A2 around centre C1.
        var c2x = Vector256.Create(p.F64(-0x38));
        var c2y = Vector256.Create(p.F64(-0x40));
        var c2z = Vector256.Create(p.F64(-0x48));
        var c1x = Vector256.Create(p.F64(-0x20));
        var c1y = Vector256.Create(p.F64(-0x28));
        var c1z = Vector256.Create(p.F64(-0x30));

        Vector256<double> aC2 = a1 * (a - c2x) + c2x;
        Vector256<double> bC2 = a1 * (b - c2y) + c2y;
        Vector256<double> cC2 = a1 * (c - c2z) + c2z;
        Vector256<double> aC1 = a2 * (a - c1x) + c1x;
        Vector256<double> bC1 = a2 * (b - c1y) + c1y;
        Vector256<double> cC1 = a2 * (c - c1z) + c1z;

        s.X = Vector256.ConditionalSelect(branchC2, aC2, aC1);
        s.Y = Vector256.ConditionalSelect(branchC2, bC2, bC1);
        s.Z = Vector256.ConditionalSelect(branchC2, cC2, cC1);
        s.W *= Vector256.ConditionalSelect(branchC2, a1, a2);
    }

    /// <summary>
    /// Branchless compare-exchange used by the JCube3 sort. The scalar swaps when a strict compare is
    /// false, i.e. <c>if (!(strict)) (x, y) = (y, x)</c>. <paramref name="strictMask"/> is the
    /// strict-compare result; we swap exactly the lanes where it is false. Implemented as a pair of
    /// <see cref="Vector256.ConditionalSelect"/>: where the mask is set we keep the originals, elsewhere
    /// we take the swapped operands.
    /// </summary>
    private static void CompareExchangeWhereFalse(
        ref Vector256<double> x, ref Vector256<double> y, Vector256<double> strictMask)
    {
        Vector256<double> newX = Vector256.ConditionalSelect(strictMask, x, y);
        Vector256<double> newY = Vector256.ConditionalSelect(strictMask, y, x);
        x = newX;
        y = newY;
    }

    /// <summary>
    /// Four-wide sine. There is no hardware double-precision SIMD sine, so this evaluates
    /// <see cref="Math.Sin(double)"/> per lane and repacks. The result is therefore bit-identical to the
    /// scalar path (same library call, same rounding) — important for matching the verified oracle.
    /// </summary>
    private static Vector256<double> Sin(Vector256<double> v) =>
        Vector256.Create(
            Math.Sin(v.GetElement(0)),
            Math.Sin(v.GetElement(1)),
            Math.Sin(v.GetElement(2)),
            Math.Sin(v.GetElement(3)));
}
