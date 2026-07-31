using System.Runtime.CompilerServices;

namespace FractalAnimator.Core.Kernel;

public readonly struct Interval
{
    public readonly double Lo;
    public readonly double Hi;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Interval(double lo, double hi) { Lo = lo; Hi = hi; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Interval Exact(double value) => new(value, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Interval Ball(double centre, double radius)
    {
        if (double.IsNaN(centre) || double.IsNaN(radius)) return new Interval(double.NaN, double.NaN);
        var r = Math.Abs(radius);
        return new Interval(Down(centre - r), Up(centre + r));
    }

    public bool IsEmpty => double.IsNaN(Lo) || double.IsNaN(Hi);
    public double Width => Hi - Lo;
    public double Centre => 0.5 * (Lo + Hi);
    public bool Contains(double v) => v >= Lo && v <= Hi;
    public bool StrictlyBelow(double v) => Hi < v;
    public bool StrictlyAbove(double v) => Lo > v;

    public const int TranscendentalUlps = 4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double Down(double v) => double.IsFinite(v) ? Math.BitDecrement(v) : v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double Up(double v) => double.IsFinite(v) ? Math.BitIncrement(v) : v;

    static double DownN(double v, int n) { for (var i = 0; i < n && double.IsFinite(v); i++) v = Math.BitDecrement(v); return v; }
    static double UpN(double v, int n) { for (var i = 0; i < n && double.IsFinite(v); i++) v = Math.BitIncrement(v); return v; }

    static Interval Loose(double lo, double hi) =>
        new(DownN(lo, TranscendentalUlps), UpN(hi, TranscendentalUlps));

    public static Interval operator +(Interval a, Interval b) => new(Down(a.Lo + b.Lo), Up(a.Hi + b.Hi));
    public static Interval operator -(Interval a, Interval b) => new(Down(a.Lo - b.Hi), Up(a.Hi - b.Lo));
    public static Interval operator -(Interval a) => new(-a.Hi, -a.Lo);
    public static Interval operator +(Interval a, double b) => new(Down(a.Lo + b), Up(a.Hi + b));

    public static Interval operator *(Interval a, Interval b)
    {
        var p1 = a.Lo * b.Lo; var p2 = a.Lo * b.Hi; var p3 = a.Hi * b.Lo; var p4 = a.Hi * b.Hi;
        return new Interval(Down(Math.Min(Math.Min(p1, p2), Math.Min(p3, p4))),
                            Up(Math.Max(Math.Max(p1, p2), Math.Max(p3, p4))));
    }

    public static Interval operator *(Interval a, double b) =>
        b >= 0 ? new Interval(Down(a.Lo * b), Up(a.Hi * b)) : new Interval(Down(a.Hi * b), Up(a.Lo * b));

    public static Interval operator /(Interval a, Interval b)
    {
        if (b.Lo <= 0.0 && b.Hi >= 0.0) return new Interval(double.NaN, double.NaN);
        var q1 = a.Lo / b.Lo; var q2 = a.Lo / b.Hi; var q3 = a.Hi / b.Lo; var q4 = a.Hi / b.Hi;
        return new Interval(Down(Math.Min(Math.Min(q1, q2), Math.Min(q3, q4))),
                            Up(Math.Max(Math.Max(q1, q2), Math.Max(q3, q4))));
    }

    public static Interval Abs(Interval a)
    {
        if (a.Lo >= 0) return a;
        if (a.Hi <= 0) return new Interval(-a.Hi, -a.Lo);
        return new Interval(0.0, Math.Max(-a.Lo, a.Hi));
    }

    public static Interval Max(Interval a, double c) => new(Math.Max(a.Lo, c), Math.Max(a.Hi, c));

    public static Interval Sqrt(Interval a)
    {
        if (a.Hi < 0) return new Interval(double.NaN, double.NaN);
        var lo = a.Lo <= 0 ? 0.0 : Down(Math.Sqrt(a.Lo));
        return new Interval(lo, Up(Math.Sqrt(a.Hi)));
    }

    public static Interval Log(Interval a)
    {
        if (a.Lo <= 0) return new Interval(double.NaN, double.NaN);
        return Loose(Math.Log(a.Lo), Math.Log(a.Hi));
    }

    public static Interval Log2(Interval a)
    {
        if (a.Lo <= 0) return new Interval(double.NaN, double.NaN);
        return Loose(Math.Log2(a.Lo), Math.Log2(a.Hi));
    }

    public static Interval Cos(Interval a)
    {
        if (a.IsEmpty) return a;
        if (a.Width >= 2.0 * Math.PI) return new Interval(-1.0, 1.0);

        var lo = Math.Cos(a.Lo);
        var hi = Math.Cos(a.Hi);
        var min = Math.Min(lo, hi);
        var max = Math.Max(lo, hi);

        var kLo = Math.Ceiling(a.Lo / Math.PI);
        for (var i = 0; i < 2; i++)
        {
            var k = kLo + i;
            var point = k * Math.PI;
            if (point > a.Hi) break;
            if (Math.Abs(k % 2.0) < 0.5) max = 1.0; else min = -1.0;
        }
        return Loose(min, max);
    }

    public static Interval Pow(Interval baseValue, double exponent)
    {
        if (baseValue.Hi < 0) return new Interval(double.NaN, double.NaN);
        var lo = Math.Max(baseValue.Lo, 0.0);
        return Loose(Math.Pow(lo, exponent), Math.Pow(Math.Max(baseValue.Hi, 0.0), exponent));
    }

    public override string ToString() => $"[{Lo:G17}, {Hi:G17}]";
}
