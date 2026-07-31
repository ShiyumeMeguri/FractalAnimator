using System.Runtime.CompilerServices;
using FractalAnimator.Core.Kernel;

namespace FractalAnimator.Core;

public readonly struct DoubleDouble : IEquatable<DoubleDouble>, IPrecision<DoubleDouble>
{
    public readonly double Hi;
    public readonly double Lo;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DoubleDouble(double hi, double lo) { Hi = hi; Lo = lo; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DoubleDouble(double value) { Hi = value; Lo = 0.0; }

    public static DoubleDouble Zero { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new(0.0, 0.0); }
    public static DoubleDouble One { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new(1.0, 0.0); }

    public static DoubleDouble Pi
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(3.141592653589793e+00, 1.2246467991473532e-16);
    }

    public static readonly DoubleDouble Ln2 = new(6.931471805599453e-01, 2.3190468138462996e-17);
    public static readonly DoubleDouble HalfPi = new(1.5707963267948966e+00, 6.123233995736766e-17);
    public static readonly DoubleDouble TwoPi = new(6.283185307179586e+00, 2.4492935982947064e-16);

    public double ToDouble() => Hi + Lo;
    public bool IsZero => Hi == 0.0;
    public int Sign => Hi > 0 ? 1 : Hi < 0 ? -1 : 0;
    public bool Equals(DoubleDouble other) => Hi == other.Hi && Lo == other.Lo;
    public override bool Equals(object? obj) => obj is DoubleDouble d && Equals(d);
    public override int GetHashCode() => HashCode.Combine(Hi, Lo);
    public override string ToString() => (Hi + Lo).ToString("R");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static (double s, double e) TwoSum(double a, double b)
    {
        var s = a + b;
        var bb = s - a;
        return (s, (a - (s - bb)) + (b - bb));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static (double s, double e) QuickTwoSum(double a, double b)
    {
        var s = a + b;
        return (s, b - (s - a));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static (double p, double e) TwoProd(double a, double b)
    {
        var p = a * b;
        return (p, Math.FusedMultiplyAdd(a, b, -p));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDouble operator +(DoubleDouble a, DoubleDouble b)
    {
        var (s1, e1) = TwoSum(a.Hi, b.Hi);
        var (s2, e2) = TwoSum(a.Lo, b.Lo);
        e1 += s2;
        (s1, e1) = QuickTwoSum(s1, e1);
        e1 += e2;
        (s1, e1) = QuickTwoSum(s1, e1);
        return new DoubleDouble(s1, e1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDouble operator -(DoubleDouble a, DoubleDouble b) => a + (-b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDouble operator -(DoubleDouble a) => new(-a.Hi, -a.Lo);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDouble operator *(DoubleDouble a, DoubleDouble b)
    {
        var (p, e) = TwoProd(a.Hi, b.Hi);
        e += a.Hi * b.Lo + a.Lo * b.Hi;
        (p, e) = QuickTwoSum(p, e);
        return new DoubleDouble(p, e);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDouble operator *(DoubleDouble a, double b)
    {
        var (p, e) = TwoProd(a.Hi, b);
        e += a.Lo * b;
        (p, e) = QuickTwoSum(p, e);
        return new DoubleDouble(p, e);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDouble operator /(DoubleDouble a, double b)
    {
        var q1 = a.Hi / b;
        var (p, e) = TwoProd(q1, b);
        var r = a - new DoubleDouble(p, e);
        var q2 = (r.Hi + r.Lo) / b;
        var (s, err) = QuickTwoSum(q1, q2);
        return new DoubleDouble(s, err);
    }

    public static DoubleDouble operator /(DoubleDouble a, DoubleDouble b)
    {
        var q1 = a.Hi / b.Hi;
        var r = a - b * q1;
        var q2 = r.Hi / b.Hi;
        r -= b * q2;
        var q3 = r.Hi / b.Hi;
        var (s, e) = QuickTwoSum(q1, q2);
        return new DoubleDouble(s, e) + new DoubleDouble(q3);
    }

    public static bool operator <(DoubleDouble a, DoubleDouble b) => a.Hi < b.Hi || (a.Hi == b.Hi && a.Lo < b.Lo);
    public static bool operator >(DoubleDouble a, DoubleDouble b) => a.Hi > b.Hi || (a.Hi == b.Hi && a.Lo > b.Lo);
    public static bool operator ==(DoubleDouble a, DoubleDouble b) => a.Equals(b);
    public static bool operator !=(DoubleDouble a, DoubleDouble b) => !a.Equals(b);

    public static DoubleDouble Abs(DoubleDouble a) => a.Hi < 0 ? -a : a;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDouble ScaleB(DoubleDouble a, int n) =>
        new(Math.ScaleB(a.Hi, n), Math.ScaleB(a.Lo, n));

    public static DoubleDouble Sqrt(DoubleDouble a)
    {
        if (a.Hi == 0.0) return Zero;
        if (a.Hi < 0.0) throw new ArgumentException("Sqrt of a negative DoubleDouble");
        var x = 1.0 / Math.Sqrt(a.Hi);
        var ax = a.Hi * x;
        var diff = a - new DoubleDouble(ax) * ax;
        return new DoubleDouble(ax) + new DoubleDouble(diff.Hi * x * 0.5);
    }

    public static DoubleDouble Exp(DoubleDouble a)
    {
        if (a.Hi == 0.0) return One;
        if (a.Hi <= -746.0) return Zero;
        if (a.Hi >= 710.0) return new DoubleDouble(double.PositiveInfinity);

        const int reduction = 9;
        var k = Math.Floor(a.ToDouble() / Ln2.ToDouble() + 0.5);
        var r = ScaleB(a - Ln2 * k, -reduction);

        var term = r;
        var sum = r;
        for (var n = 2; n <= 16; n++)
        {
            term = term * r / (double)n;
            sum += term;
            if (Math.Abs(term.Hi) < 1e-40 * Math.Abs(sum.Hi)) break;
        }
        for (var i = 0; i < reduction; i++) sum = sum * 2.0 + sum * sum;
        return ScaleB(sum + One, (int)k);
    }

    public static DoubleDouble ExpM1(DoubleDouble a)
    {
        if (a.Hi == 0.0) return Zero;
        if (Math.Abs(a.Hi) > 0.5) return Exp(a) - One;
        var term = a;
        var sum = a;
        for (var n = 2; n <= 60; n++)
        {
            term = term * a / (double)n;
            sum += term;
            if (Math.Abs(term.Hi) < 1e-40 * Math.Abs(sum.Hi)) break;
        }
        return sum;
    }

    public static DoubleDouble Log(DoubleDouble a)
    {
        if (a.Hi <= 0.0) throw new ArgumentException("Log of a non-positive DoubleDouble");
        if (a.Hi == 1.0 && a.Lo == 0.0) return Zero;
        var y = new DoubleDouble(Math.Log(a.ToDouble()));
        y += a * Exp(-y) - One;
        y += a * Exp(-y) - One;
        return y;
    }

    public static DoubleDouble LogP1(DoubleDouble a)
    {
        if (a.Hi == 0.0) return Zero;
        if (Math.Abs(a.Hi) > 0.25) return Log(One + a);
        var y = new DoubleDouble(double.LogP1(a.ToDouble()));
        y += (a - ExpM1(y)) * Exp(-y);
        y += (a - ExpM1(y)) * Exp(-y);
        return y;
    }

    public static (DoubleDouble Sin, DoubleDouble Cos) SinCos(DoubleDouble a)
    {
        if (a.Hi == 0.0) return (Zero, One);
        var t = a - TwoPi * Math.Floor(a.ToDouble() / TwoPi.ToDouble() + 0.5);
        var q = (int)Math.Floor(t.ToDouble() / HalfPi.ToDouble() + 0.5);
        var r = t - HalfPi * q;

        var r2 = r * r;
        DoubleDouble sin = r, sinTerm = r, cos = One, cosTerm = One;
        for (var n = 1; n <= 20; n++)
        {
            sinTerm = -sinTerm * r2 / ((2.0 * n) * (2.0 * n + 1.0));
            cosTerm = -cosTerm * r2 / ((2.0 * n - 1.0) * (2.0 * n));
            sin += sinTerm;
            cos += cosTerm;
            if (Math.Abs(sinTerm.Hi) < 1e-38 && Math.Abs(cosTerm.Hi) < 1e-38) break;
        }
        return (((q % 4) + 4) % 4) switch
        {
            0 => (sin, cos),
            1 => (cos, -sin),
            2 => (-sin, -cos),
            _ => (-cos, sin),
        };
    }

    public static DoubleDouble Atan2(DoubleDouble y, DoubleDouble x)
    {
        if (x.IsZero && y.IsZero) return Zero;
        if (x.IsZero) return y.Sign > 0 ? HalfPi : -HalfPi;
        if (y.IsZero) return x.Sign > 0 ? Zero : Pi;

        var r = Sqrt(x * x + y * y);
        var xx = x / r;
        var yy = y / r;
        var z = new DoubleDouble(Math.Atan2(y.ToDouble(), x.ToDouble()));
        for (var i = 0; i < 2; i++)
        {
            var (sinZ, cosZ) = SinCos(z);
            z += Math.Abs(xx.Hi) > Math.Abs(yy.Hi)
                ? (yy - sinZ) / cosZ
                : -((xx - cosZ) / sinZ);
        }
        return z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDouble FromDouble(double value) => new(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDouble FromDoubleDouble(double hi, double lo)
    {
        if (!double.IsFinite(hi)) return new DoubleDouble(hi, 0.0);
        var (s, e) = TwoSum(hi, lo);
        return new DoubleDouble(s, e);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDouble Add(DoubleDouble a, DoubleDouble b) => a + b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDouble Sub(DoubleDouble a, DoubleDouble b) => a - b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDouble Mul(DoubleDouble a, DoubleDouble b) => a * b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDouble MulByDouble(DoubleDouble a, double b) => a * b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDouble Div(DoubleDouble a, DoubleDouble b) => a / b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DoubleDouble Neg(DoubleDouble a) => -a;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Greater(DoubleDouble a, DoubleDouble b) => a > b;

    public double HighPart
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Hi;
    }

    public static double RelativeUlp => 1e-31;

    public static string RungName => "DD";

    public static int RungIndex => 3;
}

public readonly struct DdComplex
{
    public readonly DoubleDouble Re;
    public readonly DoubleDouble Im;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DdComplex(DoubleDouble re, DoubleDouble im) { Re = re; Im = im; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DdComplex(double re, double im) { Re = new DoubleDouble(re); Im = new DoubleDouble(im); }

    public static readonly DdComplex Zero = new(DoubleDouble.Zero, DoubleDouble.Zero);

    public double MagnitudeSquared
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { var r = Re.ToDouble(); var i = Im.ToDouble(); return r * r + i * i; }
    }

    public DoubleDouble MagnitudeSquaredDd => Re * Re + Im * Im;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DdComplex operator +(DdComplex a, DdComplex b) => new(a.Re + b.Re, a.Im + b.Im);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DdComplex operator -(DdComplex a, DdComplex b) => new(a.Re - b.Re, a.Im - b.Im);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DdComplex operator *(DdComplex a, DdComplex b) =>
        new(a.Re * b.Re - a.Im * b.Im, a.Re * b.Im + a.Im * b.Re);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DdComplex operator *(DdComplex a, DoubleDouble s) => new(a.Re * s, a.Im * s);

    public static DdComplex operator /(DdComplex a, DdComplex b)
    {
        var d = b.Re * b.Re + b.Im * b.Im;
        return new DdComplex((a.Re * b.Re + a.Im * b.Im) / d, (a.Im * b.Re - a.Re * b.Im) / d);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DdComplex ScaleB(DdComplex a, int n) =>
        new(DoubleDouble.ScaleB(a.Re, n), DoubleDouble.ScaleB(a.Im, n));

    public static DdComplex Pow(DdComplex z, double powerReal, double powerImag)
    {
        if (z.Re.IsZero && z.Im.IsZero) return Zero;
        var logR = DoubleDouble.Log(z.Re * z.Re + z.Im * z.Im) * 0.5;
        var theta = DoubleDouble.Atan2(z.Im, z.Re);
        var realPart = logR * powerReal - theta * powerImag;
        var imagPart = theta * powerReal + logR * powerImag;
        var mag = DoubleDouble.Exp(realPart);
        var (sin, cos) = DoubleDouble.SinCos(imagPart);
        return new DdComplex(mag * cos, mag * sin);
    }
}
