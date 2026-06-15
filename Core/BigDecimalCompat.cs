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
