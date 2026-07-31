using System.Globalization;
using System.Numerics;
using System.Text;
using FractalAnimator.Core;
using FractalAnimator.Core.Kernel;

namespace FractalAnimator;

public enum ReadinessScope
{
    FusionGate,
    Full,
}

public sealed class FusionReport
{
    public required string Width { get; init; }
    public required int Samples { get; init; }
    public required int InexactProducts { get; init; }
    public required int NonZeroResiduals { get; init; }
    public required int ExactResiduals { get; init; }

    public double NonZeroFraction => InexactProducts == 0 ? 0.0 : (double)NonZeroResiduals / InexactProducts;
    public double ExactFraction => InexactProducts == 0 ? 0.0 : (double)ExactResiduals / InexactProducts;
    public bool Passed => InexactProducts > 0 && NonZeroResiduals == InexactProducts && ExactResiduals == InexactProducts;
}

public sealed class ReconstructionReport
{
    public required string Rung { get; init; }
    public required string Transform { get; init; }
    public required int Samples { get; init; }
    public required int ExactReconstructions { get; init; }
    public required double WorstRelativeError { get; init; }

    public bool Passed => ExactReconstructions == Samples;
}

public sealed class OperationError
{
    public required string Operation { get; init; }
    public required int Samples { get; init; }
    public required double WorstRelativeError { get; init; }
    public required string WorstInput { get; init; }
}

public sealed class RungUlpReport
{
    public required string Rung { get; init; }
    public required double ClaimedRelativeUlp { get; init; }
    public required int ReferenceDigits { get; init; }
    public required OperationError[] Operations { get; init; }

    public OperationError Worst => Operations.OrderByDescending(o => o.WorstRelativeError).First();
    public double MeasuredRelativeUlp => Worst.WorstRelativeError;
    public double ClaimOverrun => ClaimedRelativeUlp <= 0.0 ? double.PositiveInfinity : MeasuredRelativeUlp / ClaimedRelativeUlp;
    public bool Sound => MeasuredRelativeUlp <= ClaimedRelativeUlp;
}

public sealed class ReadinessReport
{
    public required ReadinessScope Scope { get; init; }
    public required int Seed { get; init; }
    public required FusionReport DoubleFusion { get; init; }
    public required FusionReport SingleFusion { get; init; }
    public required ReconstructionReport[] Reconstruction { get; init; }
    public required RungUlpReport[] Rungs { get; init; }
    public required double ElapsedSeconds { get; init; }

    public bool FusionPassed => DoubleFusion.Passed && SingleFusion.Passed;
    public bool ReconstructionPassed => Reconstruction.All(r => r.Passed);
    public bool UlpClaimsSound => Rungs.All(r => r.Sound);
    public bool Passed => FusionPassed && ReconstructionPassed && UlpClaimsSound;
}

public static class KernelReadiness
{
    public const int DefaultSeed = 20260730;
    public const int DefaultFusionSamples = 16384;
    public const int DefaultReconstructionSamples = 16384;
    public const int DefaultUlpSamples = 1024;

    public const int ExactDigits = 512;
    public const int JudgeDigits = 60;
    public const int RatioDigits = 25;
    public const int ReferenceHeadroomDigits = 30;
    public const int CancellationHeadroomDigits = 40;

    const int MantissaExponentLow = -30;
    const int MantissaExponentHigh = 30;
    const int SingleExponentLow = -20;
    const int SingleExponentHigh = 20;
    const int AngleOperandExponentLow = -20;
    const int AngleOperandExponentHigh = 20;
    const int LimbTailDepth = 3;
    const int LimbBits = 53;
    const int NearUnityFirstDecade = 1;
    const int NearUnityDecades = 4;

    static readonly MathContextCompat ExactCtx = new(ExactDigits);
    static readonly MathContextCompat JudgeCtx = new(JudgeDigits);
    static readonly BigDecimalCompat BigOne = new(BigInteger.One, 0);

    public static readonly string[] UnverifiableOnCpu =
    [
        "PTX/SPIR-V emission of fma.rn: only a real device compile can prove the backend keeps the fused form instead of lowering to mul+add.",
        "FTZ / denormal flushing: GPUs may flush subnormal inputs and results to zero, which silently zeroes every TwoProd residual near the bottom of the exponent range.",
        "Contraction and reassociation of the hand-written TwoSum expressions: a backend that contracts s-a or reassociates (a-(s-bb))+(b-bb) destroys the error-free transform without touching FusedMultiplyAdd at all.",
        "ILGPU frontend support for static abstract interface members: the whole ladder is monomorphised through IPrecision<T>, and the kernel will not compile at all if the frontend cannot resolve T.Add / T.RelativeUlp.",
        "ILGPU frontend support for ValueTuple returns: SinCos and every TwoSum/TwoProd helper returns a tuple.",
        "ILGPU frontend support for stackalloc and Span<double>: the limb decomposition path uses them.",
        "Per-lane divergence cost of the ladder scheduler: correctness is unaffected but the FP32-retires-everything measurement does not transfer to a warp-scheduled backend.",
        "Transcendental accuracy of the device math library: MathF.Exp / Math.Log / Math.Atan2 are host CRT calls whose device replacements have different, usually worse, worst-case error, so every measured ulp in the table below must be re-measured on the device.",
    ];

    public static ReadinessReport Run(
        ReadinessScope scope = ReadinessScope.Full,
        int seed = DefaultSeed,
        int fusionSamples = DefaultFusionSamples,
        int reconstructionSamples = DefaultReconstructionSamples,
        int ulpSamples = DefaultUlpSamples)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var doubleFusion = MeasureDoubleFusion(fusionSamples, seed);
        var singleFusion = MeasureSingleFusion(fusionSamples, seed + 1);

        var reconstruction = new List<ReconstructionReport>
        {
            MeasureReconstruction<FloatFloat>("TwoProd", reconstructionSamples, seed + 2, true, true, ExactOf),
            MeasureReconstruction<FloatFloat>("TwoSum", reconstructionSamples, seed + 3, false, true, ExactOf),
            MeasureReconstruction<DoubleDouble>("TwoProd", reconstructionSamples, seed + 4, true, false, ExactOf),
            MeasureReconstruction<DoubleDouble>("TwoSum", reconstructionSamples, seed + 5, false, false, ExactOf),
            MeasureReconstruction<QuadDouble>("TwoProd", reconstructionSamples, seed + 6, true, false, ExactOf),
            MeasureReconstruction<QuadDouble>("TwoSum", reconstructionSamples, seed + 7, false, false, ExactOf),
        };

        var rungs = new List<RungUlpReport>();
        if (scope == ReadinessScope.Full)
        {
            rungs.Add(Measure<Fp32>(ExactOf, ulpSamples, seed + 11));
            rungs.Add(Measure<FloatFloat>(ExactOf, ulpSamples, seed + 12));
            rungs.Add(Measure<Fp64>(ExactOf, ulpSamples, seed + 13));
            rungs.Add(Measure<DoubleDouble>(ExactOf, ulpSamples, seed + 14));
            rungs.Add(Measure<QuadDouble>(ExactOf, ulpSamples, seed + 15));
        }

        clock.Stop();
        return new ReadinessReport
        {
            Scope = scope,
            Seed = seed,
            DoubleFusion = doubleFusion,
            SingleFusion = singleFusion,
            Reconstruction = reconstruction.ToArray(),
            Rungs = rungs.ToArray(),
            ElapsedSeconds = clock.Elapsed.TotalSeconds,
        };
    }

    public static bool TryGate(out string report, int seed = DefaultSeed)
    {
        var result = Run(ReadinessScope.FusionGate, seed);
        report = Describe(result);
        return result.FusionPassed && result.ReconstructionPassed;
    }

    public static BigDecimalCompat ExactOf(Fp32 value) => BigDecimalCompat.FromDoubleExact(value.Value);

    public static BigDecimalCompat ExactOf(Fp64 value) => BigDecimalCompat.FromDoubleExact(value.Value);

    public static BigDecimalCompat ExactOf(FloatFloat value) =>
        BigDecimalCompat.FromDoubleExact(value.Hi).Add(BigDecimalCompat.FromDoubleExact(value.Lo), ExactCtx);

    public static BigDecimalCompat ExactOf(DoubleDouble value) =>
        BigDecimalCompat.FromDoubleExact(value.Hi).Add(BigDecimalCompat.FromDoubleExact(value.Lo), ExactCtx);

    public static BigDecimalCompat ExactOf(QuadDouble value) =>
        BigDecimalCompat.FromDoubleExact(value.X0)
            .Add(BigDecimalCompat.FromDoubleExact(value.X1), ExactCtx)
            .Add(BigDecimalCompat.FromDoubleExact(value.X2), ExactCtx)
            .Add(BigDecimalCompat.FromDoubleExact(value.X3), ExactCtx);

    static FusionReport MeasureDoubleFusion(int samples, int seed)
    {
        var rng = new Random(seed);
        var left = Normals(rng, samples, MantissaExponentLow, MantissaExponentHigh);
        var right = Normals(rng, samples, MantissaExponentLow, MantissaExponentHigh);

        var inexact = new bool[samples];
        var nonZero = new bool[samples];
        var reconstructs = new bool[samples];

        Parallel.For(0, samples, i =>
        {
            var a = left[i];
            var b = right[i];
            var product = a * b;
            var residual = Math.FusedMultiplyAdd(a, b, -product);

            var exactProduct = BigDecimalCompat.FromDoubleExact(a).Multiply(BigDecimalCompat.FromDoubleExact(b), ExactCtx);
            var trueResidual = exactProduct.Subtract(BigDecimalCompat.FromDoubleExact(product), ExactCtx);
            if (trueResidual.UnscaledValue.IsZero) return;

            inexact[i] = true;
            nonZero[i] = residual != 0.0;
            reconstructs[i] = BigDecimalCompat.FromDoubleExact(residual).CompareTo(trueResidual) == 0;
        });

        return new FusionReport
        {
            Width = "double",
            Samples = samples,
            InexactProducts = inexact.Count(f => f),
            NonZeroResiduals = nonZero.Count(f => f),
            ExactResiduals = reconstructs.Count(f => f),
        };
    }

    static FusionReport MeasureSingleFusion(int samples, int seed)
    {
        var rng = new Random(seed);
        var left = Singles(rng, samples, SingleExponentLow, SingleExponentHigh);
        var right = Singles(rng, samples, SingleExponentLow, SingleExponentHigh);

        var inexact = new bool[samples];
        var nonZero = new bool[samples];
        var reconstructs = new bool[samples];

        Parallel.For(0, samples, i =>
        {
            var a = (float)left[i];
            var b = (float)right[i];
            var product = a * b;
            var residual = MathF.FusedMultiplyAdd(a, b, -product);

            var exactProduct = BigDecimalCompat.FromDoubleExact(a).Multiply(BigDecimalCompat.FromDoubleExact(b), ExactCtx);
            var trueResidual = exactProduct.Subtract(BigDecimalCompat.FromDoubleExact(product), ExactCtx);
            if (trueResidual.UnscaledValue.IsZero) return;

            inexact[i] = true;
            nonZero[i] = residual != 0.0f;
            reconstructs[i] = BigDecimalCompat.FromDoubleExact(residual).CompareTo(trueResidual) == 0;
        });

        return new FusionReport
        {
            Width = "float",
            Samples = samples,
            InexactProducts = inexact.Count(f => f),
            NonZeroResiduals = nonZero.Count(f => f),
            ExactResiduals = reconstructs.Count(f => f),
        };
    }

    static ReconstructionReport MeasureReconstruction<T>(
        string transform, int samples, int seed, bool multiply, bool singleWidth, Func<T, BigDecimalCompat> toExact)
        where T : struct, IPrecision<T>
    {
        var rng = new Random(seed);
        var left = singleWidth
            ? Singles(rng, samples, SingleExponentLow, SingleExponentHigh)
            : Normals(rng, samples, MantissaExponentLow, MantissaExponentHigh);
        var right = singleWidth
            ? Singles(rng, samples, SingleExponentLow, SingleExponentHigh)
            : Normals(rng, samples, MantissaExponentLow, MantissaExponentHigh);

        var exact = new bool[samples];
        var errors = new double[samples];

        Parallel.For(0, samples, i =>
        {
            var a = left[i];
            var b = right[i];
            var lifted = T.FromDouble(a);
            var other = T.FromDouble(b);
            var produced = multiply ? T.Mul(lifted, other) : T.Add(lifted, other);

            var wideA = BigDecimalCompat.FromDoubleExact(a);
            var wideB = BigDecimalCompat.FromDoubleExact(b);
            var reference = multiply ? wideA.Multiply(wideB, ExactCtx) : wideA.Add(wideB, ExactCtx);
            var measured = toExact(produced);

            if (measured.CompareTo(reference) == 0) { exact[i] = true; return; }
            errors[i] = RelativeMagnitude(measured, reference, reference, JudgeCtx);
        });

        var worst = 0.0;
        foreach (var e in errors) if (e > worst) worst = e;

        return new ReconstructionReport
        {
            Rung = T.RungName,
            Transform = transform,
            Samples = samples,
            ExactReconstructions = exact.Count(f => f),
            WorstRelativeError = worst,
        };
    }

    static RungUlpReport Measure<T>(Func<T, BigDecimalCompat> toExact, int samples, int seed)
        where T : struct, IPrecision<T>
    {
        var digits = (int)Math.Ceiling(-Math.Log10(T.RelativeUlp)) + ReferenceHeadroomDigits;
        var ctx = new MathContextCompat(digits);
        var wide = new MathContextCompat(digits + CancellationHeadroomDigits);
        var rng = new Random(seed);
        var operations = new List<OperationError>();

        double Judge(BigDecimalCompat measured, BigDecimalCompat reference, BigDecimalCompat denominator) =>
            RelativeMagnitude(measured, reference, denominator, wide);

        OperationError Sweep(string name, Func<int, double> body, Func<int, string> describe)
        {
            var errors = new double[samples];
            Parallel.For(0, samples, i => errors[i] = body(i));
            var worst = 0.0;
            var worstIndex = 0;
            for (var i = 0; i < samples; i++) if (errors[i] > worst) { worst = errors[i]; worstIndex = i; }
            return new OperationError
            {
                Operation = name,
                Samples = samples,
                WorstRelativeError = worst,
                WorstInput = describe(worstIndex),
            };
        }

        string Show(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

        var addA = Normals(rng, samples, MantissaExponentLow, MantissaExponentHigh);
        var addATail = Tails(rng, samples);
        var addB = Normals(rng, samples, MantissaExponentLow, MantissaExponentHigh);
        var addBTail = Tails(rng, samples);
        operations.Add(Sweep("Add", i =>
        {
            var a = Widen<T>(addA[i], addATail, i);
            var b = Widen<T>(addB[i], addBTail, i);
            var reference = toExact(a).Add(toExact(b), wide);
            return Judge(toExact(T.Add(a, b)), reference, reference);
        }, i => $"a={Show(addA[i])} b={Show(addB[i])}"));

        var subA = Normals(rng, samples, MantissaExponentLow, MantissaExponentHigh);
        var subATail = Tails(rng, samples);
        var subB = new double[samples];
        for (var i = 0; i < samples; i++)
            subB[i] = (i & 1) == 0
                ? NextNormal(rng, MantissaExponentLow, MantissaExponentHigh)
                : subA[i] * (1.0 + (rng.NextDouble() - 0.5) * 2.0e-3);
        var subBTail = Tails(rng, samples);
        operations.Add(Sweep("Sub", i =>
        {
            var a = Widen<T>(subA[i], subATail, i);
            var b = Widen<T>(subB[i], subBTail, i);
            var reference = toExact(a).Subtract(toExact(b), wide);
            return Judge(toExact(T.Sub(a, b)), reference, reference);
        }, i => $"a={Show(subA[i])} b={Show(subB[i])}"));

        var mulA = Normals(rng, samples, MantissaExponentLow, MantissaExponentHigh);
        var mulATail = Tails(rng, samples);
        var mulB = Normals(rng, samples, MantissaExponentLow, MantissaExponentHigh);
        var mulBTail = Tails(rng, samples);
        operations.Add(Sweep("Mul", i =>
        {
            var a = Widen<T>(mulA[i], mulATail, i);
            var b = Widen<T>(mulB[i], mulBTail, i);
            var reference = toExact(a).Multiply(toExact(b), wide);
            return Judge(toExact(T.Mul(a, b)), reference, reference);
        }, i => $"a={Show(mulA[i])} b={Show(mulB[i])}"));

        var divA = Normals(rng, samples, MantissaExponentLow, MantissaExponentHigh);
        var divATail = Tails(rng, samples);
        var divB = Normals(rng, samples, MantissaExponentLow, MantissaExponentHigh);
        var divBTail = Tails(rng, samples);
        operations.Add(Sweep("Div", i =>
        {
            var a = Widen<T>(divA[i], divATail, i);
            var b = Widen<T>(divB[i], divBTail, i);
            var reference = toExact(a).Divide(toExact(b), wide);
            return Judge(toExact(T.Div(a, b)), reference, reference);
        }, i => $"a={Show(divA[i])} b={Show(divB[i])}"));

        var scaleA = Normals(rng, samples, MantissaExponentLow, MantissaExponentHigh);
        var scaleATail = Tails(rng, samples);
        var scaleB = Normals(rng, samples, MantissaExponentLow, MantissaExponentHigh);
        operations.Add(Sweep("MulByDouble", i =>
        {
            var a = Widen<T>(scaleA[i], scaleATail, i);
            var reference = toExact(a).Multiply(BigDecimalCompat.FromDoubleExact(scaleB[i]), wide);
            return Judge(toExact(T.MulByDouble(a, scaleB[i])), reference, reference);
        }, i => $"a={Show(scaleA[i])} b={Show(scaleB[i])}"));

        var sqrtA = Normals(rng, samples, MantissaExponentLow, MantissaExponentHigh);
        var sqrtATail = Tails(rng, samples);
        operations.Add(Sweep("Sqrt", i =>
        {
            var a = T.Abs(Widen<T>(sqrtA[i], sqrtATail, i));
            var reference = toExact(a).Sqrt(wide);
            return Judge(toExact(T.Sqrt(a)), reference, reference);
        }, i => $"a={Show(Math.Abs(sqrtA[i]))}"));

        var expA = Uniform(rng, samples, -10.0, 10.0);
        var expATail = Tails(rng, samples);
        operations.Add(Sweep("Exp", i =>
        {
            var a = Widen<T>(expA[i], expATail, i);
            var reference = toExact(a).Exp(wide);
            return Judge(toExact(T.Exp(a)), reference, reference);
        }, i => $"a={Show(expA[i])}"));

        var expm1A = SignedLogUniform(rng, samples, 1.0e-8, 0.5);
        var expm1ATail = Tails(rng, samples);
        operations.Add(Sweep("ExpM1", i =>
        {
            var a = Widen<T>(expm1A[i], expm1ATail, i);
            var reference = toExact(a).Exp(wide).Subtract(BigOne, wide);
            return Judge(toExact(T.ExpM1(a)), reference, reference);
        }, i => $"a={Show(expm1A[i])}"));

        var logA = LogUniformAwayFromOne(rng, samples, 1.0e-6, 1.0e6);
        var logATail = Tails(rng, samples);
        operations.Add(Sweep("Log", i =>
        {
            var a = Widen<T>(logA[i], logATail, i);
            var reference = toExact(a).Ln(wide);
            return Judge(toExact(T.Log(a)), reference, reference);
        }, i => $"a={Show(logA[i])}"));

        var nearOneA = NearUnity(samples);
        var nearOneTail = Tails(rng, samples);
        operations.Add(Sweep("LogNearOne", i =>
        {
            var a = Widen<T>(nearOneA[i], nearOneTail, i);
            var reference = toExact(a).Ln(wide);
            return Judge(toExact(T.Log(a)), reference, reference);
        }, i => $"a={Show(nearOneA[i])}"));

        var logp1A = SignedLogUniform(rng, samples, 1.0e-8, 0.5);
        var logp1ATail = Tails(rng, samples);
        operations.Add(Sweep("LogP1", i =>
        {
            var a = Widen<T>(logp1A[i], logp1ATail, i);
            var reference = BigOne.Add(toExact(a), wide).Ln(wide);
            return Judge(toExact(T.LogP1(a)), reference, reference);
        }, i => $"a={Show(logp1A[i])}"));

        var angleA = Uniform(rng, samples, -6.5, 6.5);
        var angleATail = Tails(rng, samples);
        operations.Add(Sweep("SinCos", i =>
        {
            var a = Widen<T>(angleA[i], angleATail, i);
            var (sin, cos) = BigDecimalCompat.SinCos(toExact(a), wide);
            var (measuredSin, measuredCos) = T.SinCos(a);
            var sinError = Judge(toExact(measuredSin), sin, BigOne);
            var cosError = Judge(toExact(measuredCos), cos, BigOne);
            return Math.Max(sinError, cosError);
        }, i => $"a={Show(angleA[i])}"));

        var atanY = Normals(rng, samples, AngleOperandExponentLow, AngleOperandExponentHigh);
        var atanYTail = Tails(rng, samples);
        var atanX = Normals(rng, samples, AngleOperandExponentLow, AngleOperandExponentHigh);
        var atanXTail = Tails(rng, samples);
        operations.Add(Sweep("Atan2", i =>
        {
            var y = Widen<T>(atanY[i], atanYTail, i);
            var x = Widen<T>(atanX[i], atanXTail, i);
            var reference = BigDecimalCompat.Atan2(toExact(y), toExact(x), wide);
            var denominator = reference.Abs().CompareTo(BigOne) > 0 ? reference.Abs() : BigOne;
            return Judge(toExact(T.Atan2(y, x)), reference, denominator);
        }, i => $"y={Show(atanY[i])} x={Show(atanX[i])}"));

        return new RungUlpReport
        {
            Rung = T.RungName,
            ClaimedRelativeUlp = T.RelativeUlp,
            ReferenceDigits = digits,
            Operations = operations.ToArray(),
        };
    }

    static double RelativeMagnitude(
        BigDecimalCompat measured, BigDecimalCompat reference, BigDecimalCompat denominator, MathContextCompat ctx)
    {
        if (denominator.UnscaledValue.IsZero) return 0.0;
        var difference = measured.Subtract(reference, ctx).Abs();
        if (difference.UnscaledValue.IsZero) return 0.0;
        return difference.Divide(denominator.Abs(), new MathContextCompat(RatioDigits)).ToDouble();
    }

    static double NextNormal(Random rng, int lowExponent, int highExponent)
    {
        var mantissa = (rng.NextInt64() & 0xFFFFFFFFFFFFFL) | (1L << 52);
        var exponent = rng.Next(lowExponent, highExponent + 1);
        var magnitude = Math.ScaleB((double)mantissa, exponent - 52);
        return rng.Next(2) == 0 ? -magnitude : magnitude;
    }

    static double NextSingle(Random rng, int lowExponent, int highExponent)
    {
        var mantissa = (rng.Next() & 0x7FFFFF) | (1 << 23);
        var exponent = rng.Next(lowExponent, highExponent + 1);
        var magnitude = MathF.ScaleB(mantissa, exponent - 23);
        return rng.Next(2) == 0 ? -magnitude : magnitude;
    }

    static double[][] Tails(Random rng, int count)
    {
        var tails = new double[LimbTailDepth][];
        for (var k = 0; k < LimbTailDepth; k++)
        {
            tails[k] = new double[count];
            for (var i = 0; i < count; i++) tails[k][i] = rng.NextDouble() * 2.0 - 1.0;
        }
        return tails;
    }

    static T Widen<T>(double baseValue, double[][] tails, int index) where T : struct, IPrecision<T>
    {
        var value = T.FromDouble(baseValue);
        for (var k = 0; k < tails.Length; k++)
            value = T.Add(value, T.FromDouble(baseValue * Math.ScaleB(tails[k][index], -LimbBits * (k + 1))));
        return value;
    }

    static double[] Normals(Random rng, int count, int lowExponent, int highExponent)
    {
        var values = new double[count];
        for (var i = 0; i < count; i++) values[i] = NextNormal(rng, lowExponent, highExponent);
        return values;
    }

    static double[] Singles(Random rng, int count, int lowExponent, int highExponent)
    {
        var values = new double[count];
        for (var i = 0; i < count; i++) values[i] = NextSingle(rng, lowExponent, highExponent);
        return values;
    }

    static double[] NearUnity(int count)
    {
        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            var decade = Math.Pow(10.0, -(NearUnityFirstDecade + i / 2 % NearUnityDecades));
            values[i] = (i & 1) == 0 ? 1.0 + decade : 1.0 - decade;
        }
        return values;
    }

    static double[] Uniform(Random rng, int count, double low, double high)
    {
        var values = new double[count];
        for (var i = 0; i < count; i++) values[i] = low + rng.NextDouble() * (high - low);
        return values;
    }

    static double[] SignedLogUniform(Random rng, int count, double low, double high)
    {
        var lowLog = Math.Log(low);
        var span = Math.Log(high) - lowLog;
        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            var magnitude = Math.Exp(lowLog + rng.NextDouble() * span);
            values[i] = rng.Next(2) == 0 ? -magnitude : magnitude;
        }
        return values;
    }

    static double[] LogUniformAwayFromOne(Random rng, int count, double low, double high)
    {
        var lowLog = Math.Log(low);
        var span = Math.Log(high) - lowLog;
        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            double candidate;
            do candidate = Math.Exp(lowLog + rng.NextDouble() * span);
            while (Math.Abs(candidate - 1.0) < 0.1);
            values[i] = candidate;
        }
        return values;
    }

    public static string Describe(ReadinessReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("KERNEL READINESS (CPU prerequisite gate for any GPU port)");
        text.AppendLine(Invariant($"scope={report.Scope}  seed={report.Seed}  elapsed={report.ElapsedSeconds:0.00}s  verdict={(report.Passed ? "PASS" : "FAIL")}"));
        text.AppendLine();

        text.AppendLine("1. FUSED MULTIPLY-ADD IS ACTUALLY FUSED");
        text.AppendLine("   residual = fma(a, b, -(a*b)) over random normals whose exact product is not representable");
        foreach (var fusion in new[] { report.DoubleFusion, report.SingleFusion })
            text.AppendLine(Invariant(
                $"   {fusion.Width,-7} inexact={fusion.InexactProducts,6}/{fusion.Samples,-6} nonzero={fusion.NonZeroFraction * 100.0,8:0.0000}%  bit-exact={fusion.ExactFraction * 100.0,8:0.0000}%  {(fusion.Passed ? "PASS" : "FAIL - MULTIPLY-ADD IS NOT FUSED, EVERY ERROR TERM IS ZERO")}"));
        text.AppendLine();

        text.AppendLine("2. ERROR-FREE TRANSFORMATION RECONSTRUCTION");
        text.AppendLine(Invariant($"   hi+lo of a single-limb operand pair vs the exact value, judged at {ExactDigits} digits (ratios at {JudgeDigits})"));
        foreach (var row in report.Reconstruction)
            text.AppendLine(Invariant(
                $"   {row.Rung,-5} {row.Transform,-8} exact={row.ExactReconstructions,6}/{row.Samples,-6} worst={Scientific(row.WorstRelativeError)}  {(row.Passed ? "PASS" : "FAIL - THE RUNG HAS SILENTLY DEGRADED TO ITS HIGH LIMB")}"));
        text.AppendLine();

        if (report.Rungs.Length > 0)
        {
            text.AppendLine("3. DECLARED RelativeUlp VS MEASURED WORST-CASE RELATIVE ERROR");
            text.AppendLine("   rung   claimed      measured     overrun    worst operation   verdict");
            foreach (var rung in report.Rungs)
                text.AppendLine(Invariant(
                    $"   {rung.Rung,-6} {Scientific(rung.ClaimedRelativeUlp)}    {Scientific(rung.MeasuredRelativeUlp)}    {rung.ClaimOverrun,7:0.00}x    {rung.Worst.Operation,-16}  {(rung.Sound ? "sound" : "UNSOUND")}"));
            text.AppendLine();

            foreach (var rung in report.Rungs)
            {
                text.AppendLine(Invariant($"   {rung.Rung} per-operation worst relative error ({rung.Operations[0].Samples} samples, {rung.ReferenceDigits}-digit reference):"));
                foreach (var op in rung.Operations.OrderByDescending(o => o.WorstRelativeError))
                    text.AppendLine(Invariant($"      {op.Operation,-14} {Scientific(op.WorstRelativeError)}  {op.WorstRelativeError / rung.ClaimedRelativeUlp,8:0.00} x claimed   at {op.WorstInput}"));
                text.AppendLine();
            }

            var unsound = report.Rungs.Where(r => !r.Sound).ToArray();
            if (unsound.Length > 0)
            {
                text.AppendLine("   ############################################################");
                text.AppendLine("   ##  CERTIFICATE SOUNDNESS DEFECT - CLAIMED ULP IS TIGHTER  ##");
                text.AppendLine("   ##  THAN MEASURED. EVERY PIXEL RETIRED ON THESE RUNGS IS   ##");
                text.AppendLine("   ##  CERTIFIED AGAINST A BOUND THE ARITHMETIC DOES NOT MEET ##");
                text.AppendLine("   ############################################################");
                foreach (var rung in unsound)
                    text.AppendLine(Invariant(
                        $"   {rung.Rung}: claims {Scientific(rung.ClaimedRelativeUlp)}, delivers {Scientific(rung.MeasuredRelativeUlp)} on {rung.Worst.Operation} - the claim is {rung.ClaimOverrun:0.00}x too tight."));
                text.AppendLine();
            }
        }

        text.AppendLine("4. GPU PRECONDITIONS THIS HARNESS CANNOT CHECK ON THE HOST");
        foreach (var item in UnverifiableOnCpu) text.AppendLine("   - " + item);

        return text.ToString();
    }

    static string Scientific(double value) => value.ToString("0.000e+00", CultureInfo.InvariantCulture);

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
