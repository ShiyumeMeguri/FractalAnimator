namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>
/// Mutable per-iteration formula state: the vector being iterated, the running derivative, the
/// per-pixel constant C, and the scratch fields some formulas keep across iterations (the Amazing
/// Surf / ATetraVS "scale vary" carries state in It3D at +200/+208).
/// </summary>
public struct Mb3dFormulaState
{
    public double X, Y, Z, W;     // the iterated vector + running derivative (W)
    public double Cx, Cy, Cz;     // the constant added by main formulas (It3D.J1/J2/J3)
    public double VaryScale;      // It3D.VaryScale (+200): persistent scale for "scale vary"
    public int FirstIteration;    // It3D.bFirstIt (+208): 0 until the first scale-vary init
}

/// <summary>
/// Faithful C# translations of the Mandelbulb3D formula iteration bodies, each mirroring the x87
/// assembly in the corresponding <c>.m3f</c> <c>[CODE]</c> block (or the built-in Pascal body). Every
/// method transforms the vector in <see cref="Mb3dFormulaState"/> in place, exactly as the assembly
/// mutates the doubles at <c>@x/@y/@z</c> and the derivative at <c>@z+8</c>.
///
/// Source map: Amazing Box = HybridCubeDE (formulas.pas:4932); _SinY / _reciprocalZ3b / JCube3 /
/// Amazing Surf = the disassembled [CODE] blocks (M3Formulas\*.m3f).
/// </summary>
public static class Mb3dFormulas
{
    /// <summary>
    /// Built-in "Amazing Box" (#4 / HybridCubeDE). Box fold each axis with limit <c>Fold</c>, sphere
    /// fold by <c>MinR</c>/<c>Scale</c>, scale, then add C. Box fold (formulas.pas:4942-4968):
    /// <c>cf = |c+fold| - |c-fold| - c</c>.
    /// </summary>
    public static void AmazingBox(ref Mb3dFormulaState s, Mb3dFormulaParams p)
    {
        double fold = p.F64(-0x28);          // [base-40]
        double xf = Math.Abs(s.X + fold) - Math.Abs(s.X - fold) - s.X;
        double yf = Math.Abs(s.Y + fold) - Math.Abs(s.Y - fold) - s.Y;
        double zf = Math.Abs(s.Z + fold) - Math.Abs(s.Z - fold) - s.Z;
        double r2 = xf * xf + yf * yf + zf * zf;

        double minR2 = p.F64(-0x20);         // [base-32] = MinR^2
        double mul;
        if (r2 < minR2) mul = p.F64(-0x18);  // [base-24] = Scale/MinR^2
        else if (1.0 < r2) mul = p.F64(-0x10); // [base-16] = Scale
        else mul = p.F64(-0x10) / r2;        // Scale/r^2

        s.W *= mul;
        s.X = xf * mul + s.Cx;
        s.Y = yf * mul + s.Cy;
        s.Z = zf * mul + s.Cz;
    }

    /// <summary>
    /// "_SinY" transform (M3Formulas\_SinY.m3f): on the vector component selected by <c>Index</c>,
    /// <c>c = sin((c - offset1)*scale1)*scale2 + offset2</c>. Offsets/scales are 4-byte singles.
    /// </summary>
    public static void SinY(ref Mb3dFormulaState s, Mb3dFormulaParams p)
    {
        int index = p.I32(-0x0C) & 3;        // [base-12] = Index
        double c = index switch { 0 => s.X, 1 => s.Y, 2 => s.Z, _ => s.W };
        c = Math.Sin((c - p.F32(-0x10)) * p.F32(-0x14)) * p.F32(-0x18) + p.F32(-0x1C);
        switch (index)
        {
            case 0: s.X = c; break;
            case 1: s.Y = c; break;
            case 2: s.Z = c; break;
            default: s.W = c; break;
        }
    }

    /// <summary>
    /// "_reciprocalZ3b" transform (M3Formulas\_reciprocalZ3b.m3f) on z:
    /// <c>z' = sign(z) * (1/Lim1 + 1/Lim2 - 1/(|z*mul1| + Lim1) - 1/(|z*z*mul2| + Lim2))</c>.
    /// The original sign of z is captured (high byte) and re-applied by xor at the end.
    /// </summary>
    public static void ReciprocalZ3b(ref Mb3dFormulaState s, Mb3dFormulaParams p)
    {
        double z = s.Z;
        double lim1 = p.F64(-0x10);          // [base-16] = Limiter1
        double invLim1 = p.F64(-0x18);       // [base-24] = 1/Limiter1
        double lim2 = p.F64(-0x20);          // [base-32] = Limiter2
        double invLim2 = p.F64(-0x28);       // [base-40] = 1/Limiter2
        double mul1 = p.F64(-0x30);          // [base-48] = mul1
        double mul2 = p.F64(-0x38);          // [base-56] = mul2

        double v = invLim1 + invLim2
                   - 1.0 / (Math.Abs(z * mul1) + lim1)
                   - 1.0 / (Math.Abs(z * z * mul2) + lim2);
        s.Z = double.IsNegative(z) ? -v : v;
    }

    /// <summary>
    /// "Amazing Surf" (M3Formulas\Amazing Surf.m3f). Tglad-fold x and y (z untouched), invert by a
    /// sphere (or cylinder) radius, scale (with optional per-iteration "scale vary"), add C, then
    /// apply a 3x3 rotation. Mirrors the disassembled [CODE].
    /// </summary>
    public static void AmazingSurf(ref Mb3dFormulaState s, Mb3dFormulaParams p)
    {
        // Scale vary, carried in It3D across iterations (no-op when Scale vary = 0).
        if (s.FirstIteration == 0)
        {
            s.FirstIteration = 1;
            s.VaryScale = p.F64(-0x10);                 // Scale
        }
        s.VaryScale += p.F64(-0x54) * (Math.Abs(s.VaryScale) - 1.0); // Scale vary

        double fold = p.F64(-0x28);
        double xf = Math.Abs(s.X + fold) - Math.Abs(s.X - fold) - s.X;
        double yf = Math.Abs(s.Y + fold) - Math.Abs(s.Y - fold) - s.Y;

        double rr = xf * xf + yf * yf;
        if (p.I32(-0x58) == 0) rr += s.Z * s.Z;          // 0 = sphere, else cylinder

        double minR2 = p.F64(-0x20);
        double mul;
        if (rr < minR2) mul = p.F64(-0x18);              // Scale/MinR^2 (static)
        else if (1.0 < rr) mul = s.VaryScale;            // VaryScale
        else mul = s.VaryScale / rr;                     // VaryScale/rr

        s.W *= mul;
        double nx = xf * mul + s.Cx;
        double ny = yf * mul + s.Cy;
        double nz = s.Z * mul + s.Cz;

        // Rotation matrix (row-major M00..M22 at [-0x2c .. -0x4c]); output = M * n.
        double m00 = p.F32(-0x2c), m01 = p.F32(-0x30), m02 = p.F32(-0x34);
        double m10 = p.F32(-0x38), m11 = p.F32(-0x3c), m12 = p.F32(-0x40);
        double m20 = p.F32(-0x44), m21 = p.F32(-0x48), m22 = p.F32(-0x4c);
        s.X = nx * m00 + ny * m01 + nz * m02;
        s.Y = nx * m10 + ny * m11 + nz * m12;
        s.Z = nx * m20 + ny * m21 + nz * m22;
    }

    /// <summary>
    /// "JCube3" (M3Formulas\JCube3.m3f) — a brute-force Jerusalem-cube IFS. Take |x|,|y|,|z|, run the
    /// exact 3-element compare/exchange network from the assembly, then apply one of two affine folds
    /// (around C1 with scale A2, or around C2 with scale A1) chosen by thresholds derived from Alpha
    /// and GScale. The folded triple is written back to x,y,z (the sort is intentionally not undone —
    /// this is the "discontinuous" approximation noted in the formula description).
    /// </summary>
    public static void JCube3(ref Mb3dFormulaState s, Mb3dFormulaParams p)
    {
        double a = Math.Abs(s.X), b = Math.Abs(s.Y), c = Math.Abs(s.Z);
        // Sort network: exact transcription of the fcom/fxch sequence (st0=a, st1=b, st2=c).
        if (!(a < b)) (a, b) = (b, a);     // fxch st1 when a >= b
        if (!(a >= c)) (a, c) = (c, a);    // fxch st2 when a < c
        (a, b) = (b, a);                   // unconditional fxch st1
        if (!(a < c)) (a, c) = (c, a);     // fxch st2 when a >= c

        double alpha = p.F64(-0x10);
        double invAlpha = p.F64(-0x18);
        double gScale = p.F64(-0x50);
        // Locals exactly as the x87 prologue builds them. The 0x024 `fsubr st(1)` is the d8/5 form
        // (st0 = st1 - st0), and [esp+0x18] transiently holds -GScale before being overwritten by
        // BETA, so the constants are: A1 = Alpha + (GScale-1), A2 = A1/Alpha, A3 = 1/A2,
        // BETA = 1 - A3*GScale.
        double a1 = alpha + (gScale - 1.0);       // [esp]
        double a2 = a1 * invAlpha;                 // [esp+8] = A1/Alpha
        double a3 = 1.0 / a2;                      // [esp+0x10]
        double beta = 1.0 - a3 * gScale;           // [esp+0x18]

        // Decisions are fcompp(A3 vs s0) and fcompp(s1 vs s0+beta); the shr-ah/jbe idiom resolves to
        // the C0 "<" test. d7 (C2/A1 fold) is taken when A3 < s0 OR s1 < s0+beta.
        bool branchC2 = (a3 < a) || (b < a + beta);
        if (branchC2)
        {
            double cx = p.F64(-0x38), cy = p.F64(-0x40), cz = p.F64(-0x48); // C2 (Cx2,Cy2,Cz2)
            a = a1 * (a - cx) + cx;
            b = a1 * (b - cy) + cy;
            c = a1 * (c - cz) + cz;
            s.W *= a1;
        }
        else
        {
            double cx = p.F64(-0x20), cy = p.F64(-0x28), cz = p.F64(-0x30); // C1 (Cx1,Cy1,Cz1)
            a = a2 * (a - cx) + cx;
            b = a2 * (b - cy) + cy;
            c = a2 * (c - cz) + cz;
            s.W *= a2;
        }
        s.X = a;
        s.Y = b;
        s.Z = c;
    }

    /// <summary>
    /// Verification-only translation of "_AmazingBox.m3f" (the box without adding C, fixed fold = 1).
    /// Shares the box-fold + sphere-fold core with <see cref="AmazingBox"/>; used to validate that
    /// core against the runnable .m3f assembly oracle.
    /// </summary>
    public static void AmazingBoxNoC(ref Mb3dFormulaState s, Mb3dFormulaParams p)
    {
        const double fold = 1.0;
        double xf = Math.Abs(s.X + fold) - Math.Abs(s.X - fold) - s.X;
        double yf = Math.Abs(s.Y + fold) - Math.Abs(s.Y - fold) - s.Y;
        double zf = Math.Abs(s.Z + fold) - Math.Abs(s.Z - fold) - s.Z;
        double r2 = xf * xf + yf * yf + zf * zf;

        double minR2 = p.F64(-0x20);
        double mul;
        if (r2 < minR2) mul = p.F64(-0x18);
        else if (1.0 < r2) mul = p.F64(-0x10);
        else mul = p.F64(-0x10) / r2;

        s.W *= mul;
        s.X = xf * mul;
        s.Y = yf * mul;
        s.Z = zf * mul;
    }
}
