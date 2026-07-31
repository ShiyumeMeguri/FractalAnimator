using System.Runtime.CompilerServices;

namespace FractalAnimator.Core.Kernel;

public readonly struct FloatFloat : IPrecision<FloatFloat>, IEquatable<FloatFloat>
{
    public readonly float Hi;
    public readonly float Lo;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FloatFloat(float hi, float lo) { Hi = hi; Lo = lo; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FloatFloat(float value) { Hi = value; Lo = 0f; }

    const float Ln2Hi = 0.693147182f, Ln2Mid = -1.90465421e-09f, Ln2Lo = -8.78318374e-17f;
    const float PiHi = 3.14159274f, PiMid = -8.74227766e-08f;
    const float HalfPiHi = 1.57079637f, HalfPiMid = -4.37113883e-08f, HalfPiLo = -1.71512451e-15f;
    const float TwoPiHi = 6.28318548f, TwoPiMid = -1.74845553e-07f;

    const double Ln2AsDouble = 0.6931471805599453;
    const double HalfPiAsDouble = 1.5707963267948966;

    public static readonly FloatFloat Ln2 = new(Ln2Hi, Ln2Mid);
    public static readonly FloatFloat HalfPi = new(HalfPiHi, HalfPiMid);
    public static readonly FloatFloat TwoPi = new(TwoPiHi, TwoPiMid);

    public static double RelativeUlp => 3e-15;
    public static string RungName => "FF32";
    public static int RungIndex => 1;

    public static FloatFloat Zero { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new(0f, 0f); }
    public static FloatFloat One { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new(1f, 0f); }
    public static FloatFloat Pi { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new(PiHi, PiMid); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat FromDouble(double value)
    {
        var hi = (float)value;
        if (!float.IsFinite(hi)) return new FloatFloat(hi, 0f);
        return Normalize(hi, (float)(value - hi));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat FromDoubleDouble(double hi, double lo)
    {
        var h = (float)hi;
        if (!float.IsFinite(h)) return new FloatFloat(h, 0f);
        return Normalize(h, (float)((hi - h) + lo));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToDouble() => (double)Hi + Lo;

    public double HighPart { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Hi; }

    public bool IsZero { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Hi == 0f; }
    public int Sign => Hi > 0f ? 1 : Hi < 0f ? -1 : 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Greater(FloatFloat a, FloatFloat b) => a > b;

    public bool Equals(FloatFloat other) => Hi == other.Hi && Lo == other.Lo;
    public override bool Equals(object? obj) => obj is FloatFloat f && Equals(f);
    public override int GetHashCode() => HashCode.Combine(Hi, Lo);
    public override string ToString() => ToDouble().ToString("R");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static (float s, float e) TwoSum(float a, float b)
    {
        var s = a + b;
        var bb = s - a;
        return (s, (a - (s - bb)) + (b - bb));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static (float s, float e) QuickTwoSum(float a, float b)
    {
        var s = a + b;
        return (s, b - (s - a));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static (float p, float e) TwoProd(float a, float b)
    {
        var p = a * b;
        return (p, MathF.FusedMultiplyAdd(a, b, -p));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static FloatFloat Normalize(float hi, float lo)
    {
        var (s, e) = QuickTwoSum(hi, lo);
        return new FloatFloat(s, e);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static FloatFloat ExactProduct(float a, float b)
    {
        var (p, e) = TwoProd(a, b);
        return new FloatFloat(p, e);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat operator +(FloatFloat a, FloatFloat b)
    {
        var (s1, e1) = TwoSum(a.Hi, b.Hi);
        var (s2, e2) = TwoSum(a.Lo, b.Lo);
        e1 += s2;
        (s1, e1) = QuickTwoSum(s1, e1);
        e1 += e2;
        (s1, e1) = QuickTwoSum(s1, e1);
        return new FloatFloat(s1, e1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat operator -(FloatFloat a, FloatFloat b) => a + (-b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat operator -(FloatFloat a) => new(-a.Hi, -a.Lo);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat operator *(FloatFloat a, FloatFloat b)
    {
        var (p, e) = TwoProd(a.Hi, b.Hi);
        e += a.Hi * b.Lo + a.Lo * b.Hi;
        (p, e) = QuickTwoSum(p, e);
        return new FloatFloat(p, e);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat operator *(FloatFloat a, float b)
    {
        var (p, e) = TwoProd(a.Hi, b);
        e += a.Lo * b;
        (p, e) = QuickTwoSum(p, e);
        return new FloatFloat(p, e);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat operator /(FloatFloat a, float b)
    {
        var q1 = a.Hi / b;
        var (p, e) = TwoProd(q1, b);
        var r = a - new FloatFloat(p, e);
        var q2 = (r.Hi + r.Lo) / b;
        var (s, err) = QuickTwoSum(q1, q2);
        return new FloatFloat(s, err);
    }

    public static FloatFloat operator /(FloatFloat a, FloatFloat b)
    {
        var q1 = a.Hi / b.Hi;
        var r = a - b * q1;
        var q2 = r.Hi / b.Hi;
        r -= b * q2;
        var q3 = r.Hi / b.Hi;
        var (s, e) = QuickTwoSum(q1, q2);
        return new FloatFloat(s, e) + new FloatFloat(q3);
    }

    public static bool operator <(FloatFloat a, FloatFloat b) => a.Hi < b.Hi || (a.Hi == b.Hi && a.Lo < b.Lo);
    public static bool operator >(FloatFloat a, FloatFloat b) => a.Hi > b.Hi || (a.Hi == b.Hi && a.Lo > b.Lo);
    public static bool operator ==(FloatFloat a, FloatFloat b) => a.Equals(b);
    public static bool operator !=(FloatFloat a, FloatFloat b) => !a.Equals(b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat Add(FloatFloat a, FloatFloat b) => a + b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat Sub(FloatFloat a, FloatFloat b) => a - b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat Mul(FloatFloat a, FloatFloat b) => a * b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat Div(FloatFloat a, FloatFloat b) => a / b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat Neg(FloatFloat a) => -a;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat MulByDouble(FloatFloat a, double b) => a * FromDouble(b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat ScaleB(FloatFloat a, int n) =>
        new(MathF.ScaleB(a.Hi, n), MathF.ScaleB(a.Lo, n));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FloatFloat Abs(FloatFloat a) => a.Hi < 0f ? -a : a;

    public static FloatFloat Sqrt(FloatFloat a)
    {
        if (a.Hi == 0f) return Zero;
        if (a.Hi < 0f) throw new ArgumentException("Sqrt of a negative FloatFloat");

        var x = 1f / MathF.Sqrt(a.Hi);
        var halfX = x * 0.5f;
        var y = new FloatFloat(a.Hi * x);
        y += (a - y * y) * halfX;
        y += (a - y * y) * halfX;
        return y;
    }

    const float ExpOverflow = 88.8f;
    const float ExpUnderflow = -104f;

    const int ExpReduction = 6;

    public static FloatFloat Exp(FloatFloat a)
    {
        if (a.Hi == 0f) return One;
        if (a.Hi <= ExpUnderflow) return Zero;
        if (a.Hi >= ExpOverflow) return new FloatFloat(float.PositiveInfinity);

        var k = (float)Math.Floor(a.ToDouble() / Ln2AsDouble + 0.5);

        var r = a - ExactProduct(Ln2Hi, k);
        r -= ExactProduct(Ln2Mid, k);
        r -= ExactProduct(Ln2Lo, k);
        r = ScaleB(r, -ExpReduction);

        var term = r;
        var sum = r;
        for (var n = 2; n <= 12; n++)
        {
            term = term * r / (float)n;
            sum += term;
            if (IsNegligible(term.Hi, sum.Hi)) break;
        }
        for (var i = 0; i < ExpReduction; i++) sum = ScaleB(sum, 1) + sum * sum;
        return ScaleB(sum + One, (int)k);
    }

    public static FloatFloat ExpM1(FloatFloat a)
    {
        if (a.Hi == 0f) return Zero;
        if (MathF.Abs(a.Hi) > 0.5f) return Exp(a) - One;
        var term = a;
        var sum = a;
        for (var n = 2; n <= 20; n++)
        {
            term = term * a / (float)n;
            sum += term;
            if (IsNegligible(term.Hi, sum.Hi)) break;
        }
        return sum;
    }

    public static FloatFloat Log(FloatFloat a)
    {
        if (a.Hi <= 0f) throw new ArgumentException("Log of a non-positive FloatFloat");
        if (a.Hi == 1f && a.Lo == 0f) return Zero;

        var y = new FloatFloat(MathF.Log(a.Hi));
        for (var i = 0; i < 2; i++)
        {
            var ratio = y.Hi > 0f ? a / Exp(y) : a * Exp(-y);
            y += ratio - One;
        }
        return y;
    }

    public static FloatFloat LogP1(FloatFloat a)
    {
        if (a.Hi == 0f) return Zero;
        if (MathF.Abs(a.Hi) > 0.25f) return Log(One + a);
        var y = new FloatFloat(float.LogP1(a.Hi));
        y += (a - ExpM1(y)) * Exp(-y);
        y += (a - ExpM1(y)) * Exp(-y);
        return y;
    }

    public static (FloatFloat Sin, FloatFloat Cos) SinCos(FloatFloat a)
    {
        if (a.Hi == 0f) return (Zero, One);

        var q = (float)Math.Floor(a.ToDouble() / HalfPiAsDouble + 0.5);
        var r = a - ExactProduct(HalfPiHi, q);
        r -= ExactProduct(HalfPiMid, q);
        r -= ExactProduct(HalfPiLo, q);

        var quadrant = (int)(q - 4f * MathF.Floor(q * 0.25f));

        var r2 = r * r;
        FloatFloat sin = r, sinTerm = r, cos = One, cosTerm = One;
        for (var n = 1; n <= 14; n++)
        {
            sinTerm = -sinTerm * r2 / ((2f * n) * (2f * n + 1f));
            cosTerm = -cosTerm * r2 / ((2f * n - 1f) * (2f * n));
            sin += sinTerm;
            cos += cosTerm;
            if (Math.Abs((double)sinTerm.Hi) < 1e-20 && Math.Abs((double)cosTerm.Hi) < 1e-20) break;
        }
        return quadrant switch
        {
            0 => (sin, cos),
            1 => (cos, -sin),
            2 => (-sin, -cos),
            _ => (-cos, sin),
        };
    }

    public static FloatFloat Atan2(FloatFloat y, FloatFloat x)
    {
        if (x.IsZero && y.IsZero) return Zero;
        if (x.IsZero) return y.Sign > 0 ? HalfPi : -HalfPi;
        if (y.IsZero) return x.Sign > 0 ? Zero : Pi;

        var r = Sqrt(x * x + y * y);
        var xx = x / r;
        var yy = y / r;
        var z = new FloatFloat(MathF.Atan2(y.Hi, x.Hi));
        for (var i = 0; i < 2; i++)
        {
            var (sinZ, cosZ) = SinCos(z);
            z += MathF.Abs(xx.Hi) > MathF.Abs(yy.Hi)
                ? (yy - sinZ) / cosZ
                : -((xx - cosZ) / sinZ);
        }
        return z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool IsNegligible(float term, float sum) =>
        Math.Abs((double)term) < 1e-20 * Math.Abs((double)sum);
}
