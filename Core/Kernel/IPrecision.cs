namespace FractalAnimator.Core.Kernel;

public interface IPrecision<T> where T : struct, IPrecision<T>
{
    static abstract T FromDouble(double value);
    static abstract T FromDoubleDouble(double hi, double lo);
    static abstract T Zero { get; }
    static abstract T One { get; }

    static abstract T Add(T a, T b);
    static abstract T Sub(T a, T b);
    static abstract T Mul(T a, T b);
    static abstract T MulByDouble(T a, double b);
    static abstract T Div(T a, T b);
    static abstract T Neg(T a);
    static abstract T ScaleB(T a, int n);
    static abstract T Sqrt(T a);
    static abstract T Abs(T a);

    static abstract T Exp(T a);
    static abstract T ExpM1(T a);
    static abstract T Log(T a);
    static abstract T LogP1(T a);
    static abstract (T Sin, T Cos) SinCos(T a);
    static abstract T Atan2(T y, T x);
    static abstract T Pi { get; }

    double ToDouble();
    double HighPart { get; }
    static abstract bool Greater(T a, T b);

    static abstract double RelativeUlp { get; }
    static abstract string RungName { get; }
    static abstract int RungIndex { get; }
}

public readonly struct Cx<T> where T : struct, IPrecision<T>
{
    public readonly T Re;
    public readonly T Im;

    public Cx(T re, T im) { Re = re; Im = im; }
    public static Cx<T> FromDouble(double re, double im) => new(T.FromDouble(re), T.FromDouble(im));
    public static Cx<T> Zero => new(T.Zero, T.Zero);

    public static Cx<T> operator +(Cx<T> a, Cx<T> b) => new(T.Add(a.Re, b.Re), T.Add(a.Im, b.Im));
    public static Cx<T> operator -(Cx<T> a, Cx<T> b) => new(T.Sub(a.Re, b.Re), T.Sub(a.Im, b.Im));

    public static Cx<T> operator *(Cx<T> a, Cx<T> b) => new(
        T.Sub(T.Mul(a.Re, b.Re), T.Mul(a.Im, b.Im)),
        T.Add(T.Mul(a.Re, b.Im), T.Mul(a.Im, b.Re)));

    public static Cx<T> operator /(Cx<T> a, Cx<T> b)
    {
        var d = T.Add(T.Mul(b.Re, b.Re), T.Mul(b.Im, b.Im));
        return new Cx<T>(
            T.Div(T.Add(T.Mul(a.Re, b.Re), T.Mul(a.Im, b.Im)), d),
            T.Div(T.Sub(T.Mul(a.Im, b.Re), T.Mul(a.Re, b.Im)), d));
    }

    public static Cx<T> ScaleB(Cx<T> a, int n) => new(T.ScaleB(a.Re, n), T.ScaleB(a.Im, n));

    public double MagnitudeSquared
    {
        get { var r = Re.HighPart; var i = Im.HighPart; return r * r + i * i; }
    }

    public static Cx<T> Pow(Cx<T> z, double powerReal, double powerImag)
    {
        if (z.MagnitudeSquared == 0.0) return Zero;
        var logR = T.MulByDouble(T.Log(T.Add(T.Mul(z.Re, z.Re), T.Mul(z.Im, z.Im))), 0.5);
        var theta = T.Atan2(z.Im, z.Re);
        var re = T.Sub(T.MulByDouble(logR, powerReal), T.MulByDouble(theta, powerImag));
        var im = T.Add(T.MulByDouble(theta, powerReal), T.MulByDouble(logR, powerImag));
        var mag = T.Exp(re);
        var (sin, cos) = T.SinCos(im);
        return new Cx<T>(T.Mul(mag, cos), T.Mul(mag, sin));
    }
}
