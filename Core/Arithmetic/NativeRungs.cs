using System.Runtime.CompilerServices;

namespace FractalAnimator.Core.Kernel;

public readonly struct Fp32 : IPrecision<Fp32>
{
    public readonly float Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fp32(float value) => Value = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 FromDouble(double value) => new((float)value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 FromDoubleDouble(double hi, double lo) => new((float)(hi + lo));

    public static Fp32 Zero { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new(0.0f); }
    public static Fp32 One { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new(1.0f); }
    public static Fp32 Pi { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new(MathF.PI); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 Add(Fp32 a, Fp32 b) => new(a.Value + b.Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 Sub(Fp32 a, Fp32 b) => new(a.Value - b.Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 Mul(Fp32 a, Fp32 b) => new(a.Value * b.Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 MulByDouble(Fp32 a, double b) => new((float)(a.Value * b));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 Div(Fp32 a, Fp32 b) => new(a.Value / b.Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 Neg(Fp32 a) => new(-a.Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 ScaleB(Fp32 a, int n) => new(MathF.ScaleB(a.Value, n));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 Sqrt(Fp32 a) => new(MathF.Sqrt(a.Value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 Abs(Fp32 a) => new(MathF.Abs(a.Value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 Exp(Fp32 a) => new(MathF.Exp(a.Value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 ExpM1(Fp32 a) => new((float)NativeTranscendental.ExpM1(a.Value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 Log(Fp32 a) => new(MathF.Log(a.Value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 LogP1(Fp32 a) => new((float)NativeTranscendental.LogP1(a.Value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (Fp32 Sin, Fp32 Cos) SinCos(Fp32 a)
    {
        var (sin, cos) = MathF.SinCos(a.Value);
        return (new Fp32(sin), new Fp32(cos));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp32 Atan2(Fp32 y, Fp32 x) => new(MathF.Atan2(y.Value, x.Value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToDouble() => Value;

    public double HighPart { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Value; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Greater(Fp32 a, Fp32 b) => a.Value > b.Value;

    public static double RelativeUlp => 6e-8;
    public static string RungName => "FP32";
    public static int RungIndex => 0;

    public override string ToString() => Value.ToString("R");
}

public readonly struct Fp64 : IPrecision<Fp64>
{
    public readonly double Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fp64(double value) => Value = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 FromDouble(double value) => new(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 FromDoubleDouble(double hi, double lo) => new(hi + lo);

    public static Fp64 Zero { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new(0.0); }
    public static Fp64 One { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new(1.0); }
    public static Fp64 Pi { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new(Math.PI); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 Add(Fp64 a, Fp64 b) => new(a.Value + b.Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 Sub(Fp64 a, Fp64 b) => new(a.Value - b.Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 Mul(Fp64 a, Fp64 b) => new(a.Value * b.Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 MulByDouble(Fp64 a, double b) => new(a.Value * b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 Div(Fp64 a, Fp64 b) => new(a.Value / b.Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 Neg(Fp64 a) => new(-a.Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 ScaleB(Fp64 a, int n) => new(Math.ScaleB(a.Value, n));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 Sqrt(Fp64 a) => new(Math.Sqrt(a.Value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 Abs(Fp64 a) => new(Math.Abs(a.Value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 Exp(Fp64 a) => new(Math.Exp(a.Value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 ExpM1(Fp64 a) => new(NativeTranscendental.ExpM1(a.Value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 Log(Fp64 a) => new(Math.Log(a.Value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 LogP1(Fp64 a) => new(NativeTranscendental.LogP1(a.Value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (Fp64 Sin, Fp64 Cos) SinCos(Fp64 a)
    {
        var (sin, cos) = Math.SinCos(a.Value);
        return (new Fp64(sin), new Fp64(cos));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fp64 Atan2(Fp64 y, Fp64 x) => new(Math.Atan2(y.Value, x.Value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToDouble() => Value;

    public double HighPart { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Value; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Greater(Fp64 a, Fp64 b) => a.Value > b.Value;

    public static double RelativeUlp => 1.1e-16;
    public static string RungName => "FP64";

    public static int RungIndex => 2;

    public override string ToString() => Value.ToString("R");
}

file static class NativeTranscendental
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double ExpM1(double x)
    {
        var u = Math.Exp(x);
        if (u == 1.0) return x;
        if (u - 1.0 == -1.0) return -1.0;
        if (double.IsInfinity(u)) return u;
        return (u - 1.0) * x / Math.Log(u);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double LogP1(double x)
    {
        var u = 1.0 + x;
        if (u == 1.0) return x;
        if (double.IsInfinity(u)) return Math.Log(u);
        return Math.Log(u) * x / (u - 1.0);
    }
}
