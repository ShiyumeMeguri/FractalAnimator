using FractalAnimator.Core.Kernel;

namespace FractalAnimator.Core.Atoms;

public static class ArbitraryPrecisionFallback
{
    public const int StartPrecision = 80;
    public const int PrecisionStep = 60;
    public const int MaxPrecision = 600;

    public static bool TryResolve(
        BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        BigDecimalCompat juliaReal, BigDecimalCompat juliaImag, double powerReal, double powerImag,
        double pixelOffsetReal, double pixelOffsetImag, int scaleExp,
        int maxIterations, double leafOffset,
        out double r, out double g, out double b, out int precisionUsed)
    {
        var scale = BigDecimalCompat.Pow2(scaleExp);
        var seedCtx = new MathContextCompat(MaxPrecision);
        var cRe = centerReal.Add(BigDecimalCompat.FromDoubleExact(pixelOffsetReal).Multiply(scale, seedCtx), seedCtx);
        var cIm = centerImag.Add(BigDecimalCompat.FromDoubleExact(pixelOffsetImag).Multiply(scale, seedCtx), seedCtx);
        var kRe = juliaReal;
        var kIm = juliaImag;

        for (var precision = StartPrecision; precision <= MaxPrecision; precision += PrecisionStep)
        {
            if (!TryIterate(cRe, cIm, kRe, kIm, powerReal, powerImag, maxIterations, leafOffset, precision,
                            out var state, out var iterations))
                continue;

            if (!BStyleTrapAtom.IsCertified(state, iterations)) continue;

            (r, g, b) = BStyleTrapAtom.Resolve(state, iterations);
            precisionUsed = precision;
            return true;
        }

        r = 0.0; g = 0.0; b = 0.0; precisionUsed = MaxPrecision;
        return false;
    }

    public static bool TryResolveAt(
        BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        BigDecimalCompat juliaReal, BigDecimalCompat juliaImag, double powerReal, double powerImag,
        double pixelOffsetReal, double pixelOffsetImag, int scaleExp,
        int maxIterations, double leafOffset, int precision,
        out double r, out double g, out double b)
    {
        var scale = BigDecimalCompat.Pow2(scaleExp);
        var seedCtx = new MathContextCompat(precision + 60);
        var cRe = centerReal.Add(BigDecimalCompat.FromDoubleExact(pixelOffsetReal).Multiply(scale, seedCtx), seedCtx);
        var cIm = centerImag.Add(BigDecimalCompat.FromDoubleExact(pixelOffsetImag).Multiply(scale, seedCtx), seedCtx);
        var kRe = juliaReal;
        var kIm = juliaImag;

        if (TryIterate(cRe, cIm, kRe, kIm, powerReal, powerImag, maxIterations, leafOffset, precision,
                       out var state, out var iterations))
        {
            (r, g, b) = BStyleTrapAtom.Resolve(state, iterations);
            return true;
        }
        r = 0.0; g = 0.0; b = 0.0;
        return false;
    }

    static bool TryIterate(
        BigDecimalCompat cRe, BigDecimalCompat cIm, BigDecimalCompat kRe, BigDecimalCompat kIm,
        double powerReal, double powerImag, int maxIterations, double leafOffset, int precision,
        out ColorState state, out int iterations)
    {
        var ctx = new MathContextCompat(precision);
        var zr = cRe;
        var zi = cIm;
        state = new ColorState { TrapMin = 1e5, TrapMinError = 0.0, FinalMagnitudeSquared = 1.0 };

        var relativeError = Math.Pow(10.0, 1 - precision);
        var accumulated = 0.0;
        var powerModulus = Math.Sqrt(powerReal * powerReal + powerImag * powerImag);

        iterations = 1;
        for (; iterations < maxIterations; iterations++)
        {
            var magnitude = Math.Sqrt(MagnitudeSquared(zr, zi));
            if (!(magnitude > 0.0)) { state = default; return false; }

            var localDerivative = powerModulus * Math.Pow(magnitude, powerReal - 1.0);
            if (!double.IsFinite(localDerivative)) { state = default; return false; }
            accumulated = localDerivative * accumulated + relativeError * magnitude;

            var (pr, pi) = DeepOrbitTrapEngine.ComplexPow(zr, zi, powerReal, powerImag, ctx);
            zr = pr.Add(kRe, ctx);
            zi = pi.Add(kIm, ctx);

            var re = zr.ToDouble();
            var im = zi.ToDouble();
            var magSq = re * re + im * im;
            state.FinalMagnitudeSquared = magSq;
            state.FinalPointError = accumulated;

            var pointMagnitude = Math.Sqrt(magSq);
            var uncertainty = 2.0 * pointMagnitude * accumulated + accumulated * accumulated;
            if (!(Math.Abs(magSq - 1024.0) > uncertainty)) { state = default; return false; }
            if (magSq > 1024.0) return true;

            BStyleTrapAtom.Accumulate(ref state, re, im, accumulated);
        }
        iterations = maxIterations;
        return true;
    }

    static double MagnitudeSquared(BigDecimalCompat re, BigDecimalCompat im)
    {
        var r = re.ToDouble();
        var i = im.ToDouble();
        return r * r + i * i;
    }
}
