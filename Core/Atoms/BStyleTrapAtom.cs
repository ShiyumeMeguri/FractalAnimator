using FractalAnimator.Core.Kernel;

namespace FractalAnimator.Core.Atoms;

public readonly struct BStyleTrapAtom : IColoringAtom
{
    const double ExponentR = 10.0, ExponentG = 2.5, ExponentB = 0.75;
    const double LeafOffset = 0.59;
    const double TwoPi = 6.283185307179586;
    const double QuantisationHalfStep = 0.5 / 255.0;
    const double SmoothOffset = -1.7369655941662063;

    const double LeafFloor = 1e-10;
    const double TrapScale = 0.4;
    const double TrapConstant = 0.04;
    const double ClampPeak = 2.2245;
    const double ClampLeafLo = 1.0 - 2e-10;
    const double ClampLeafHi = 1.0 + 2e-10;

    public static void Accumulate(ref ColorState state, double re, double im, double pointError)
    {
        var radiusSquared = RadiusSquaredInterval(re, im, pointError);
        if (radiusSquared.IsEmpty) { Invalidate(ref state); return; }

        if (!double.IsNaN(state.TrapHi))
        {
            if (!(state.WindowKey == state.TrapHi)) RebuildWindow(ref state);
            if (Excluded(in state, radiusSquared)) return;
        }

        var domain = Interval.Max(LeafFromRadiusSquared(radiusSquared), LeafFloor);
        var trapBall = TrapFromLeaf(domain);
        if (trapBall.IsEmpty) { Invalidate(ref state); return; }

        var lo = double.IsNaN(state.TrapLo) ? double.PositiveInfinity : state.TrapLo;
        var hi = double.IsNaN(state.TrapHi) ? double.PositiveInfinity : state.TrapHi;

        state.TrapLo = Math.Min(lo, trapBall.Lo);
        state.TrapHi = Math.Min(hi, trapBall.Hi);
    }

    static void Invalidate(ref ColorState state)
    {
        state.TrapLo = double.NaN;
        state.TrapHi = double.NaN;
        state.WindowKey = double.NaN;
    }

    static bool Excluded(in ColorState state, Interval radiusSquared) =>
        (radiusSquared.Hi < state.WindowLoA || radiusSquared.Lo > state.WindowHiA)
        && (radiusSquared.Hi < state.WindowLoB || radiusSquared.Lo > state.WindowHiB);

    static void RebuildWindow(ref ColorState state)
    {
        var hi = state.TrapHi;
        state.WindowKey = hi;

        if (double.IsNaN(hi)) { OpenWindow(ref state); return; }

        var reach = ((Interval.Exp(Interval.Exact(hi)) - Interval.Exact(TrapConstant))
                     / Interval.Exact(TrapScale)).Hi;

        if (double.IsNaN(reach)) { OpenWindow(ref state); return; }
        if (!(reach > 0.0)) { ShutWindow(ref state); return; }

        var outward = Interval.Exp(Interval.Exact(reach));
        var inward = Interval.Exp(Interval.Exact(-reach));

        var loA = Interval.Exp(-outward).Lo;
        var hiA = Interval.Exp(-inward).Hi;
        var loB = Interval.Exp(inward).Lo;
        var hiB = Interval.Exp(outward).Hi;

        if (hi > ClampPeak)
        {
            hiA = Math.Max(hiA, ClampLeafHi);
            loB = Math.Min(loB, ClampLeafLo);
        }

        state.WindowLoA = RadiusSquaredFloor(loA);
        state.WindowHiA = RadiusSquaredCeiling(hiA);
        state.WindowLoB = RadiusSquaredFloor(loB);
        state.WindowHiB = RadiusSquaredCeiling(hiB);
    }

    static double RadiusSquaredFloor(double leaf) =>
        !(leaf > LeafFloor) ? 0.0 : Interval.SqrNonNegative(Interval.Exact(leaf) + LeafOffset).Lo;

    static double RadiusSquaredCeiling(double leaf) =>
        leaf < LeafFloor ? double.NegativeInfinity : Interval.SqrNonNegative(Interval.Exact(leaf) + LeafOffset).Hi;

    static void OpenWindow(ref ColorState state)
    {
        state.WindowLoA = 0.0;
        state.WindowHiA = double.PositiveInfinity;
        state.WindowLoB = 0.0;
        state.WindowHiB = double.PositiveInfinity;
    }

    static void ShutWindow(ref ColorState state)
    {
        state.WindowLoA = double.PositiveInfinity;
        state.WindowHiA = double.NegativeInfinity;
        state.WindowLoB = double.PositiveInfinity;
        state.WindowHiB = double.NegativeInfinity;
    }

    static Interval RadiusSquaredInterval(double re, double im, double pointError)
    {
        var x = Interval.Abs(Interval.ScaledBall(re, pointError, 5.0));
        var y = Interval.Abs(Interval.ScaledBall(im, pointError, 5.0)) + LeafOffset;
        return Interval.SqrNonNegative(x) + Interval.SqrNonNegative(y);
    }

    static Interval LeafFromRadiusSquared(Interval radiusSquared) =>
        Interval.Sqrt(radiusSquared) - Interval.Exact(LeafOffset);

    static Interval TrapFromLeaf(Interval domain)
    {
        var a = Interval.Log(domain);
        if (a.IsEmpty) return a;
        var b = Interval.Log(Interval.Max(Interval.Abs(a), LeafFloor));
        if (b.IsEmpty) return b;
        return Interval.Log(Interval.Abs(b) * TrapScale + TrapConstant);
    }

    public static Interval TrapIntervalForTest(double re, double im, double pointError)
    {
        var radiusSquared = RadiusSquaredInterval(re, im, pointError);
        if (radiusSquared.IsEmpty) return radiusSquared;
        return TrapFromLeaf(Interval.Max(LeafFromRadiusSquared(radiusSquared), LeafFloor));
    }

    public static Interval RadiusSquaredForTest(double re, double im, double pointError) =>
        RadiusSquaredInterval(re, im, pointError);

    public static bool ExcludedForTest(ref ColorState state, Interval radiusSquared)
    {
        if (double.IsNaN(state.TrapHi)) return false;
        if (!(state.WindowKey == state.TrapHi)) RebuildWindow(ref state);
        return Excluded(in state, radiusSquared);
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
        if (!double.IsFinite(state.TrapLo) || !double.IsFinite(state.TrapHi))
            return new Interval(double.NaN, double.NaN);

        var smooth = SmoothIterations(state, iterations);
        if (smooth.IsEmpty || smooth.Lo <= 0.0) return new Interval(double.NaN, double.NaN);

        var trap = new Interval(state.TrapLo, state.TrapHi);
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
