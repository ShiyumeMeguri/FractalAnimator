using System.Diagnostics;
using System.Globalization;
using System.Text;
using FractalAnimator.Core;
using FractalAnimator.Core.Atoms;
using FractalAnimator.Core.Kernel;

namespace FractalAnimator;

public static class KernelProfile
{
    public const double RebaseGainThreshold = 0.5;
    public const double RenormThreshold = 1.0e15;
    public const double RelativeErrorCap = 0.1;
    public const double CapInflation = 1.0 / (1.0 - RelativeErrorCap);
    public const double DoubleNarrowingUlp = 1.1102230246251565e-16;
    public const double CoupledArithmeticUlps = 2.0;
    public const double TableUlps = 1.0;
    public const double TrapErrorStandIn = 1.0e-13;

    public static double Sink;

    public interface IShape
    {
        static abstract bool Certificate { get; }
        static abstract bool Reconstruct { get; }
        static abstract bool IntervalTrap { get; }
        static abstract bool RecordRebases { get; }
        static abstract bool ReplayRebases { get; }
    }

    public readonly struct ShapeCalibrate : IShape
    {
        public static bool Certificate => true;
        public static bool Reconstruct => true;
        public static bool IntervalTrap => true;
        public static bool RecordRebases => true;
        public static bool ReplayRebases => false;
    }

    public readonly struct ShapeFull : IShape
    {
        public static bool Certificate => true;
        public static bool Reconstruct => true;
        public static bool IntervalTrap => true;
        public static bool RecordRebases => false;
        public static bool ReplayRebases => false;
    }

    public readonly struct ShapeTrivialTrap : IShape
    {
        public static bool Certificate => true;
        public static bool Reconstruct => true;
        public static bool IntervalTrap => false;
        public static bool RecordRebases => false;
        public static bool ReplayRebases => false;
    }

    public readonly struct ShapeNoCertificate : IShape
    {
        public static bool Certificate => false;
        public static bool Reconstruct => true;
        public static bool IntervalTrap => true;
        public static bool RecordRebases => false;
        public static bool ReplayRebases => false;
    }

    public readonly struct ShapeBareIteration : IShape
    {
        public static bool Certificate => false;
        public static bool Reconstruct => true;
        public static bool IntervalTrap => false;
        public static bool RecordRebases => false;
        public static bool ReplayRebases => false;
    }

    public readonly struct ShapeDeltaOnly : IShape
    {
        public static bool Certificate => false;
        public static bool Reconstruct => false;
        public static bool IntervalTrap => false;
        public static bool RecordRebases => false;
        public static bool ReplayRebases => true;
    }

    public sealed class RebaseTrack<T> where T : struct, IPrecision<T>
    {
        public int[] Step = new int[4];
        public int[] Reference = new int[4];
        public Cx<T>[] Delta = new Cx<T>[4];
        public int[] ScaleExp = new int[4];
        public int Count;

        public void Add(int step, int reference, Cx<T> delta, int scaleExp)
        {
            if (Count == Step.Length)
            {
                var grown = Count * 2;
                Array.Resize(ref Step, grown);
                Array.Resize(ref Reference, grown);
                Array.Resize(ref Delta, grown);
                Array.Resize(ref ScaleExp, grown);
            }
            Step[Count] = step;
            Reference[Count] = reference;
            Delta[Count] = delta;
            ScaleExp[Count] = scaleExp;
            Count++;
        }
    }

    public sealed class RungBreakdown
    {
        public required string Rung { get; init; }
        public required int Pixels { get; init; }
        public required int Retired { get; init; }
        public required long Steps { get; init; }
        public required long DecoupledSteps { get; init; }
        public required long Rebases { get; init; }
        public required int VerdictMismatches { get; init; }
        public required double FullMs { get; init; }
        public required double TrivialTrapMs { get; init; }
        public required double NoCertificateMs { get; init; }
        public required double BareIterationMs { get; init; }
        public required double DeltaOnlyMs { get; init; }
    }

    public sealed class ZoomBreakdown
    {
        public required double Zooms { get; init; }
        public required int Size { get; init; }
        public required int Iterations { get; init; }
        public required int Repeats { get; init; }
        public required List<RungBreakdown> Rungs { get; init; }
        public required int Unprofiled { get; init; }
        public required double ShippingParallelMs { get; init; }
        public required int[] ShippingRetiredPerRung { get; init; }
        public required int ShippingUncertified { get; init; }
    }

    public static int Run(string[] args)
    {
        var size = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 96;
        var iterations = args.Length > 2 && int.TryParse(args[2], out var it) ? it : 300;
        var repeats = args.Length > 3 && int.TryParse(args[3], out var rp) ? rp : 3;
        Console.WriteLine(Report(new[] { 120.0, 300.0, 0.0 }, size, iterations, repeats));
        return 0;
    }

    public static string Report(double[] zoomLadder, int size, int iterations, int repeats)
    {
        ArgumentNullException.ThrowIfNull(zoomLadder);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(repeats, 1);

        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        var centerReal = BigDecimalCompat.Parse(FractalStressHarness.DeepCenterReal);
        var centerImag = BigDecimalCompat.Parse(FractalStressHarness.DeepCenterImag);
        var p = SelfTest.CorruptionParams();
        var juliaReal = p.JuliaCXRaw * 1e-5;
        var juliaImag = p.JuliaCYRaw * 1e-5;

        var bakeWatch = Stopwatch.StartNew();
        var baked = PowerJuliaBaker.Bake(centerReal, centerImag, juliaReal, juliaImag,
            p.PowerReal, p.PowerImag, iterations);
        bakeWatch.Stop();

        sb.AppendLine("KernelProfile — where one certified pixel evaluation actually spends its time");
        sb.AppendFormat(ci, "  centre re = {0}", FractalStressHarness.DeepCenterReal).AppendLine();
        sb.AppendFormat(ci, "         im = {0}", FractalStressHarness.DeepCenterImag).AppendLine();
        sb.AppendFormat(ci, "  power = {0} + {1}i   julia = {2} + {3}i", p.PowerReal, p.PowerImag, juliaReal, juliaImag)
            .AppendLine();
        sb.AppendFormat(ci, "  bake  = {0:0.0} ms at {1} digits, {2} orbit entries",
            bakeWatch.Elapsed.TotalMilliseconds, baked.Precision, baked.Length).AppendLine();
        sb.AppendFormat(ci, "  machine {0} logical cores  ServerGC={1}  {2}",
            Environment.ProcessorCount, System.Runtime.GCSettings.IsServerGC,
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription).AppendLine();
        sb.AppendLine("  variant timings are SINGLE-THREADED, min of the repeats, over the exact pixel set each");
        sb.AppendLine("  rung receives from the ladder, with every variant forced to the step budget the full");
        sb.AppendLine("  variant actually consumed on that pixel (rounded up, which favours the cheap variants).");
        sb.AppendLine("  variant d replays the rebase schedule recorded from variant a, so all four variants walk");
        sb.AppendLine("  bit-identical orbits and differ only in the work performed per step.");
        sb.AppendLine();

        foreach (var zooms in zoomLadder)
        {
            var breakdown = Measure(baked, zooms, size, iterations, repeats);
            AppendZoom(sb, breakdown);
        }

        sb.AppendLine("VARIANTS");
        sb.AppendLine("  a full        the shipping CertifiedIterator body, reproduced verbatim");
        sb.AppendLine("  b trivialtrap identical, except BStyleTrapAtom.Accumulate is replaced by min plus add");
        sb.AppendLine("  c nocert      identical, except the three-ledger certificate and its escape-uncertainty");
        sb.AppendLine("                test are deleted; reconstruction, plain escape test, trap, rebase, renorm stay");
        sb.AppendLine("  e bare        neither certificate nor interval trap: reconstruction narrowing, plain escape");
        sb.AppendLine("                test, rebase, renorm and the delta step, which is the uncertified renderer");
        sb.AppendLine("  d deltaonly   TFractal.Step plus renormalisation plus the replayed rebase schedule, which");
        sb.AppendLine("                is all SIMD over the rung arithmetic could ever vectorise");
        return sb.ToString();
    }

    static void AppendZoom(StringBuilder sb, ZoomBreakdown z)
    {
        var ci = CultureInfo.InvariantCulture;
        const string rule =
            "----------------------------------------------------------------------------------------------------------------";

        sb.AppendFormat(ci, "zoom {0:0}   size {1}x{1}   iterations {2}   repeats {3}", z.Zooms, z.Size, z.Iterations, z.Repeats)
            .AppendLine();
        sb.AppendFormat(ci, "  shipping parallel render: {0:0.0} ms   rungs {1}   uncert {2}",
            z.ShippingParallelMs, string.Join("/", z.ShippingRetiredPerRung), z.ShippingUncertified).AppendLine();
        if (z.Unprofiled > 0)
            sb.AppendFormat(ci, "  {0} pixels fell through to the arbitrary-precision rung and are NOT profiled here",
                z.Unprofiled).AppendLine();

        sb.AppendFormat(ci, "{0,-6} {1,7} {2,7} {3,10} {4,10} {5,7} {6,9} {7,9} {8,9} {9,9} {10,9} {11,7}",
            "rung", "pixels", "retired", "steps", "decoupled", "rebases",
            "a full", "b notrap", "c nocert", "e bare", "d delta", "f=d/a").AppendLine();
        sb.AppendLine(rule);

        double a = 0, b = 0, c = 0, e = 0, d = 0;
        long steps = 0, decoupled = 0, rebases = 0;
        var mismatches = 0;
        foreach (var r in z.Rungs)
        {
            sb.AppendFormat(ci, "{0,-6} {1,7} {2,7} {3,10} {4,10} {5,7} {6,9:0.00} {7,9:0.00} {8,9:0.00} {9,9:0.00} {10,9:0.00} {11,7:0.000}",
                r.Rung, r.Pixels, r.Retired, r.Steps, r.DecoupledSteps, r.Rebases,
                r.FullMs, r.TrivialTrapMs, r.NoCertificateMs, r.BareIterationMs, r.DeltaOnlyMs,
                r.FullMs > 0 ? r.DeltaOnlyMs / r.FullMs : 0.0).AppendLine();
            a += r.FullMs; b += r.TrivialTrapMs; c += r.NoCertificateMs;
            e += r.BareIterationMs; d += r.DeltaOnlyMs;
            steps += r.Steps; decoupled += r.DecoupledSteps; rebases += r.Rebases;
            mismatches += r.VerdictMismatches;
        }

        sb.AppendLine(rule);
        sb.AppendFormat(ci, "{0,-6} {1,7} {2,7} {3,10} {4,10} {5,7} {6,9:0.00} {7,9:0.00} {8,9:0.00} {9,9:0.00} {10,9:0.00} {11,7:0.000}",
            "TOTAL", "", "", steps, decoupled, rebases, a, b, c, e, d, a > 0 ? d / a : 0.0).AppendLine();
        sb.AppendFormat(ci, "  verdict mismatches against the shipping CertifiedIterator: {0}", mismatches).AppendLine();
        sb.AppendLine();

        var trap = a - b;
        var certificate = a - c;
        var reconstruction = e - d;
        var overlap = a - trap - certificate - reconstruction - d;
        sb.AppendFormat(ci, "  {0,-52} {1,10} {2,9}", "bucket", "ms", "% of a").AppendLine();
        Bucket(sb, "interval trap accumulation             a minus b", trap, a);
        Bucket(sb, "certificate ledger and escape bound    a minus c", certificate, a);
        Bucket(sb, "reconstruction, escape test, rebase    e minus d", reconstruction, a);
        Bucket(sb, "delta recurrence and renormalisation   d", d, a);
        Bucket(sb, "instruction-level overlap of the above a-trap-cert-recon-d", overlap, a);
        Bucket(sb, "rung-independent double bookkeeping    a minus d", a - d, a);
        sb.AppendLine();

        if (steps > 0)
            sb.AppendFormat(ci, "  per certified step: a {0:0.0} ns   d {1:0.0} ns   bookkeeping {2:0.0} ns",
                a * 1e6 / steps, d * 1e6 / steps, (a - d) * 1e6 / steps).AppendLine();

        var f = a > 0 ? d / a : 0.0;
        sb.AppendFormat(ci, "  vectorisable fraction f = d/a = {0:0.000}", f).AppendLine();
        sb.AppendFormat(ci, "  Amdahl ceiling with a perfect W-wide SIMD delta step: 1/((1-f)+f/W)").AppendLine();
        foreach (var w in new[] { 4.0, 8.0, 16.0, double.PositiveInfinity })
        {
            var speedup = 1.0 / ((1.0 - f) + (double.IsInfinity(w) ? 0.0 : f / w));
            sb.AppendFormat(ci, "    W = {0,-8} -> {1:0.000}x", double.IsInfinity(w) ? "infinity" : w.ToString("0", ci), speedup)
                .AppendLine();
        }
        sb.AppendLine();
    }

    static void Bucket(StringBuilder sb, string name, double ms, double total)
    {
        var ci = CultureInfo.InvariantCulture;
        sb.AppendFormat(ci, "  {0,-52} {1,10:0.00} {2,8:0.0}%", name, ms, total > 0 ? 100.0 * ms / total : 0.0).AppendLine();
    }

    public static ZoomBreakdown Measure(
        PowerJuliaBaker.BakedOrbit baked, double zooms, int size, int iterations, int repeats)
    {
        ArgumentNullException.ThrowIfNull(baked);

        var spacingLog2 = 2.0 - zooms;
        var scaleExp = (int)Math.Floor(spacingLog2);
        var mantissa = Math.Pow(2.0, spacingLog2 - scaleExp);
        var offsetToDelta = mantissa / size;
        var centerX = size / 2.0;
        var centerY = size / 2.0;

        var reference = new float[size * size * 3];
        var shipping = CertifiedRenderer.Render(baked, zooms, size, size, iterations, reference);

        var pending = new int[size * size];
        for (var i = 0; i < pending.Length; i++) pending[i] = i;
        var pendingCount = pending.Length;

        var rungs = new List<RungBreakdown>();
        pendingCount = ProfileRung<Fp32>(baked, "FP32", pending, pendingCount, size, offsetToDelta,
            centerX, centerY, scaleExp, iterations, repeats, rungs);
        pendingCount = ProfileRung<FloatFloat>(baked, "FF32", pending, pendingCount, size, offsetToDelta,
            centerX, centerY, scaleExp, iterations, repeats, rungs);
        pendingCount = ProfileRung<Fp64>(baked, "FP64", pending, pendingCount, size, offsetToDelta,
            centerX, centerY, scaleExp, iterations, repeats, rungs);
        pendingCount = ProfileRung<DoubleDouble>(baked, "DD", pending, pendingCount, size, offsetToDelta,
            centerX, centerY, scaleExp, iterations, repeats, rungs);
        pendingCount = ProfileRung<QuadDouble>(baked, "QD", pending, pendingCount, size, offsetToDelta,
            centerX, centerY, scaleExp, iterations, repeats, rungs);

        return new ZoomBreakdown
        {
            Zooms = zooms,
            Size = size,
            Iterations = iterations,
            Repeats = repeats,
            Rungs = rungs,
            Unprofiled = pendingCount,
            ShippingParallelMs = shipping.TotalMilliseconds,
            ShippingRetiredPerRung = shipping.RetiredPerRung,
            ShippingUncertified = shipping.Uncertified,
        };
    }

    static int ProfileRung<T>(
        PowerJuliaBaker.BakedOrbit baked, string name, int[] pending, int pendingCount, int size,
        double offsetToDelta, double centerX, double centerY, int scaleExp, int iterations, int repeats,
        List<RungBreakdown> rungs)
        where T : struct, IPrecision<T>
    {
        if (pendingCount == 0) return 0;

        var orbit = PowerJuliaBaker.Narrow<T>(baked);
        var limit = iterations - 1;

        var seeds = new Cx<T>[pendingCount];
        for (var k = 0; k < pendingCount; k++)
        {
            var index = pending[k];
            var x = index % size;
            var y = index / size;
            seeds[k] = Cx<T>.FromDouble((x - centerX) * offsetToDelta, (centerY - y) * offsetToDelta);
        }

        var budgets = new int[pendingCount];
        var survived = new bool[pendingCount];
        var tracks = new RebaseTrack<T>[pendingCount];
        long totalSteps = 0, totalDecoupled = 0, totalRebases = 0;
        var mismatches = 0;
        var sink = 0.0;

        for (var k = 0; k < pendingCount; k++)
        {
            tracks[k] = new RebaseTrack<T>();
            var steps = Pass<T, PowerJuliaAtom<T>, BStyleTrapAtom, ShapeCalibrate>(
                orbit, seeds[k], scaleExp, limit, limit, tracks[k], out var escalated,
                out var decoupled, out var rebases, ref sink);
            budgets[k] = steps;
            survived[k] = escalated;
            totalSteps += steps;
            totalDecoupled += decoupled;
            totalRebases += rebases;

            var truth = CertifiedIterator<T, PowerJuliaAtom<T>, BStyleTrapAtom>
                .Iterate(orbit, seeds[k], scaleExp, iterations);
            if ((truth.Verdict == PixelVerdict.NeedsHigherPrecision) != escalated) mismatches++;
        }

        double Sweep<TShape>() where TShape : struct, IShape
        {
            var local = 0.0;
            var watch = Stopwatch.StartNew();
            for (var k = 0; k < pendingCount; k++)
                Pass<T, PowerJuliaAtom<T>, BStyleTrapAtom, TShape>(
                    orbit, seeds[k], scaleExp, budgets[k], limit, tracks[k],
                    out _, out _, out _, ref local);
            watch.Stop();
            Sink += local;
            return watch.Elapsed.TotalMilliseconds;
        }

        for (var warm = 0; warm < 2; warm++)
        {
            Sweep<ShapeFull>();
            Sweep<ShapeTrivialTrap>();
            Sweep<ShapeNoCertificate>();
            Sweep<ShapeBareIteration>();
            Sweep<ShapeDeltaOnly>();
            Thread.Sleep(120);
        }

        double full = double.MaxValue, trivialTrap = double.MaxValue;
        double noCertificate = double.MaxValue, bare = double.MaxValue, deltaOnly = double.MaxValue;
        for (var r = 0; r < repeats; r++)
        {
            full = Math.Min(full, Sweep<ShapeFull>());
            trivialTrap = Math.Min(trivialTrap, Sweep<ShapeTrivialTrap>());
            noCertificate = Math.Min(noCertificate, Sweep<ShapeNoCertificate>());
            bare = Math.Min(bare, Sweep<ShapeBareIteration>());
            deltaOnly = Math.Min(deltaOnly, Sweep<ShapeDeltaOnly>());
        }
        Sink += sink;

        var remaining = 0;
        for (var k = 0; k < pendingCount; k++)
            if (survived[k]) pending[remaining++] = pending[k];

        rungs.Add(new RungBreakdown
        {
            Rung = name,
            Pixels = pendingCount,
            Retired = pendingCount - remaining,
            Steps = totalSteps,
            DecoupledSteps = totalDecoupled,
            Rebases = totalRebases,
            VerdictMismatches = mismatches,
            FullMs = full,
            TrivialTrapMs = trivialTrap,
            NoCertificateMs = noCertificate,
            BareIterationMs = bare,
            DeltaOnlyMs = deltaOnly,
        });
        return remaining;
    }

    static int Pass<T, TFractal, TColor, TShape>(
        ReferenceOrbit<T> orbit, Cx<T> deltaC, int scaleExp, int budget, int limit, RebaseTrack<T> track,
        out bool escalated, out int decoupledSteps, out int rebases, ref double sink)
        where T : struct, IPrecision<T>
        where TFractal : IFractalAtom<T>
        where TColor : IColoringAtom
        where TShape : struct, IShape
    {
        var delta = deltaC;
        var color = ColorState.Fresh(1e5);

        var relativeError = 0.0;
        var referenceIndex = 0;
        var steps = 0;
        var cursor = 0;
        var ranToEnd = true;
        escalated = false;
        decoupledSteps = 0;
        rebases = 0;
        if (TShape.RecordRebases) track.Count = 0;

        var rungUlp = T.RelativeUlp;
        var tableUlp = orbit.TableRelativeUlp;

        for (var iteration = 0; iteration < budget; iteration++)
        {
            steps = iteration + 1;

            if (referenceIndex + 1 >= orbit.Length) { escalated = true; ranToEnd = false; break; }

            var previousOffsetMagnitude = 0.0;
            var sourceMagnitude = 0.0;
            var sourcePointMagnitude = 0.0;
            var previousAbsoluteError = 0.0;
            if (TShape.Certificate)
            {
                var previousOffset = Cx<T>.ScaleB(delta, scaleExp);
                previousOffsetMagnitude = Math.Sqrt(previousOffset.MagnitudeSquared);
                sourceMagnitude = Math.Sqrt(orbit.MagnitudeSquared[referenceIndex]);
                var sourcePoint = orbit.Z[referenceIndex] + previousOffset;
                sourcePointMagnitude = Math.Sqrt(sourcePoint.MagnitudeSquared);
                previousAbsoluteError = relativeError * previousOffsetMagnitude * CapInflation;
            }

            TFractal.Step(orbit, referenceIndex, ref delta, ref scaleExp, out var decoupled, relativeError, out _);
            referenceIndex++;
            if (decoupled) decoupledSteps++;

            if (TShape.ReplayRebases && cursor < track.Count && track.Step[cursor] == iteration)
            {
                delta = track.Delta[cursor];
                scaleExp = track.ScaleExp[cursor];
                referenceIndex = track.Reference[cursor];
                rebases++;
                cursor++;
            }

            if (TShape.Reconstruct)
            {
                var offset = Cx<T>.ScaleB(delta, scaleExp);
                var offsetMagnitude = Math.Sqrt(offset.MagnitudeSquared);
                var point = orbit.Z[referenceIndex] + offset;
                var re = point.Re.ToDouble();
                var im = point.Im.ToDouble();
                var magnitudeSquared = re * re + im * im;
                var displayError = TrapErrorStandIn;

                if (TShape.Certificate)
                {
                    var pointMagnitude = Math.Sqrt(magnitudeSquared);
                    var referenceMagnitude = Math.Sqrt(orbit.MagnitudeSquared[referenceIndex]);

                    if (decoupled)
                    {
                        var ballRadius = previousAbsoluteError;
                        var innerRadius = sourcePointMagnitude - ballRadius;
                        if (!(innerRadius > 0.5 * sourcePointMagnitude)) { escalated = true; ranToEnd = false; break; }

                        var supDerivative = TFractal.DerivativeSupremum(orbit, innerRadius);
                        if (!double.IsFinite(supDerivative)) { escalated = true; ranToEnd = false; break; }

                        var denominator = offsetMagnitude - supDerivative * ballRadius;
                        if (!(denominator > 0.0)) { escalated = true; ranToEnd = false; break; }

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

                    if (!(relativeError <= RelativeErrorCap)) { escalated = true; ranToEnd = false; break; }

                    displayError = relativeError * offsetMagnitude * CapInflation
                                   + TableUlps * tableUlp * referenceMagnitude
                                   + CoupledArithmeticUlps * rungUlp * pointMagnitude
                                   + DoubleNarrowingUlp * pointMagnitude;

                    color.FinalMagnitudeSquared = magnitudeSquared;
                    color.FinalPointError = displayError;

                    var escapeUncertainty = 2.0 * pointMagnitude * displayError + displayError * displayError;
                    var escapeMargin = Math.Abs(magnitudeSquared - TFractal.BailoutSquared);
                    if (!(escapeMargin > escapeUncertainty)) { escalated = true; ranToEnd = false; break; }
                }

                if (magnitudeSquared > TFractal.BailoutSquared)
                {
                    if (TShape.Certificate && TShape.IntervalTrap && !TColor.IsCertified(color, iteration + 1))
                        escalated = true;
                    ranToEnd = false;
                    break;
                }

                if (TShape.IntervalTrap)
                {
                    TColor.Accumulate(ref color, re, im, displayError);
                }
                else
                {
                    color.TrapLo = Math.Min(color.TrapLo, magnitudeSquared);
                    color.TrapHi = Math.Min(color.TrapHi, magnitudeSquared) + displayError;
                }

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
                            delta = point - candidatePoint;
                            scaleExp = 0;
                            referenceIndex = candidate;
                            rebases++;
                            if (TShape.RecordRebases) track.Add(iteration, referenceIndex, delta, scaleExp);
                            if (TShape.Certificate)
                            {
                                var pointMagnitude = Math.Sqrt(magnitudeSquared);
                                relativeError = relativeError * (offsetMagnitude / candidateDistance)
                                                + (2.0 * TableUlps * tableUlp + CoupledArithmeticUlps * rungUlp)
                                                  * pointMagnitude / candidateDistance;
                                if (!(relativeError <= RelativeErrorCap)) { escalated = true; ranToEnd = false; break; }
                            }
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

        if (TShape.Certificate && TShape.IntervalTrap && ranToEnd && budget == limit
            && !TColor.IsCertified(color, limit + 1))
            escalated = true;

        sink += delta.Re.HighPart + delta.Im.HighPart + color.TrapMin + color.TrapMinError + scaleExp + referenceIndex;
        return steps;
    }
}
