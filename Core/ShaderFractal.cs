namespace FractalAnimator.Core;

/// <summary>
/// A 2-component complex number, mirroring the HLSL <c>float2</c> the FractalParadise shaders use.
/// </summary>
public readonly struct Cplx
{
    public readonly double Re;
    public readonly double Im;

    public Cplx(double re, double im)
    {
        Re = re;
        Im = im;
    }

    public double Dot => Re * Re + Im * Im;

    public static Cplx operator +(Cplx a, Cplx b) => new(a.Re + b.Re, a.Im + b.Im);
    public static Cplx operator -(Cplx a, Cplx b) => new(a.Re - b.Re, a.Im - b.Im);
    public static Cplx operator *(Cplx a, double s) => new(a.Re * s, a.Im * s);
}

/// <summary>
/// The fractal type selector, matching FractalFunction.hlsl's <c>applySelectedFractal</c> switch
/// (cases 0..7), with two Newton variants appended (8, 9) from the Newton shaders.
/// </summary>
public enum ShaderFractalType
{
    Mandelbrot = 0,
    BurningShip = 1,
    Feather = 2,
    Sfx = 3,
    Henon = 4,
    Duffing = 5,
    Ikeda = 6,
    Chirikov = 7,
    Newton = 8,
    NewtonFlower = 9,
}

/// <summary>
/// Faithful 1:1 port of FractalParadise's complex iteration kernels.
/// Source: Assets/FractalFunction.hlsl (cx_* helpers + the eight fractal functions) and
/// Assets/NewtonFractal01.shader (func/deriv/newtonIteration) and Assets/FractalArt/NewtonFractalFlower.shader.
/// The HLSL globals <c>_PowerReal</c>/<c>_PowerImag</c> are passed in as <c>power</c>.
/// </summary>
public static class ShaderFractal
{
    // FractalFunction.hlsl:5 cx_pow — z^p = exp(p * log(z)).
    public static Cplx Pow(Cplx z, Cplx p)
    {
        if (z.Re == 0.0 && z.Im == 0.0) return new Cplx(0, 0);
        var logR = 0.5 * Math.Log(z.Dot);
        var theta = Math.Atan2(z.Im, z.Re);
        var realPart = p.Re * logR - p.Im * theta;
        var imagPart = p.Re * theta + p.Im * logR;
        var r = Math.Exp(realPart);
        return new Cplx(r * Math.Cos(imagPart), r * Math.Sin(imagPart));
    }

    // FractalFunction.hlsl:29 cx_mul.
    public static Cplx Mul(Cplx a, Cplx b) =>
        new(a.Re * b.Re - a.Im * b.Im, a.Re * b.Im + a.Im * b.Re);

    // FractalFunction.hlsl:37 cx_sqr.
    public static Cplx Sqr(Cplx a)
    {
        var x2 = a.Re * a.Re;
        var y2 = a.Im * a.Im;
        var xy = a.Re * a.Im;
        return new Cplx(x2 - y2, xy + xy);
    }

    // FractalFunction.hlsl:45 cx_cube.
    public static Cplx Cube(Cplx a)
    {
        var x2 = a.Re * a.Re;
        var y2 = a.Im * a.Im;
        var d = x2 - y2;
        return new Cplx(a.Re * (d - y2 - y2), a.Im * (x2 + x2 + d));
    }

    // FractalFunction.hlsl:56 cx_div.
    public static Cplx Div(Cplx a, Cplx b)
    {
        var denom = 1.0 / (b.Re * b.Re + b.Im * b.Im);
        return new Cplx((a.Re * b.Re + a.Im * b.Im) * denom, (a.Im * b.Re - a.Re * b.Im) * denom);
    }

    // FractalFunction.hlsl:67 mandelbrot.
    public static Cplx Mandelbrot(Cplx z, Cplx c, double powRe, double powIm)
    {
        if (powRe == 2.0 && powIm == 0.0) return Sqr(z) + c;
        return Pow(z, new Cplx(powRe, powIm)) + c;
    }

    // FractalFunction.hlsl:82 burning_ship — abs the components, then z^p + c.
    public static Cplx BurningShip(Cplx z, Cplx c, double powRe, double powIm)
    {
        z = new Cplx(Math.Abs(z.Re), Math.Abs(z.Im));
        if (powRe == 2.0 && powIm == 0.0) return Sqr(z) + c;
        return Pow(z, new Cplx(powRe, powIm)) + c;
    }

    // FractalFunction.hlsl:100 feather — z^p / (1 + z*z) + c.
    public static Cplx Feather(Cplx z, Cplx c, double powRe, double powIm)
    {
        var one = new Cplx(1.0, 0.0);
        var zSquared = new Cplx(z.Re * z.Re, z.Im * z.Im); // matches HLSL `z * z` (component-wise)
        if (powRe == 3.0 && powIm == 0.0) return Div(Cube(z), one + zSquared) + c;
        return Div(Pow(z, new Cplx(powRe, powIm)), one + zSquared) + c;
    }

    // FractalFunction.hlsl:121 sfx.
    public static Cplx Sfx(Cplx z, Cplx c)
    {
        var d = z.Dot;
        var cc2 = new Cplx(c.Re * c.Re - c.Im * c.Im, 2 * c.Re * c.Im);
        return z * d - Mul(z, cc2);
    }

    // FractalFunction.hlsl:128 henon.
    public static Cplx Henon(Cplx z, Cplx c) =>
        new(1.0 - c.Re * z.Re * z.Re + z.Im, c.Im * z.Re);

    // FractalFunction.hlsl:136 duffing.
    public static Cplx Duffing(Cplx z, Cplx c) =>
        new(z.Im, -c.Im * z.Re + c.Re * z.Im - z.Im * z.Im * z.Im);

    // FractalFunction.hlsl:144 ikeda.
    public static Cplx Ikeda(Cplx z, Cplx c)
    {
        var t = 0.4 - 6.0 / (1.0 + z.Dot);
        var st = Math.Sin(t);
        var ct = Math.Cos(t);
        return new Cplx(1.0 + c.Re * (z.Re * ct - z.Im * st), c.Im * (z.Re * st + z.Im * ct));
    }

    // FractalFunction.hlsl:155 chirikov.
    public static Cplx Chirikov(Cplx z, Cplx c)
    {
        var y = z.Im + c.Im * Math.Sin(z.Re);
        var x = z.Re + c.Re * y;
        return new Cplx(x, y);
    }

    // FractalFunction.hlsl:163 applySelectedFractal.
    public static Cplx Apply(ShaderFractalType type, Cplx z, Cplx c, double powRe, double powIm) => type switch
    {
        ShaderFractalType.Mandelbrot => Mandelbrot(z, c, powRe, powIm),
        ShaderFractalType.BurningShip => BurningShip(z, c, powRe, powIm),
        ShaderFractalType.Feather => Feather(z, c, powRe, powIm),
        ShaderFractalType.Sfx => Sfx(z, c),
        ShaderFractalType.Henon => Henon(z, c),
        ShaderFractalType.Duffing => Duffing(z, c),
        ShaderFractalType.Ikeda => Ikeda(z, c),
        ShaderFractalType.Chirikov => Chirikov(z, c),
        _ => Mandelbrot(z, c, powRe, powIm),
    };

    // NewtonFractal01.shader:96 func — a transcendental f(z) whose roots form the Newton fractal.
    public static Cplx NewtonFunc(Cplx z)
    {
        var realPart = Math.Cosh(z.Re) * Math.Cos(z.Im) + Math.Cos(z.Re) * Math.Cosh(z.Im) - 1.0;
        var imagPart = Math.Sinh(z.Re) * Math.Sin(z.Im) - Math.Sin(z.Re) * Math.Sinh(z.Im);
        return new Cplx(realPart, imagPart);
    }

    // NewtonFractal01.shader:117 deriv.
    public static Cplx NewtonDeriv(Cplx z)
    {
        var realPart = Math.Sinh(z.Re) * Math.Cos(z.Im) - Math.Sin(z.Re) * Math.Cosh(z.Im);
        var imagPart = Math.Cosh(z.Re) * Math.Sin(z.Im) - Math.Cos(z.Re) * Math.Sinh(z.Im);
        return new Cplx(realPart, imagPart);
    }

    // NewtonFractal01.shader:128 newtonIteration — z -= f(z)/f'(z).
    public static Cplx NewtonStep(Cplx z)
    {
        var fz = NewtonFunc(z);
        var dfz = NewtonDeriv(z);
        var denom = dfz.Dot;
        var realPart = fz.Re * dfz.Re + fz.Im * dfz.Im;
        var imagPart = -fz.Re * dfz.Im + fz.Im * dfz.Re;
        return new Cplx(z.Re - realPart / denom, z.Im - imagPart / denom);
    }

    // NewtonFractalFlower.shader:42 complex hyperbolic/trig helpers + complexDivide.
    static Cplx CoshComplex(Cplx a) => new(Math.Cosh(a.Re) * Math.Cos(a.Im), Math.Sinh(a.Re) * Math.Sin(a.Im));
    static Cplx CosComplex(Cplx a) => new(Math.Cos(a.Re) * Math.Cosh(a.Im), -Math.Sin(a.Re) * Math.Sinh(a.Im));
    static Cplx SinhComplex(Cplx a) => new(Math.Sinh(a.Re) * Math.Cos(a.Im), Math.Cosh(a.Re) * Math.Sin(a.Im));
    static Cplx SinComplex(Cplx a) => new(Math.Sin(a.Re) * Math.Cosh(a.Im), Math.Cos(a.Re) * Math.Sinh(a.Im));
    static Cplx DivideFlower(Cplx a, Cplx b)
    {
        var d = b.Dot;
        return new Cplx((a.Re * b.Re + a.Im * b.Im) / d, (a.Im * b.Re - a.Re * b.Im) / d);
    }

    // NewtonFractalFlower.shader:68 foldApproach — returns the convergence step count (-1 if it never converged).
    public static int NewtonFlowerSteps(Cplx v)
    {
        for (var i = 0; i < 100; i++)
        {
            var pNext = CoshComplex(v) + CosComplex(v) - new Cplx(1.0, 0.0);
            var pDiff = SinhComplex(v) - SinComplex(v);
            var vNext = v - DivideFlower(pNext, pDiff);
            var dx = vNext.Re - v.Re;
            var dy = vNext.Im - v.Im;
            if (Math.Sqrt(dx * dx + dy * dy) < 0.001) return i;
            v = vNext;
        }
        return -1;
    }
}
