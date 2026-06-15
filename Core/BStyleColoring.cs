namespace FractalAnimator.Core;

/// <summary>
/// The parameters of one FractalParadise material (Fractal.shader properties). Coordinates use the
/// same convention as the shader: the Julia constant is <c>(_CX, _CY) * 1e-5</c>.
/// </summary>
public sealed class ShaderColorParams
{
    public ShaderFractalType Type = ShaderFractalType.Mandelbrot;
    public double PowerReal = 2.0;
    public double PowerImag = 0.0;
    public bool Reverse;
    public bool UseJulia;
    public double JuliaCXRaw = 1.0;   // _CX (scaled by 1e-5 inside)
    public double JuliaCYRaw = 1.0;   // _CY
    public double LeafOrbitOffset = 1.0; // also serves as Newton's _FlowLight
    public double ColorR = 10.0;
    public double ColorG = 2.5;
    public double ColorB = 0.75;
    public int Iterations = 500;
}

/// <summary>
/// Faithful 1:1 port of FractalParadise's orbit-trap coloring.
/// Source: Assets/Fractal.shader (<c>calculateFractalColorBStyle</c>, default "B-style"),
/// Assets/NewtonFractal01.shader (<c>calculateFractalColorBStyle</c> for the Newton variant) and
/// Assets/FractalArt/NewtonFractalFlower.shader (step coloring). Returns linear RGB in [0, 1].
///
/// One deliberate deviation: where the shader feeds <c>dot(z,z) &lt; 1</c> into <c>log2(log2(...))</c>
/// (NaN on the GPU, which simply saturates), the CPU clamps to just above 1 — same intent, no NaN.
/// </summary>
public static class BStyleColoring
{
    const double TwoPi = 6.283185307179586;

    public static (double R, double G, double B) Color(Cplx c, ShaderColorParams p) => p.Type switch
    {
        ShaderFractalType.Newton => Newton(c, p),
        ShaderFractalType.NewtonFlower => NewtonFlower(c, p),
        _ => BStyle(c, p),
    };

    // Fractal.shader:77 calculateFractalColorBStyle.
    static (double, double, double) BStyle(Cplx c, ShaderColorParams p)
    {
        if (p.Reverse)
        {
            var denom = c.Re * c.Re + c.Im * c.Im;
            c = new Cplx(c.Re / denom, c.Im / denom);
        }

        var z = c;
        var juliaConst = p.UseJulia ? new Cplx(p.JuliaCXRaw * 1e-5, p.JuliaCYRaw * 1e-5) : c;
        var outerDist = 1e5;
        const double boundSquared = 1024.0;
        var smoothOffset = -Math.Log2(Math.Log2(32.0)) - 1.0;

        var iters = 1;
        for (; iters < p.Iterations; iters++)
        {
            z = ShaderFractal.Apply(p.Type, z, juliaConst, p.PowerReal, p.PowerImag);
            if (z.Dot > boundSquared) break;
            outerDist = Math.Min(outerDist, OuterTrap(z, p.LeafOrbitOffset));
        }

        var zzFinal = Math.Max(z.Dot, 1.0000001);
        var smoothIters = iters - (Math.Log2(Math.Log2(zzFinal)) + smoothOffset);
        var t = -0.25 * outerDist + 0.9 * Math.Log2(smoothIters) + 0.5;
        return ColorBand(t, p.ColorR, p.ColorG, p.ColorB);
    }

    // NewtonFractal01.shader:145 calculateFractalColorBStyle (Newton variant; _FlowLight == LeafOrbitOffset).
    static (double, double, double) Newton(Cplx c, ShaderColorParams p)
    {
        if (p.Reverse)
        {
            var denom = c.Re * c.Re + c.Im * c.Im;
            if (denom > 1e-14) c = new Cplx(c.Re / denom, c.Im / denom);
        }

        var z = c;
        var outerDist = 1e5;
        var converged = false;
        var iterIndex = 0;
        var smoothOffset = -1.0 - Math.Log2(Math.Log2(32.0));

        for (var i = 0; i < p.Iterations; i++)
        {
            z = ShaderFractal.NewtonStep(z);
            outerDist = Math.Min(outerDist, OuterTrap(z, p.LeafOrbitOffset));
            var value = ShaderFractal.NewtonFunc(z);
            if (Math.Sqrt(value.Dot) < 1e-6)
            {
                converged = true;
                iterIndex = i;
                break;
            }
            if (z.Dot > 1e8)
            {
                iterIndex = i;
                break;
            }
            iterIndex = i;
        }

        if (converged && iterIndex < p.Iterations)
            return (0, 0, 0); // in-set is painted black (shader overwrites finalColor to 0)

        var zz = Math.Max(z.Dot, 1.000001);
        var smoothIters = iterIndex - (Math.Log2(Math.Log2(zz)) + smoothOffset);
        var t = -0.25 * outerDist + 0.9 * Math.Log2(smoothIters) + 0.5;
        return ColorBand(t, 10.0, 2.5, 0.75); // out-set uses fixed exponents in the shader
    }

    // NewtonFractalFlower.shader:89 frag — step-count coloring (B channel is always 0).
    static (double, double, double) NewtonFlower(Cplx c, ShaderColorParams p)
    {
        var step = ShaderFractal.NewtonFlowerSteps(c);
        if (step < 0) step = 10; // _214_step fallback when it never converges
        var t = Frac(step / 20.0);
        var value = Frac(t * t * 0.8);
        return (value * p.ColorR, value * p.ColorG, 0.0);
    }

    /// <summary>
    /// Escape iteration count for one point (the B-style bound is 32, i.e. |z|² &gt; 1024). Returns
    /// <paramref name="maxIters"/> for points that stay bounded. Used to locate boundary centers worth
    /// zooming into (high counts sit on the fractal edge; flat regions go to the void).
    /// </summary>
    public static int EscapeIteration(Cplx c, ShaderColorParams p, int maxIters)
    {
        if (p.Reverse)
        {
            var denom = c.Re * c.Re + c.Im * c.Im;
            c = new Cplx(c.Re / denom, c.Im / denom);
        }
        var z = c;
        var juliaConst = p.UseJulia ? new Cplx(p.JuliaCXRaw * 1e-5, p.JuliaCYRaw * 1e-5) : c;
        for (var i = 1; i < maxIters; i++)
        {
            z = ShaderFractal.Apply(p.Type, z, juliaConst, p.PowerReal, p.PowerImag);
            if (z.Dot > 1024.0) return i;
        }
        return maxIters;
    }

    /// <summary>Leaf orbit-trap value for a reconstructed orbit point (used by the deep-zoom engine).</summary>
    public static double LeafTrapValue(double zReal, double zImag, double offset) =>
        OuterTrap(new Cplx(zReal, zImag), offset);

    /// <summary>
    /// Final B-style color from an accumulated orbit-trap minimum and the (smooth) escape iteration —
    /// the shared tail of <see cref="BStyle"/>, reused by the perturbation deep-zoom path.
    /// </summary>
    public static (double R, double G, double B) ColorFromTrap(double outerDist, int iterations,
        double finalMagnitudeSquared, double colorR, double colorG, double colorB)
    {
        var smoothOffset = -Math.Log2(Math.Log2(32.0)) - 1.0;
        var zz = Math.Max(finalMagnitudeSquared, 1.0000001);
        var smoothIters = iterations - (Math.Log2(Math.Log2(zz)) + smoothOffset);
        var t = -0.25 * outerDist + 0.9 * Math.Log2(smoothIters) + 0.5;
        return ColorBand(t, colorR, colorG, colorB);
    }

    // The nested-log "leaf" orbit trap shared by Fractal.shader and NewtonFractal01.shader.
    static double OuterTrap(Cplx z, double offset)
    {
        var leaf = LeafOrbit(new Cplx(5.0 * z.Re, 5.0 * z.Im), offset);
        var a = Math.Log(Math.Max(1e-10, leaf));
        var b = Math.Log(Math.Max(1e-10, Math.Abs(a)));
        return Math.Log(0.4 * Math.Abs(b) + 0.04);
    }

    static double LeafOrbit(Cplx p, double offset)
    {
        var y = Math.Abs(p.Im) + offset;
        return Math.Sqrt(p.Re * p.Re + y * y) - offset;
    }

    static (double, double, double) ColorBand(double t, double expR, double expG, double expB)
    {
        var phase = t * TwoPi;
        var baseValue = 0.5 - 0.5 * Math.Cos(phase);
        return (Math.Pow(baseValue, expR), Math.Pow(baseValue, expG), Math.Pow(baseValue, expB));
    }

    static double Frac(double v) => v - Math.Floor(v);
}
