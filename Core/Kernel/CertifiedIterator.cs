namespace FractalAnimator.Core.Kernel;

public enum PixelVerdict
{
    Escaped,
    Interior,
    NeedsHigherPrecision,
}

public readonly struct PixelResult
{
    public required PixelVerdict Verdict { get; init; }
    public required int Iterations { get; init; }
    public required ColorState Color { get; init; }
    public required int DecoupledSteps { get; init; }
    public required int Rebases { get; init; }
}

public static class CertifiedIterator<T, TFractal, TColor>
    where T : struct, IPrecision<T>
    where TFractal : IFractalAtom<T>
    where TColor : IColoringAtom
{
    const double RebaseGainThreshold = 0.5;
    const double RenormThreshold = 1.0e15;
    const double RelativeErrorCap = 0.1;
    const double CapInflation = 1.0 / (1.0 - RelativeErrorCap);
    const double DoubleNarrowingUlp = 1.1102230246251565e-16;
    const double CoupledArithmeticUlps = 2.0;
    const double TableUlps = 1.0;

    public static readonly long[] EscalationReasons = new long[10];
    public static readonly long[] CapBuckets = new long[5];
    public static long CapDecoupledSum;

    public static PixelResult Iterate(
        ReferenceOrbit<T> orbit, Cx<T> deltaC, int scaleExp, int maxIterations)
    {
        var delta = deltaC;
        var color = ColorState.Fresh(1e5);

        var relativeError = 0.0;
        var referenceIndex = 0;
        var decoupledSteps = 0;
        var rebases = 0;

        var limit = maxIterations - 1;
        var rungUlp = T.RelativeUlp;
        var tableUlp = orbit.TableRelativeUlp;

        for (var iteration = 0; iteration < limit; iteration++)
        {
            if (referenceIndex + 1 >= orbit.Length)
            {
                Interlocked.Increment(ref EscalationReasons[6]);
                return Escalate(iteration, color, decoupledSteps, rebases);
            }

            var previousOffset = Cx<T>.ScaleB(delta, scaleExp);
            var previousOffsetMagnitude = Math.Sqrt(previousOffset.MagnitudeSquared);
            var deltaLeading = Math.Max(Math.Abs(delta.Re.HighPart), Math.Abs(delta.Im.HighPart));
            if (deltaLeading > 0.0 && previousOffsetMagnitude <= 0.0)
                return Escalate(iteration, color, decoupledSteps, rebases);
            var sourceMagnitude = Math.Sqrt(orbit.MagnitudeSquared[referenceIndex]);
            var sourcePoint = orbit.Z[referenceIndex] + previousOffset;
            var sourcePointMagnitude = Math.Sqrt(sourcePoint.MagnitudeSquared);
            var previousAbsoluteError = relativeError * previousOffsetMagnitude * CapInflation;

            TFractal.Step(orbit, referenceIndex, ref delta, ref scaleExp, out var decoupled, relativeError, out var branchUndecidable);
            if (branchUndecidable) return Escalate(iteration, color, decoupledSteps, rebases);
            referenceIndex++;
            if (decoupled) decoupledSteps++;

            var offset = Cx<T>.ScaleB(delta, scaleExp);
            var offsetMagnitude = Math.Sqrt(offset.MagnitudeSquared);
            var point = orbit.Z[referenceIndex] + offset;
            var re = point.Re.ToDouble();
            var im = point.Im.ToDouble();
            var magnitudeSquared = re * re + im * im;
            var pointMagnitude = Math.Sqrt(magnitudeSquared);
            var referenceMagnitude = Math.Sqrt(orbit.MagnitudeSquared[referenceIndex]);

            if (decoupled)
            {
                var ballRadius = previousAbsoluteError;
                var innerRadius = sourcePointMagnitude - ballRadius;
                if (!(innerRadius > 0.5 * sourcePointMagnitude))
                {
                    Interlocked.Increment(ref EscalationReasons[0]);
                    return Escalate(iteration, color, decoupledSteps, rebases);
                }

                var supDerivative = TFractal.DerivativeSupremum(orbit, innerRadius);
                if (!double.IsFinite(supDerivative))
                {
                    Interlocked.Increment(ref EscalationReasons[1]);
                    return Escalate(iteration, color, decoupledSteps, rebases);
                }

                var denominator = offsetMagnitude - supDerivative * ballRadius;
                if (!(denominator > 0.0))
                {
                    Interlocked.Increment(ref EscalationReasons[2]);
                    return Escalate(iteration, color, decoupledSteps, rebases);
                }

                var amplification = supDerivative * previousOffsetMagnitude / denominator;
                relativeError = (relativeError + TableUlps * tableUlp + CoupledArithmeticUlps * rungUlp) * amplification
                                + CoupledArithmeticUlps * rungUlp;
            }
            else
            {
                var u = sourceMagnitude > 0
                    ? (previousOffsetMagnitude + previousAbsoluteError) / sourceMagnitude
                    : 0.0;
                var truncation = TFractal.TruncationCoefficient * u * u;
                relativeError = relativeError * (1.0 + 5.0 * u)
                                + CoupledArithmeticUlps * rungUlp
                                + TableUlps * tableUlp
                                + truncation;
            }

            if (!(relativeError <= RelativeErrorCap))
            {
                Interlocked.Increment(ref EscalationReasons[3]);
                if (double.IsFinite(relativeError))
                {
                    var bucket = relativeError < 1.0 ? 0 : relativeError < 1e3 ? 1 : relativeError < 1e9 ? 2 : 3;
                    Interlocked.Increment(ref CapBuckets[bucket]);
                    Interlocked.Add(ref CapDecoupledSum, decoupledSteps);
                }
                else Interlocked.Increment(ref CapBuckets[4]);
                return Escalate(iteration, color, decoupledSteps, rebases);
            }

            var displayError = relativeError * offsetMagnitude * CapInflation
                               + TableUlps * tableUlp * referenceMagnitude
                               + CoupledArithmeticUlps * rungUlp * pointMagnitude
                               + DoubleNarrowingUlp * pointMagnitude;

            color.FinalMagnitudeSquared = magnitudeSquared;
            color.FinalPointError = displayError;

            var bailout = TFractal.BailoutSquared;
            var escapeUncertainty = 2.0 * pointMagnitude * displayError + displayError * displayError;
            var escapeMargin = Math.Abs(magnitudeSquared - bailout);
            if (!(escapeMargin > escapeUncertainty))
            {
                Interlocked.Increment(ref EscalationReasons[4]);
                return Escalate(iteration, color, decoupledSteps, rebases);
            }

            if (magnitudeSquared > bailout)
            {
                var escaped = new PixelResult
                {
                    Verdict = PixelVerdict.Escaped,
                    Iterations = iteration + 1,
                    Color = color,
                    DecoupledSteps = decoupledSteps,
                    Rebases = rebases,
                };
                if (TColor.IsCertified(color, iteration + 1)) return escaped;
                Interlocked.Increment(ref EscalationReasons[5]);
                return Escalate(iteration, color, decoupledSteps, rebases);
            }

            TColor.Accumulate(ref color, re, im, displayError);

            if (KernelOptions.RebaseEnabled && decoupled && offsetMagnitude > 0.0)
            {
                var candidate = orbit.Nearest.Query(re, im);
                if (candidate >= 0 && candidate + 1 < orbit.Length)
                {
                    var candidatePoint = orbit.Z[candidate];
                    var dr = re - candidatePoint.Re.ToDouble();
                    var di = im - candidatePoint.Im.ToDouble();
                    var candidateDistance = Math.Sqrt(dr * dr + di * di);
                    if (candidateDistance > 0.0 && candidateDistance < RebaseGainThreshold * offsetMagnitude)
                    {
                        var rebasedError = relativeError * (offsetMagnitude / candidateDistance)
                                           + (2.0 * TableUlps * tableUlp + CoupledArithmeticUlps * rungUlp)
                                             * pointMagnitude / candidateDistance;
                        if (rebasedError <= RelativeErrorCap)
                        {
                            delta = point - candidatePoint;
                            scaleExp = 0;
                            referenceIndex = candidate;
                            rebases++;
                            relativeError = rebasedError;
                        }
                    }
                }
            }

            var leading = Math.Max(Math.Abs(delta.Re.HighPart), Math.Abs(delta.Im.HighPart));
            if (leading > RenormThreshold || (leading > 0.0 && leading < 1.0e-15))
            {
                var k = Math.ILogB(leading);
                delta = Cx<T>.ScaleB(delta, -k);
                scaleExp += k;
            }
        }

        var interior = new PixelResult
        {
            Verdict = PixelVerdict.Interior,
            Iterations = maxIterations,
            Color = color,
            DecoupledSteps = decoupledSteps,
            Rebases = rebases,
        };
        return TColor.IsCertified(color, maxIterations)
            ? interior
            : Escalate(limit, color, decoupledSteps, rebases);
    }

    static PixelResult Escalate(int iteration, ColorState color, int decoupled, int rebases) => new()
    {
        Verdict = PixelVerdict.NeedsHigherPrecision,
        Iterations = iteration,
        Color = color,
        DecoupledSteps = decoupled,
        Rebases = rebases,
    };
}
