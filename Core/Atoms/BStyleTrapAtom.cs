using FractalAnimator.Core.Kernel;

namespace FractalAnimator.Core.Atoms;

public readonly struct BStyleTrapAtom : IColoringAtom
{
    const double ExponentR = 10.0, ExponentG = 2.5, ExponentB = 0.75;
    const double LeafOffset = 0.59;
    const double TwoPi = 6.283185307179586;
    const double QuantisationHalfStep = 0.5 / 255.0;
    const double SmoothOffset = -1.7369655941662063;

    public static void Accumulate(ref ColorState state, double re, double im, double pointError)
    {
        var trapBall = TrapInterval(re, im, pointError);
        if (trapBall.IsEmpty)
        {
            state.TrapMin = double.NaN;
            state.TrapMinError = double.PositiveInfinity;
            return;
        }

        var lo = double.IsNaN(state.TrapMin) ? double.PositiveInfinity : state.TrapMin - state.TrapMinError;
        var hi = double.IsNaN(state.TrapMin) ? double.PositiveInfinity : state.TrapMin + state.TrapMinError;

        var newLo = Math.Min(lo, trapBall.Lo);
        var newHi = Math.Min(hi, trapBall.Hi);

        state.TrapMin = 0.5 * (newLo + newHi);
        state.TrapMinError = 0.5 * (newHi - newLo);
    }

    public static Interval TrapIntervalForTest(double re, double im, double pointError) => TrapInterval(re, im, pointError);

    public static double TrapLowerBoundForTest(double re, double im, double pointError) => TrapLowerBound(re, im, pointError);

    static double TrapLowerBound(double re, double im, double pointError)
    {
        var ex = 5.0 * pointError;
        var x = Math.Abs(5.0 * re);
        var y = Math.Abs(5.0 * im);

        var xLo = Math.Max(x - ex, 0.0);
        var yLo = Math.Max(y - ex, 0.0) + LeafOffset;
        var radiusLo = Math.Sqrt(xLo * xLo + yLo * yLo);
        var leafLo = Math.Max(radiusLo - LeafOffset, 1e-10);

        var xHi = x + ex;
        var yHi = y + ex + LeafOffset;
        var radiusHi = Math.Sqrt(xHi * xHi + yHi * yHi);
        var leafHi = Math.Max(radiusHi - LeafOffset, 1e-10);

        var aLo = Math.Log(leafLo);
        var aHi = Math.Log(leafHi);

        var absLo = aLo <= 0.0 && aHi >= 0.0 ? 0.0 : Math.Min(Math.Abs(aLo), Math.Abs(aHi));
        var bLo = Math.Log(Math.Max(absLo, 1e-10));

        var innerLo = 0.4 * Math.Abs(bLo) + 0.04;
        var candidate = Math.Log(innerLo);

        var bHi = Math.Log(Math.Max(Math.Max(Math.Abs(aLo), Math.Abs(aHi)), 1e-10));
        var innerAlt = 0.4 * Math.Abs(bHi) + 0.04;
        var alt = Math.Log(innerAlt);

        var floorValue = Math.Min(candidate, alt);
        return floorValue - 1e-6 * Math.Abs(floorValue) - 1e-6;
    }

    static Interval TrapInterval(double re, double im, double pointError)
    {
        var x = Interval.Ball(5.0 * re, 5.0 * pointError);
        var y = Interval.Abs(Interval.Ball(5.0 * im, 5.0 * pointError)) + LeafOffset;
        var radius = Interval.Sqrt(x * x + y * y);
        var leaf = radius - Interval.Exact(LeafOffset);
        var a = Interval.Log(Interval.Max(leaf, 1e-10));
        if (a.IsEmpty) return a;
        var b = Interval.Log(Interval.Max(Interval.Abs(a), 1e-10));
        if (b.IsEmpty) return b;
        var inner = Interval.Abs(b) * 0.4 + 0.04;
        return Interval.Log(inner);
    }

    public static (double R, double G, double B) Resolve(in ColorState state, int iterations)
    {
        var band = BandInterval(state, iterations);
        if (band.IsEmpty) return (0.0, 0.0, 0.0);
        var t = band.Centre;
        var baseValue = 0.5 - 0.5 * Math.Cos(t * TwoPi);
        return (Math.Pow(baseValue, ExponentR), Math.Pow(baseValue, ExponentG), Math.Pow(baseValue, ExponentB));
    }

    static Interval SmoothIterations(in ColorState state, int iterations)
    {
        var magnitude = Math.Sqrt(state.FinalMagnitudeSquared);
        var zz = Interval.Max(Interval.Ball(state.FinalMagnitudeSquared, 2.0 * magnitude * state.FinalPointError
                                                                        + state.FinalPointError * state.FinalPointError),
                              1.0000001);
        var inner = Interval.Log2(zz);
        if (inner.IsEmpty || inner.Lo <= 0.0) return new Interval(double.NaN, double.NaN);
        var outer = Interval.Log2(inner);
        if (outer.IsEmpty) return outer;
        return Interval.Exact(iterations) - (outer + SmoothOffset);
    }

    static Interval BandInterval(in ColorState state, int iterations)
    {
        if (double.IsNaN(state.TrapMin) || !double.IsFinite(state.TrapMinError))
            return new Interval(double.NaN, double.NaN);

        var smooth = SmoothIterations(state, iterations);
        if (smooth.IsEmpty || smooth.Lo <= 0.0) return new Interval(double.NaN, double.NaN);

        var trap = Interval.Ball(state.TrapMin, state.TrapMinError);
        return trap * -0.25 + Interval.Log2(smooth) * 0.9 + Interval.Exact(0.5);
    }

    public static bool IsBlackCertified(in ColorState state, int iterations)
    {
        var smooth = SmoothIterations(state, iterations);
        return !smooth.IsEmpty && smooth.StrictlyBelow(0.0);
    }

    public static bool IsCertified(in ColorState state, int iterations)
    {
        if (IsBlackCertified(state, iterations)) return true;

        var band = BandInterval(state, iterations);
        if (band.IsEmpty) return false;

        var cosBall = Interval.Cos(band * TwoPi);
        if (cosBall.IsEmpty) return false;
        var baseBall = Interval.Exact(0.5) - cosBall * 0.5;
        if (baseBall.IsEmpty) return false;

        return Quantises(baseBall, ExponentR) && Quantises(baseBall, ExponentG) && Quantises(baseBall, ExponentB);
    }

    static bool Quantises(Interval baseBall, double exponent)
    {
        var channel = Interval.Pow(baseBall, exponent);
        if (channel.IsEmpty) return false;
        var lo = (int)Math.Round(Math.Clamp(channel.Lo, 0.0, 1.0) * 255.0);
        var hi = (int)Math.Round(Math.Clamp(channel.Hi, 0.0, 1.0) * 255.0);
        if (lo != hi) return false;
        return channel.Width < QuantisationHalfStep;
    }
}
