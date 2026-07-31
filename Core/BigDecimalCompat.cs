using System.Globalization;
using System.Numerics;

namespace FractalAnimator.Core;

public readonly record struct MathContextCompat(int Precision);

public readonly struct BigDecimalCompat : IComparable<BigDecimalCompat>
{
    public BigInteger UnscaledValue { get; }
    public int Scale { get; }
    public BigDecimalCompat(BigInteger unscaledValue, int scale) { UnscaledValue = unscaledValue; Scale = scale; }
    public static BigDecimalCompat Zero => new BigDecimalCompat(BigInteger.Zero, 0);

    public static BigDecimalCompat Parse(string s)
    {
        s = s.Trim();
        var ei = s.IndexOfAny(['e', 'E']);
        var exp = 0;
        if (ei >= 0) { exp = int.Parse(s[(ei + 1)..], CultureInfo.InvariantCulture); s = s[..ei]; }
        var sign = 1;
        if (s[0] == '+') s = s[1..];
        else if (s[0] == '-') { sign = -1; s = s[1..]; }
        var pi = s.IndexOf('.');
        var sc = 0;
        if (pi >= 0) { sc = s.Length - pi - 1; s = s.Remove(pi, 1); }
        s = s.TrimStart('0');
        if (s.Length == 0) return Zero;
        return new BigDecimalCompat(BigInteger.Parse(s, CultureInfo.InvariantCulture) * sign, sc - exp);
    }

    public static BigDecimalCompat FromDouble(double v) => Parse(v.ToString("R", CultureInfo.InvariantCulture));

    /// <summary>
    /// The exact value of a double, from its bits. <see cref="FromDouble"/> goes through "R" formatting,
    /// which yields the shortest round-tripping text — so it parses the literal 0.2 rather than the
    /// double 0.2000000000000000111…. That 1e-17 discrepancy is invisible in double work but becomes the
    /// dominant error the moment a result is carried at double-double or higher precision.
    /// </summary>
    public static BigDecimalCompat FromDoubleExact(double v)
    {
        if (v == 0.0) return Zero;
        var bits = BitConverter.DoubleToInt64Bits(v);
        var negative = bits < 0;
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exponent == 0) exponent = 1; else mantissa |= 1L << 52;
        exponent -= 1075;
        var m = new BigDecimalCompat(negative ? -mantissa : mantissa, 0);
        return m.Multiply(Pow2(exponent), new MathContextCompat(400));
    }

    /// <summary>
    /// Narrows to double-double: the nearest double, plus the remainder that double could not hold.
    /// This is how an arbitrary-precision reference orbit is handed to the double-double pixel loop
    /// without throwing away the ~16 extra digits that stop the reconstruction from cancelling.
    /// </summary>
    /// <summary>
    /// Splits into a descending sequence of exactly-representable doubles whose sum is this value to the
    /// span's capacity. This is how an arbitrary-precision reference orbit is handed to ANY rung of the
    /// precision ladder without a per-rung conversion: a rung reconstructs the value by summing as many
    /// limbs as it can hold and harmlessly rounding away the rest, so one baked table serves float-float,
    /// double-double and quad-double alike.
    /// </summary>
    public void ToLimbs(Span<double> limbs)
    {
        var ctx = new MathContextCompat(limbs.Length * 20 + 24);
        var remainder = this;
        for (var i = 0; i < limbs.Length; i++)
        {
            var limb = remainder.ToDouble();
            if (!double.IsFinite(limb) || limb == 0.0)
            {
                limbs[i] = 0.0;
                continue;
            }
            limbs[i] = limb;
            remainder = remainder.Subtract(FromDoubleExact(limb), ctx);
        }
    }

    public DoubleDouble ToDoubleDouble()
    {
        var hi = ToDouble();
        if (!double.IsFinite(hi) || hi == 0.0) return new DoubleDouble(hi);
        var ctx = new MathContextCompat(48);
        var lo = Subtract(FromDoubleExact(hi), ctx).ToDouble();
        return new DoubleDouble(hi, lo);
    }

    /// <summary>
    /// Exact <c>2^exponent</c>. For negative exponents this stays exact because
    /// <c>2^-e = 5^e * 10^-e</c>, which a base-10 BigDecimal represents without rounding.
    /// </summary>
    public static BigDecimalCompat Pow2(int exponent)
    {
        if (exponent == 0) return new BigDecimalCompat(BigInteger.One, 0);
        if (exponent > 0) return new BigDecimalCompat(BigInteger.Pow(2, exponent), 0);
        return new BigDecimalCompat(BigInteger.Pow(5, -exponent), -exponent);
    }

    public BigDecimalCompat Add(BigDecimalCompat o, MathContextCompat ctx) { Align(o, out var l, out var r, out var sc); return new BigDecimalCompat(l + r, sc).Round(ctx); }
    public BigDecimalCompat Subtract(BigDecimalCompat o, MathContextCompat ctx) { Align(o, out var l, out var r, out var sc); return new BigDecimalCompat(l - r, sc).Round(ctx); }
    public BigDecimalCompat Multiply(BigDecimalCompat o, MathContextCompat ctx) => new BigDecimalCompat(UnscaledValue * o.UnscaledValue, Scale + o.Scale).Round(ctx);
    public BigDecimalCompat Pow(int n, MathContextCompat ctx)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        if (n == 0) return new BigDecimalCompat(BigInteger.One, 0);
        var r = this; for (var i = 1; i < n; i++) r = r.Multiply(this, ctx);
        return r.Round(ctx);
    }

    /// <summary>Exact decimal text (no scientific notation), suitable for round-tripping a coordinate.</summary>
    public string ToPlainString()
    {
        if (UnscaledValue.IsZero) return "0";
        var digits = BigInteger.Abs(UnscaledValue).ToString(CultureInfo.InvariantCulture);
        var sign = UnscaledValue.Sign < 0 ? "-" : "";
        if (Scale <= 0) return sign + digits + new string('0', -Scale);
        if (digits.Length <= Scale) digits = new string('0', Scale - digits.Length + 1) + digits;
        var pointIndex = digits.Length - Scale;
        return sign + digits[..pointIndex] + "." + digits[pointIndex..];
    }

    public double ToDouble()
    {
        if (UnscaledValue.IsZero) return 0d;
        var s = BigInteger.Abs(UnscaledValue).ToString(CultureInfo.InvariantCulture);
        var take = Math.Min(16, s.Length);
        var sig = double.Parse(s[..take], CultureInfo.InvariantCulture) / Math.Pow(10, take - 1);
        return (UnscaledValue.Sign < 0 ? -1 : 1) * sig * Math.Pow(10, s.Length - 1 - Scale);
    }

    public int CompareTo(BigDecimalCompat o) { Align(o, out var l, out var r, out _); return l.CompareTo(r); }

    public BigDecimalCompat Negate() => new(-UnscaledValue, Scale);
    public BigDecimalCompat Abs() => UnscaledValue.Sign < 0 ? Negate() : this;

    /// <summary>floor(log10(|value|)) — exact from the digit count, no double round-trip. Used to decide
    /// when a Taylor term has become negligible at a given target precision.</summary>
    public int DecimalExponent() =>
        UnscaledValue.IsZero ? int.MinValue : BigInteger.Abs(UnscaledValue).ToString(CultureInfo.InvariantCulture).Length - Scale - 1;

    /// <summary>
    /// Division by a small integer, which is what every Taylor/series loop in this file actually needs
    /// (term/n, term/(2n+1), term/(2n(2n+1))...). The general <see cref="Divide"/> has to shift the
    /// numerator by the full working precision and then run a big DivRem; against a tiny divisor that
    /// work is almost entirely wasted. Dividing the unscaled BigInteger directly, after a shift just
    /// large enough to keep the requested digits, is the same result for a fraction of the cost — and
    /// these loops run hundreds of times per ComplexPow, which dominates deep-frame render time.
    /// </summary>
    public BigDecimalCompat DivideByInt(long divisor, MathContextCompat ctx)
    {
        if (divisor == 0) throw new DivideByZeroException();
        if (UnscaledValue.IsZero) return Zero;
        var digits = BigInteger.Abs(UnscaledValue).ToString(CultureInfo.InvariantCulture).Length;
        var shift = Math.Max(0, Math.Max(ctx.Precision, 1) + 6 - digits);
        var num = shift == 0 ? UnscaledValue : UnscaledValue * BigInteger.Pow(10, shift);
        var q = num / divisor;
        return new BigDecimalCompat(q, Scale + shift).Round(ctx);
    }

    public BigDecimalCompat Divide(BigDecimalCompat o, MathContextCompat ctx)
    {
        if (o.UnscaledValue.IsZero) throw new DivideByZeroException();
        if (UnscaledValue.IsZero) return Zero;
        var numDigits = BigInteger.Abs(UnscaledValue).ToString(CultureInfo.InvariantCulture).Length;
        var denDigits = BigInteger.Abs(o.UnscaledValue).ToString(CultureInfo.InvariantCulture).Length;
        var shift = Math.Max(ctx.Precision, 1) + 12 - (numDigits - denDigits);
        if (shift < 0) shift = 0;
        var num = UnscaledValue * BigInteger.Pow(10, shift);
        var (q, rem) = BigInteger.DivRem(num, o.UnscaledValue);
        var qSign = Math.Sign(num.Sign * o.UnscaledValue.Sign);
        if (BigInteger.Abs(rem) * 2 >= BigInteger.Abs(o.UnscaledValue)) q += qSign >= 0 ? BigInteger.One : BigInteger.MinusOne;
        return new BigDecimalCompat(q, Scale - o.Scale + shift).Round(ctx);
    }

    public BigDecimalCompat Sqrt(MathContextCompat ctx)
    {
        if (UnscaledValue.Sign < 0) throw new ArgumentException("Sqrt of a negative BigDecimalCompat");
        if (UnscaledValue.IsZero) return Zero;
        var target = ctx.Precision + 15;
        var seed = Math.Sqrt(ToDouble());
        var x = FromDouble(double.IsFinite(seed) && seed > 0 ? seed : 1.0);

        // Same precision ladder as Ln: Newton doubles correct digits per step, so running the early
        // steps at full precision is pure waste.
        var ladder = new List<int>();
        for (var p = target; p > 20; p = p / 2 + 4) ladder.Add(p);
        ladder.Add(20);
        ladder.Reverse();

        foreach (var p in ladder)
        {
            var stepCtx = new MathContextCompat(p);
            x = x.Add(Divide(x, stepCtx), stepCtx).DivideByInt(2, stepCtx);
        }
        var finalCtx = new MathContextCompat(target);
        x = x.Add(Divide(x, finalCtx), finalCtx).DivideByInt(2, finalCtx);
        return x.Round(ctx);
    }

    /// <summary>e^this via range reduction (halve until |x| is small) + Taylor series + repeated squaring.</summary>
    public BigDecimalCompat Exp(MathContextCompat ctx)
    {
        var workCtx = new MathContextCompat(ctx.Precision + 15);
        var absD = Math.Abs(ToDouble());
        var k = absD > 0.1 ? Math.Max(0, (int)Math.Ceiling(Math.Log2(absD / 0.1))) : 0;
        var reduced = k == 0 ? this : Multiply(Pow2(-k), workCtx);
        var term = new BigDecimalCompat(1, 0);
        var sum = new BigDecimalCompat(1, 0);
        for (var n = 1; n < 2000; n++)
        {
            term = term.Multiply(reduced, workCtx).DivideByInt(n, workCtx);
            sum = sum.Add(term, workCtx);
            if (term.UnscaledValue.IsZero || term.DecimalExponent() < sum.DecimalExponent() - workCtx.Precision - 3) break;
        }
        for (var i = 0; i < k; i++) sum = sum.Multiply(sum, workCtx);
        return sum.Round(ctx);
    }

    /// <summary>
    /// ln(this) via Newton's method on Exp. Newton doubles the number of correct digits each step, so
    /// every step but the last is run at only the precision it can actually deliver — the old fixed
    /// full-precision loop paid for ~5 full-precision <see cref="Exp"/> calls when one is enough, and
    /// Ln-inside-ComplexPow is the hottest path in the whole deep renderer.
    /// </summary>
    public BigDecimalCompat Ln(MathContextCompat ctx)
    {
        if (UnscaledValue.Sign <= 0) throw new ArgumentException("Ln of a non-positive BigDecimalCompat");
        var target = ctx.Precision + 15;
        var seed = Math.Log(ToDouble());
        var y = FromDouble(double.IsFinite(seed) ? seed : 0.0);
        var one = new BigDecimalCompat(1, 0);

        // Precision ladder from the double seed's ~15 good digits up to the target, doubling each step.
        var ladder = new List<int>();
        for (var p = target; p > 20; p = p / 2 + 4) ladder.Add(p);
        ladder.Add(20);
        ladder.Reverse();

        foreach (var p in ladder)
        {
            var stepCtx = new MathContextCompat(p);
            var ey = y.Exp(stepCtx);
            y = y.Add(Divide(ey, stepCtx), stepCtx).Subtract(one, stepCtx);
        }
        // One extra full-precision step so the final result is converged, not just half-converged.
        var finalCtx = new MathContextCompat(target);
        y = y.Add(Divide(y.Exp(finalCtx), finalCtx), finalCtx).Subtract(one, finalCtx);
        return y.Round(ctx);
    }

    /// <summary>atan(this) via repeated half-angle reduction (atan(v) = 2*atan(v/(1+sqrt(1+v^2)))) down to
    /// a small argument, then a Taylor series, then undoing the reduction by doubling back up.</summary>
    public static BigDecimalCompat Atan(BigDecimalCompat x, MathContextCompat ctx)
    {
        var workCtx = new MathContextCompat(ctx.Precision + 15);
        var neg = x.UnscaledValue.Sign < 0;
        var v = neg ? x.Negate() : x;
        var one = new BigDecimalCompat(1, 0);
        var halvings = 0;
        while (v.ToDouble() > 0.01 && halvings < 400)
        {
            var s = one.Add(v.Multiply(v, workCtx), workCtx).Sqrt(workCtx);
            v = v.Divide(one.Add(s, workCtx), workCtx);
            halvings++;
        }
        var t2 = v.Multiply(v, workCtx);
        var power = v;
        var sum = v;
        var sign = -1;
        for (var n = 1; n < 2000; n++)
        {
            power = power.Multiply(t2, workCtx);
            var addend = power.DivideByInt(2L * n + 1, workCtx);
            if (sign < 0) addend = addend.Negate();
            sum = sum.Add(addend, workCtx);
            sign = -sign;
            if (addend.UnscaledValue.IsZero || addend.DecimalExponent() < sum.DecimalExponent() - workCtx.Precision - 3) break;
        }
        var result = sum.Multiply(Pow2(halvings), workCtx);
        return (neg ? result.Negate() : result).Round(ctx);
    }

    /// <summary>Standard 4-quadrant atan2(y, x) built from <see cref="Atan"/> and <see cref="Pi"/>.</summary>
    public static BigDecimalCompat Atan2(BigDecimalCompat y, BigDecimalCompat x, MathContextCompat ctx)
    {
        var workCtx = new MathContextCompat(ctx.Precision + 15);
        if (x.UnscaledValue.Sign > 0)
            return Atan(y.Divide(x, workCtx), workCtx).Round(ctx);
        if (x.UnscaledValue.Sign < 0)
        {
            var a = Atan(y.Divide(x, workCtx), workCtx);
            var pi = Pi(workCtx);
            return (y.UnscaledValue.Sign >= 0 ? a.Add(pi, workCtx) : a.Subtract(pi, workCtx)).Round(ctx);
        }
        var half = Pi(workCtx).Divide(new BigDecimalCompat(2, 0), workCtx);
        return (y.UnscaledValue.Sign >= 0 ? half : half.Negate()).Round(ctx);
    }

    /// <summary>Simultaneous sin/cos via Taylor series on a halved argument, recombined with the
    /// double-angle formulas — shares the reduction so both come from one series evaluation.</summary>
    public static (BigDecimalCompat Sin, BigDecimalCompat Cos) SinCos(BigDecimalCompat x, MathContextCompat ctx)
    {
        var workCtx = new MathContextCompat(ctx.Precision + 15);
        var absD = Math.Abs(x.ToDouble());
        var k = absD > 0.1 ? Math.Max(0, (int)Math.Ceiling(Math.Log2(absD / 0.1))) : 0;
        var reduced = k == 0 ? x : x.Multiply(Pow2(-k), workCtx);
        var t2 = reduced.Multiply(reduced, workCtx);
        var sinTerm = reduced;
        var sin = reduced;
        var cosTerm = new BigDecimalCompat(1, 0);
        var cos = cosTerm;
        for (var n = 1; n < 2000; n++)
        {
            sinTerm = sinTerm.Multiply(t2, workCtx).DivideByInt((2L * n) * (2 * n + 1), workCtx).Negate();
            cosTerm = cosTerm.Multiply(t2, workCtx).DivideByInt((2L * n - 1) * (2 * n), workCtx).Negate();
            sin = sin.Add(sinTerm, workCtx);
            cos = cos.Add(cosTerm, workCtx);
            if ((sinTerm.UnscaledValue.IsZero || sinTerm.DecimalExponent() < sin.DecimalExponent() - workCtx.Precision - 3) &&
                (cosTerm.UnscaledValue.IsZero || cosTerm.DecimalExponent() < cos.DecimalExponent() - workCtx.Precision - 3))
                break;
        }
        for (var i = 0; i < k; i++)
        {
            var newSin = new BigDecimalCompat(2, 0).Multiply(sin, workCtx).Multiply(cos, workCtx);
            var newCos = cos.Multiply(cos, workCtx).Subtract(sin.Multiply(sin, workCtx), workCtx);
            sin = newSin;
            cos = newCos;
        }
        return (sin.Round(ctx), cos.Round(ctx));
    }

    static readonly object PiLock = new();
    static BigDecimalCompat? _piCache;
    static int _piCachePrecision;

    /// <summary>Cached pi via Machin's formula (16*atan(1/5) - 4*atan(1/239)); recomputes only when a
    /// caller needs more digits than the cache currently holds.</summary>
    public static BigDecimalCompat Pi(MathContextCompat ctx)
    {
        lock (PiLock)
            if (_piCache is { } cached && _piCachePrecision >= ctx.Precision) return cached.Round(ctx);
        var workCtx = new MathContextCompat(ctx.Precision + 20);
        var one = new BigDecimalCompat(1, 0);
        var oneFifth = new BigDecimalCompat(2, 1);
        var oneOver239 = one.Divide(new BigDecimalCompat(239, 0), workCtx);
        var pi = new BigDecimalCompat(16, 0).Multiply(Atan(oneFifth, workCtx), workCtx)
            .Subtract(new BigDecimalCompat(4, 0).Multiply(Atan(oneOver239, workCtx), workCtx), workCtx);
        lock (PiLock)
        {
            if (_piCachePrecision < workCtx.Precision) { _piCache = pi; _piCachePrecision = workCtx.Precision; }
            return pi.Round(ctx);
        }
    }

    private BigDecimalCompat Round(MathContextCompat ctx)
    {
        if (ctx.Precision <= 0 || UnscaledValue.IsZero) return this;
        var digits = BigInteger.Abs(UnscaledValue).ToString(CultureInfo.InvariantCulture).Length;
        var drop = digits - ctx.Precision;
        if (drop <= 0) return this;
        var d = BigInteger.Pow(10, drop);
        var (q, rm) = BigInteger.DivRem(UnscaledValue, d);
        if (BigInteger.Abs(rm) * 2 >= d) q += UnscaledValue.Sign >= 0 ? BigInteger.One : BigInteger.MinusOne;
        return new BigDecimalCompat(q, Scale - drop);
    }

    private void Align(BigDecimalCompat o, out BigInteger l, out BigInteger r, out int sc)
    {
        sc = Math.Max(Scale, o.Scale);
        l = UnscaledValue * BigInteger.Pow(10, sc - Scale);
        r = o.UnscaledValue * BigInteger.Pow(10, sc - o.Scale);
    }
}
