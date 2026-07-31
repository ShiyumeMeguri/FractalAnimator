using FractalAnimator.Core.Kernel;

namespace FractalAnimator.Core.Atoms;

public static class PowerJuliaBaker
{
    const int Limbs = 4;

    public static BigDecimalCompat JuliaFromRaw(double raw) => BigDecimalCompat.FromDouble(raw * 1e-5);

    public sealed class BakedOrbit
    {
        public required double[][] ZReal { get; init; }
        public required double[][] ZImag { get; init; }
        public required double[][][] AuxReal { get; init; }
        public required double[][][] AuxImag { get; init; }
        public required double[] MagnitudeSquared { get; init; }
        public required double[] ExpansionMagnitude { get; init; }
        public required int Length { get; init; }
        public required double[] Payload { get; init; }
        public required int Precision { get; init; }
        public required BigDecimalCompat CenterReal { get; init; }
        public required BigDecimalCompat CenterImag { get; init; }
        public double LeafOffset { get; init; } = 0.59;
        public required BigDecimalCompat JuliaReal { get; init; }
        public required BigDecimalCompat JuliaImag { get; init; }
    }

    public static BakedOrbit Bake(
        BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        double juliaReal, double juliaImag, double powerReal, double powerImag,
        int iterations, CancellationToken token = default)
        => Bake(centerReal, centerImag,
                BigDecimalCompat.FromDouble(juliaReal), BigDecimalCompat.FromDouble(juliaImag),
                powerReal, powerImag, iterations, token);

    public static BakedOrbit Bake(
        BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        BigDecimalCompat juliaReal, BigDecimalCompat juliaImag, double powerReal, double powerImag,
        int iterations, CancellationToken token = default)
    {
        var precision = OrbitPrecision.For(iterations * 2, PowerJuliaAtom<DoubleDouble>.DigitsPerIteration);
        var ctx = new MathContextCompat(precision);
        var kRe = juliaReal;
        var kIm = juliaImag;

        var orbitSteps = iterations * 2;
        var length = orbitSteps + 1;
        var zReal = NewLimbTable(length);
        var zImag = NewLimbTable(length);
        var auxReal = new double[PowerJuliaAtom<DoubleDouble>.AuxCount][][];
        var auxImag = new double[PowerJuliaAtom<DoubleDouble>.AuxCount][][];
        for (var a = 0; a < auxReal.Length; a++) { auxReal[a] = NewLimbTable(length); auxImag[a] = NewLimbTable(length); }
        var magnitudeSquared = new double[length];
        var expansion = new double[length];

        var zr = centerReal;
        var zi = centerImag;
        WriteLimbs(zReal, zImag, 0, zr, zi);
        magnitudeSquared[0] = MagSq(zr, zi);

        for (var n = 0; n < orbitSteps; n++)
        {
            token.ThrowIfCancellationRequested();

            var (d1Re, d1Im) = ScaledPow(zr, zi, powerReal - 1, powerImag, powerReal, powerImag, ctx);
            var (d2Re, d2Im) = ScaledPow2(zr, zi, powerReal, powerImag, ctx);
            WriteLimbs(auxReal[PowerJuliaAtom<DoubleDouble>.AuxDeriv], auxImag[PowerJuliaAtom<DoubleDouble>.AuxDeriv], n, d1Re, d1Im);
            WriteLimbs(auxReal[PowerJuliaAtom<DoubleDouble>.AuxDeriv2Half], auxImag[PowerJuliaAtom<DoubleDouble>.AuxDeriv2Half], n, d2Re, d2Im);
            expansion[n] = Math.Sqrt(MagSq(d1Re, d1Im));

            var (powRe, powIm) = DeepOrbitTrapEngine.ComplexPow(zr, zi, powerReal, powerImag, ctx);
            WriteLimbs(auxReal[PowerJuliaAtom<DoubleDouble>.AuxPow], auxImag[PowerJuliaAtom<DoubleDouble>.AuxPow], n, powRe, powIm);

            zr = powRe.Add(kRe, ctx);
            zi = powIm.Add(kIm, ctx);
            WriteLimbs(zReal, zImag, n + 1, zr, zi);
            magnitudeSquared[n + 1] = MagSq(zr, zi);

        }
        expansion[orbitSteps] = expansion[Math.Max(0, orbitSteps - 1)];

        return new BakedOrbit
        {
            ZReal = zReal, ZImag = zImag,
            AuxReal = auxReal, AuxImag = auxImag,
            MagnitudeSquared = magnitudeSquared,
            ExpansionMagnitude = expansion,
            Length = length,
            Precision = precision,
            Payload = [powerReal, powerImag, kRe.ToDouble(), kIm.ToDouble()],
            CenterReal = centerReal,
            CenterImag = centerImag,
            JuliaReal = kRe,
            JuliaImag = kIm,
        };
    }

    public static ReferenceOrbit<T> Narrow<T>(BakedOrbit baked) where T : struct, IPrecision<T>
    {
        var z = new Cx<T>[baked.Length];
        for (var n = 0; n < baked.Length; n++)
            z[n] = new Cx<T>(Sum<T>(baked.ZReal, n), Sum<T>(baked.ZImag, n));

        var aux = new Cx<T>[baked.AuxReal.Length][];
        for (var a = 0; a < aux.Length; a++)
        {
            aux[a] = new Cx<T>[baked.Length];
            for (var n = 0; n < baked.Length; n++)
                aux[a][n] = new Cx<T>(Sum<T>(baked.AuxReal[a], n), Sum<T>(baked.AuxImag[a], n));
        }

        return new ReferenceOrbit<T>
        {
            Z = z,
            Aux = aux,
            MagnitudeSquared = baked.MagnitudeSquared,
            ExpansionMagnitude = baked.ExpansionMagnitude,
            Length = baked.Length,
            Payload = baked.Payload,
            TableRelativeUlp = T.RelativeUlp,
            Nearest = NearestPointIndex.Build(baked.ZReal[0], baked.ZImag[0], baked.Length),
        };
    }

    static T Sum<T>(double[][] limbs, int n) where T : struct, IPrecision<T>
    {
        var value = T.Zero;
        for (var i = limbs.Length - 1; i >= 0; i--)
            value = T.Add(value, T.FromDouble(limbs[i][n]));
        return value;
    }

    static double[][] NewLimbTable(int length)
    {
        var table = new double[Limbs][];
        for (var i = 0; i < Limbs; i++) table[i] = new double[length];
        return table;
    }

    static void WriteLimbs(double[][] real, double[][] imag, int n, BigDecimalCompat re, BigDecimalCompat im)
    {
        Span<double> buffer = stackalloc double[Limbs];
        re.ToLimbs(buffer);
        for (var i = 0; i < Limbs; i++) real[i][n] = buffer[i];
        im.ToLimbs(buffer);
        for (var i = 0; i < Limbs; i++) imag[i][n] = buffer[i];
    }

    static double MagSq(BigDecimalCompat re, BigDecimalCompat im)
    {
        var r = re.ToDouble();
        var i = im.ToDouble();
        return r * r + i * i;
    }

    static (BigDecimalCompat, BigDecimalCompat) ScaledPow(
        BigDecimalCompat zr, BigDecimalCompat zi, double expRe, double expIm,
        double powerReal, double powerImag, MathContextCompat ctx)
    {
        var (re, im) = DeepOrbitTrapEngine.ComplexPow(zr, zi, expRe, expIm, ctx);
        return ComplexScale(re, im, powerReal, powerImag, ctx);
    }

    static (BigDecimalCompat, BigDecimalCompat) ScaledPow2(
        BigDecimalCompat zr, BigDecimalCompat zi, double powerReal, double powerImag, MathContextCompat ctx)
    {
        var (re, im) = DeepOrbitTrapEngine.ComplexPow(zr, zi, powerReal - 2, powerImag, ctx);
        var (sr, si) = ComplexScale(re, im, powerReal, powerImag, ctx);
        var (tr, ti) = ComplexScale(sr, si, powerReal - 1, powerImag, ctx);
        var half = BigDecimalCompat.FromDoubleExact(0.5);
        return (tr.Multiply(half, ctx), ti.Multiply(half, ctx));
    }

    static (BigDecimalCompat, BigDecimalCompat) ComplexScale(
        BigDecimalCompat re, BigDecimalCompat im, double sr, double si, MathContextCompat ctx)
    {
        var a = BigDecimalCompat.FromDoubleExact(sr);
        var b = BigDecimalCompat.FromDoubleExact(si);
        return (re.Multiply(a, ctx).Subtract(im.Multiply(b, ctx), ctx),
                re.Multiply(b, ctx).Add(im.Multiply(a, ctx), ctx));
    }
}
