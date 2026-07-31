using FractalAnimator.Core.Kernel;

namespace FractalAnimator.Core.Atoms;

public readonly struct PowerJuliaAtom<T> : IFractalAtom<T> where T : struct, IPrecision<T>
{
    public const int PayloadPowerReal = 0;
    public const int PayloadPowerImag = 1;
    public const int PayloadJuliaReal = 2;
    public const int PayloadJuliaImag = 3;
    public const int PayloadLength = 4;

    public const int AuxPow = 0;
    public const int AuxDeriv = 1;
    public const int AuxDeriv2Half = 2;

    public static int AuxCount => 3;

    public static double BailoutSquared => 1024.0;

    public static double DigitsPerIteration => 0.204;

    public static double TruncationCoefficient => 2.3 / (1.0 - 1.31 * MaxCoupledU);

    const double CoupledArithmeticUlpCount = 8.0;

    static double MaxCoupledU => 1.0e-8;

    static double CoupledThresholdSquared => 1.0e-16;

    public static double DerivativeSupremum(ReferenceOrbit<T> orbit, double innerRadius)
    {
        if (!(innerRadius > 0.0)) return double.PositiveInfinity;
        var powerReal = orbit.Payload[PayloadPowerReal];
        var powerImag = orbit.Payload[PayloadPowerImag];
        var modulus = Math.Sqrt(powerReal * powerReal + powerImag * powerImag);
        var exponent = powerReal - 1.0;
        if (exponent > 0.0) return double.PositiveInfinity;
        var argumentFactor = powerImag == 0.0 ? 1.0 : Math.Exp(Math.Abs(powerImag) * Math.PI);
        return modulus * Math.Pow(innerRadius, exponent) * argumentFactor;
    }

    public static void Step(ReferenceOrbit<T> orbit, int n, ref Cx<T> delta, ref int scaleExp, out bool decoupled, double relativeError, out bool branchUndecidable)
    {
        var z = orbit.Z[n];
        var powerReal = orbit.Payload[PayloadPowerReal];
        var powerImag = orbit.Payload[PayloadPowerImag];
        branchUndecidable = false;

        var offset = Cx<T>.ScaleB(delta, scaleExp);
        var zMagSq = orbit.MagnitudeSquared[n];
        var uMagSq = zMagSq > 0 ? offset.MagnitudeSquared / zMagSq : 0.0;
        decoupled = uMagSq >= CoupledThresholdSquared;

        if (!decoupled)
        {
            var d1 = orbit.Aux[AuxDeriv][n];
            var linear = d1 * delta;

            var d2 = orbit.Aux[AuxDeriv2Half][n];
            var deltaMagSq = delta.MagnitudeSquared;
            var linearMagSq = d1.MagnitudeSquared * deltaMagSq;
            var scale = Math.ScaleB(1.0, scaleExp);
            var quadMagSq = scale * scale * d2.MagnitudeSquared * deltaMagSq * deltaMagSq;
            var negligible = T.RelativeUlp * T.RelativeUlp;
            delta = quadMagSq > negligible * linearMagSq
                ? linear + Cx<T>.ScaleB(d2 * delta * delta, scaleExp)
                : linear;
            return;
        }

        var u = offset / z;
        var uSq = T.Add(T.Mul(u.Re, u.Re), T.Mul(u.Im, u.Im));
        var logDiffRe = T.MulByDouble(T.LogP1(T.Add(T.MulByDouble(u.Re, 2.0), uSq)), 0.5);
        var logDiffIm = T.Atan2(u.Im, T.Add(T.One, u.Re));
        var theta = T.Add(T.Atan2(z.Im, z.Re), logDiffIm);

        var thetaValue = theta.ToDouble();
        var uMagnitude = Math.Sqrt(uMagSq);
        var thetaUncertainty = CoupledArithmeticUlpCount * T.RelativeUlp
                               + orbit.TableRelativeUlp
                               + relativeError * uMagnitude / Math.Max(1.0 - uMagnitude, 1e-30);
        if (thetaUncertainty < 1.0 && Math.Abs(Math.Abs(thetaValue) - Math.PI) <= thetaUncertainty)
            branchUndecidable = true;

        if (T.Greater(theta, T.Pi) || !T.Greater(theta, T.Neg(T.Pi)))
        {
            delta = Cx<T>.Pow(z + offset, powerReal, powerImag) - orbit.Aux[AuxPow][n];
        }
        else
        {
            var wRe = T.Sub(T.MulByDouble(logDiffRe, powerReal), T.MulByDouble(logDiffIm, powerImag));
            var wIm = T.Add(T.MulByDouble(logDiffIm, powerReal), T.MulByDouble(logDiffRe, powerImag));
            var (sinHalf, _) = T.SinCos(T.MulByDouble(wIm, 0.5));
            var (sinW, cosW) = T.SinCos(wIm);
            var em1 = new Cx<T>(
                T.Sub(T.Mul(T.ExpM1(wRe), cosW), T.MulByDouble(T.Mul(sinHalf, sinHalf), 2.0)),
                T.Mul(T.Exp(wRe), sinW));
            delta = orbit.Aux[AuxPow][n] * em1;
        }
        scaleExp = 0;
    }

    public static Cx<T> Apply(Cx<T> z)
    {
        throw new NotSupportedException(
            "PowerJuliaAtom.Apply needs the map's payload; the kernel reaches the map through Step, and " +
            "the baker owns direct application. If a future kernel path needs this, thread the payload in.");
    }
}
