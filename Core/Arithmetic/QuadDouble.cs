using System.Runtime.CompilerServices;

namespace FractalAnimator.Core.Kernel;

public readonly struct QuadDouble : IPrecision<QuadDouble>, IEquatable<QuadDouble>
{
    public readonly double X0;
    public readonly double X1;
    public readonly double X2;
    public readonly double X3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public QuadDouble(double x0, double x1, double x2, double x3) { X0 = x0; X1 = x1; X2 = x2; X3 = x3; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public QuadDouble(double value) { X0 = value; X1 = 0.0; X2 = 0.0; X3 = 0.0; }

    public static readonly QuadDouble Ln2 = new(
        6.931471805599453e-01, 2.3190468138462996e-17, 5.707708438416212e-34, -3.5824322106018114e-50);

    static readonly QuadDouble s_pi = new(
        3.141592653589793e+00, 1.2246467991473532e-16, -2.9947698097183397e-33, 1.1124542208633653e-49);

    public static readonly QuadDouble HalfPi = new(
        1.5707963267948966e+00, 6.123233995736766e-17, -1.4973849048591698e-33, 5.562271104316826e-50);

    public static readonly QuadDouble TwoPi = new(
        6.283185307179586e+00, 2.4492935982947064e-16, -5.989539619436679e-33, 2.2249084417267306e-49);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QuadDouble FromDouble(double value) => new(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QuadDouble FromDoubleDouble(double hi, double lo)
    {
        var (s, e) = QuickTwoSum(hi, lo);
        return new QuadDouble(s, e, 0.0, 0.0);
    }

    public static QuadDouble Zero { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => default; }
    public static QuadDouble One { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new(1.0); }
    public static QuadDouble Pi { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => s_pi; }

    public static double RelativeUlp => 1e-62;
    public static string RungName => "QD";
    public static int RungIndex => 3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToDouble() => X0 + X1;

    public double HighPart { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => X0; }

    public bool IsZero { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => X0 == 0.0; }
    public int Sign => X0 > 0.0 ? 1 : X0 < 0.0 ? -1 : 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Greater(QuadDouble a, QuadDouble b) =>
        a.X0 > b.X0 || (a.X0 == b.X0 &&
        (a.X1 > b.X1 || (a.X1 == b.X1 &&
        (a.X2 > b.X2 || (a.X2 == b.X2 && a.X3 > b.X3)))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    double Limb(int i) => i switch { 0 => X0, 1 => X1, 2 => X2, _ => X3 };

    public bool Equals(QuadDouble other) => X0 == other.X0 && X1 == other.X1 && X2 == other.X2 && X3 == other.X3;
    public override bool Equals(object? obj) => obj is QuadDouble q && Equals(q);
    public override int GetHashCode() => HashCode.Combine(X0, X1, X2, X3);

    public override string ToString() => ToDouble().ToString("R");

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
    static void ThreeSum(ref double a, ref double b, ref double c)
    {
        var (t1, t2) = TwoSum(a, b);
        var (s, t3) = TwoSum(c, t1);
        a = s;
        (b, c) = TwoSum(t2, t3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void ThreeSum2(ref double a, ref double b, double c)
    {
        var (t1, t2) = TwoSum(a, b);
        var (s, t3) = TwoSum(c, t1);
        a = s;
        b = t2 + t3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double QuickThreeAccum(ref double a, ref double b, double c)
    {
        var (bc, bErr) = TwoSum(b, c);
        b = bErr;
        var (s, aErr) = TwoSum(a, bc);
        a = aErr;

        var aNonZero = a != 0.0;
        var bNonZero = b != 0.0;
        if (aNonZero && bNonZero) return s;

        if (!bNonZero) { b = a; a = s; }
        else { a = s; }
        return 0.0;
    }

    static QuadDouble Renormalize(double c0, double c1, double c2, double c3)
    {
        if (double.IsInfinity(c0) || double.IsNaN(c0)) return new QuadDouble(c0, 0.0, 0.0, 0.0);

        double s0, s1, s2 = 0.0, s3 = 0.0;

        (s0, c3) = QuickTwoSum(c2, c3);
        (s0, c2) = QuickTwoSum(c1, s0);
        (c0, c1) = QuickTwoSum(c0, s0);

        s0 = c0;
        s1 = c1;
        if (s1 != 0.0)
        {
            (s1, s2) = QuickTwoSum(s1, c2);
            if (s2 != 0.0) (s2, s3) = QuickTwoSum(s2, c3);
            else (s1, s2) = QuickTwoSum(s1, c3);
        }
        else
        {
            (s0, s1) = QuickTwoSum(s0, c2);
            if (s1 != 0.0) (s1, s2) = QuickTwoSum(s1, c3);
            else (s0, s1) = QuickTwoSum(s0, c3);
        }
        return new QuadDouble(s0, s1, s2, s3);
    }

    static QuadDouble Renormalize(double c0, double c1, double c2, double c3, double c4)
    {
        if (double.IsInfinity(c0) || double.IsNaN(c0)) return new QuadDouble(c0, 0.0, 0.0, 0.0);

        double s0, s1, s2 = 0.0, s3 = 0.0;

        (s0, c4) = QuickTwoSum(c3, c4);
        (s0, c3) = QuickTwoSum(c2, s0);
        (s0, c2) = QuickTwoSum(c1, s0);
        (c0, c1) = QuickTwoSum(c0, s0);

        s0 = c0;
        s1 = c1;
        if (s1 != 0.0)
        {
            (s1, s2) = QuickTwoSum(s1, c2);
            if (s2 != 0.0)
            {
                (s2, s3) = QuickTwoSum(s2, c3);
                if (s3 != 0.0) s3 += c4; else s2 += c4;
            }
            else
            {
                (s1, s2) = QuickTwoSum(s1, c3);
                if (s2 != 0.0) (s2, s3) = QuickTwoSum(s2, c4);
                else (s1, s2) = QuickTwoSum(s1, c4);
            }
        }
        else
        {
            (s0, s1) = QuickTwoSum(s0, c2);
            if (s1 != 0.0)
            {
                (s1, s2) = QuickTwoSum(s1, c3);
                if (s2 != 0.0) (s2, s3) = QuickTwoSum(s2, c4);
                else (s1, s2) = QuickTwoSum(s1, c4);
            }
            else
            {
                (s0, s1) = QuickTwoSum(s0, c3);
                if (s1 != 0.0) (s1, s2) = QuickTwoSum(s1, c4);
                else (s0, s1) = QuickTwoSum(s0, c4);
            }
        }
        return new QuadDouble(s0, s1, s2, s3);
    }

    public static QuadDouble Add(QuadDouble a, QuadDouble b)
    {
        double x0 = 0.0, x1 = 0.0, x2 = 0.0, x3 = 0.0;

        void Store(int slot, double value)
        {
            switch (slot)
            {
                case 0: x0 = value; break;
                case 1: x1 = value; break;
                case 2: x2 = value; break;
                default: x3 = value; break;
            }
        }

        int i = 0, j = 0, k = 0;
        double u, v;
        if (Math.Abs(a.X0) > Math.Abs(b.X0)) { u = a.X0; i++; } else { u = b.X0; j++; }
        if (Math.Abs(a.Limb(i)) > Math.Abs(b.Limb(j))) { v = a.Limb(i); i++; } else { v = b.Limb(j); j++; }
        (u, v) = QuickTwoSum(u, v);

        while (k < 4)
        {
            if (i >= 4 && j >= 4)
            {
                Store(k, u);
                if (k < 3) { k++; Store(k, v); }
                break;
            }

            double t;
            if (i >= 4) { t = b.Limb(j); j++; }
            else if (j >= 4) { t = a.Limb(i); i++; }
            else if (Math.Abs(a.Limb(i)) > Math.Abs(b.Limb(j))) { t = a.Limb(i); i++; }
            else { t = b.Limb(j); j++; }

            var s = QuickThreeAccum(ref u, ref v, t);
            if (s != 0.0) { Store(k, s); k++; }
        }

        for (; i < 4; i++) x3 += a.Limb(i);
        for (; j < 4; j++) x3 += b.Limb(j);

        return Renormalize(x0, x1, x2, x3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QuadDouble Sub(QuadDouble a, QuadDouble b) => Add(a, Neg(b));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QuadDouble Neg(QuadDouble a) => new(-a.X0, -a.X1, -a.X2, -a.X3);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QuadDouble Abs(QuadDouble a) => a.X0 < 0.0 ? Neg(a) : a;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QuadDouble ScaleB(QuadDouble a, int n) =>
        new(Math.ScaleB(a.X0, n), Math.ScaleB(a.X1, n), Math.ScaleB(a.X2, n), Math.ScaleB(a.X3, n));

    public static QuadDouble Mul(QuadDouble a, QuadDouble b)
    {
        var (p0, q0) = TwoProd(a.X0, b.X0);

        var (p1, q1) = TwoProd(a.X0, b.X1);
        var (p2, q2) = TwoProd(a.X1, b.X0);

        var (p3, q3) = TwoProd(a.X0, b.X2);
        var (p4, q4) = TwoProd(a.X1, b.X1);
        var (p5, q5) = TwoProd(a.X2, b.X0);

        ThreeSum(ref p1, ref p2, ref q0);

        ThreeSum(ref p2, ref q1, ref q2);
        ThreeSum(ref p3, ref p4, ref p5);
        var (s0, t0) = TwoSum(p2, p3);
        var (s1, t1) = TwoSum(q1, p4);
        var s2 = q2 + p5;
        (s1, t0) = TwoSum(s1, t0);
        s2 += t0 + t1;

        var (p6, q6) = TwoProd(a.X0, b.X3);
        var (p7, q7) = TwoProd(a.X1, b.X2);
        var (p8, q8) = TwoProd(a.X2, b.X1);
        var (p9, q9) = TwoProd(a.X3, b.X0);

        (q0, q3) = TwoSum(q0, q3);
        (q4, q5) = TwoSum(q4, q5);
        (p6, p7) = TwoSum(p6, p7);
        (p8, p9) = TwoSum(p8, p9);
        (t0, t1) = TwoSum(q0, q4);
        t1 += q3 + q5;
        var (r0, r1) = TwoSum(p6, p8);
        r1 += p7 + p9;
        (q3, q4) = TwoSum(t0, r0);
        q4 += t1 + r1;
        (t0, t1) = TwoSum(q3, s1);
        t1 += q4;

        t1 += a.X1 * b.X3 + a.X2 * b.X2 + a.X3 * b.X1 + q6 + q7 + q8 + q9 + s2;

        return Renormalize(p0, p1, s0, t0, t1);
    }

    public static QuadDouble MulByDouble(QuadDouble a, double b)
    {
        var (p0, q0) = TwoProd(a.X0, b);
        var (p1, q1) = TwoProd(a.X1, b);
        var (p2, q2) = TwoProd(a.X2, b);
        var p3 = a.X3 * b;

        var s0 = p0;
        var (s1, s2) = TwoSum(q0, p1);

        ThreeSum(ref s2, ref q1, ref p2);
        ThreeSum2(ref q1, ref q2, p3);
        var s3 = q1;
        var s4 = q2 + p2;

        return Renormalize(s0, s1, s2, s3, s4);
    }

    public static QuadDouble Div(QuadDouble a, QuadDouble b)
    {
        var q0 = a.X0 / b.X0;
        var r = Sub(a, MulByDouble(b, q0));

        var q1 = r.X0 / b.X0;
        r = Sub(r, MulByDouble(b, q1));

        var q2 = r.X0 / b.X0;
        r = Sub(r, MulByDouble(b, q2));

        var q3 = r.X0 / b.X0;
        r = Sub(r, MulByDouble(b, q3));

        var q4 = r.X0 / b.X0;
        return Renormalize(q0, q1, q2, q3, q4);
    }

    public static QuadDouble DivByDouble(QuadDouble a, double b)
    {
        var q0 = a.X0 / b;
        var (t0, t1) = TwoProd(q0, b);
        var r = Sub(a, new QuadDouble(t0, t1, 0.0, 0.0));

        var q1 = r.X0 / b;
        (t0, t1) = TwoProd(q1, b);
        r = Sub(r, new QuadDouble(t0, t1, 0.0, 0.0));

        var q2 = r.X0 / b;
        (t0, t1) = TwoProd(q2, b);
        r = Sub(r, new QuadDouble(t0, t1, 0.0, 0.0));

        var q3 = r.X0 / b;
        return Renormalize(q0, q1, q2, q3);
    }

    public static QuadDouble Sqrt(QuadDouble a)
    {
        if (a.X0 == 0.0) return Zero;
        if (a.X0 < 0.0) throw new ArgumentException("Sqrt of a negative QuadDouble", nameof(a));

        var r = new QuadDouble(1.0 / Math.Sqrt(a.X0));
        var half = new QuadDouble(0.5);
        var h = ScaleB(a, -1);
        for (var i = 0; i < 3; i++)
            r = Add(r, Mul(Sub(half, Mul(h, Mul(r, r))), r));
        return Mul(r, a);
    }

    public static QuadDouble Exp(QuadDouble a)
    {
        if (a.X0 == 0.0) return One;
        if (a.X0 <= -746.0) return Zero;
        if (a.X0 >= 710.0) return new QuadDouble(double.PositiveInfinity);

        const int reduction = 16;
        var k = Math.Floor(a.ToDouble() / Ln2.ToDouble() + 0.5);
        var r = ScaleB(Sub(a, MulByDouble(Ln2, k)), -reduction);

        var term = r;
        var sum = r;
        for (var n = 2; n <= 24; n++)
        {
            term = DivByDouble(Mul(term, r), n);
            sum = Add(sum, term);
            if (Math.Abs(term.X0) < 1e-72 * Math.Abs(sum.X0)) break;
        }

        for (var i = 0; i < reduction; i++) sum = Add(MulByDouble(sum, 2.0), Mul(sum, sum));
        return ScaleB(Add(sum, One), (int)k);
    }

    public static QuadDouble ExpM1(QuadDouble a)
    {
        if (a.X0 == 0.0) return Zero;
        if (Math.Abs(a.X0) > 0.5) return Sub(Exp(a), One);

        var term = a;
        var sum = a;
        for (var n = 2; n <= 99; n++)
        {
            term = DivByDouble(Mul(term, a), n);
            sum = Add(sum, term);
            if (Math.Abs(term.X0) < 1e-72 * Math.Abs(sum.X0)) break;
        }
        return sum;
    }

    public static QuadDouble Log(QuadDouble a)
    {
        if (a.X0 <= 0.0) throw new ArgumentException("Log of a non-positive QuadDouble", nameof(a));
        if (a.X0 == 1.0 && a.X1 == 0.0 && a.X2 == 0.0 && a.X3 == 0.0) return Zero;

        var offset = Sub(a, One);
        if (Math.Abs(offset.X0) < 0.25) return LogP1(offset);

        var y = new QuadDouble(Math.Log(a.ToDouble()));
        for (var i = 0; i < 3; i++) y = Add(y, Sub(Mul(a, Exp(Neg(y))), One));
        return y;
    }

    public static QuadDouble LogP1(QuadDouble a)
    {
        if (a.X0 == 0.0) return Zero;
        if (Math.Abs(a.X0) > 0.25) return Log(Add(One, a));

        var y = new QuadDouble(double.LogP1(a.ToDouble()));
        for (var i = 0; i < 3; i++) y = Add(y, Mul(Sub(a, ExpM1(y)), Exp(Neg(y))));
        return y;
    }

    public static (QuadDouble Sin, QuadDouble Cos) SinCos(QuadDouble a)
    {
        if (a.X0 == 0.0) return (Zero, One);

        var t = Sub(a, MulByDouble(TwoPi, Math.Floor(a.ToDouble() / TwoPi.ToDouble() + 0.5)));
        var q = (int)Math.Floor(t.ToDouble() / HalfPi.ToDouble() + 0.5);
        var r = Sub(t, MulByDouble(HalfPi, q));

        var r2 = Mul(r, r);
        QuadDouble sin = r, sinTerm = r, cos = One, cosTerm = One;
        for (var n = 1; n <= 44; n++)
        {
            sinTerm = DivByDouble(Mul(Neg(sinTerm), r2), (2.0 * n) * (2.0 * n + 1.0));
            cosTerm = DivByDouble(Mul(Neg(cosTerm), r2), (2.0 * n - 1.0) * (2.0 * n));
            sin = Add(sin, sinTerm);
            cos = Add(cos, cosTerm);
            if (Math.Abs(sinTerm.X0) < 1e-70 && Math.Abs(cosTerm.X0) < 1e-70) break;
        }

        return (((q % 4) + 4) % 4) switch
        {
            0 => (sin, cos),
            1 => (cos, Neg(sin)),
            2 => (Neg(sin), Neg(cos)),
            _ => (Neg(cos), sin),
        };
    }

    public static QuadDouble Atan2(QuadDouble y, QuadDouble x)
    {
        if (x.IsZero && y.IsZero) return Zero;
        if (x.IsZero) return y.Sign > 0 ? HalfPi : Neg(HalfPi);
        if (y.IsZero) return x.Sign > 0 ? Zero : s_pi;

        var r = Sqrt(Add(Mul(x, x), Mul(y, y)));
        var xx = Div(x, r);
        var yy = Div(y, r);
        var z = new QuadDouble(Math.Atan2(y.ToDouble(), x.ToDouble()));

        for (var i = 0; i < 3; i++)
        {
            var (sinZ, cosZ) = SinCos(z);
            z = Math.Abs(xx.X0) > Math.Abs(yy.X0)
                ? Add(z, Div(Sub(yy, sinZ), cosZ))
                : Sub(z, Div(Sub(xx, cosZ), sinZ));
        }
        return z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QuadDouble operator +(QuadDouble a, QuadDouble b) => Add(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QuadDouble operator -(QuadDouble a, QuadDouble b) => Sub(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QuadDouble operator -(QuadDouble a) => Neg(a);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QuadDouble operator *(QuadDouble a, QuadDouble b) => Mul(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QuadDouble operator *(QuadDouble a, double b) => MulByDouble(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QuadDouble operator /(QuadDouble a, QuadDouble b) => Div(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static QuadDouble operator /(QuadDouble a, double b) => DivByDouble(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator >(QuadDouble a, QuadDouble b) => Greater(a, b);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator <(QuadDouble a, QuadDouble b) => Greater(b, a);
    public static bool operator ==(QuadDouble a, QuadDouble b) => a.Equals(b);
    public static bool operator !=(QuadDouble a, QuadDouble b) => !a.Equals(b);
}
