using System.Numerics;
using System.Runtime.CompilerServices;
using FractalAnimator.Core.Atoms;

namespace FractalAnimator.Core.Kernel;

public enum BatchMode
{
    Scalar,
    Batch,
    Verify,
}

public static class CertifiedIteratorBatch
{
    public static bool VectorCapable => Vector.IsHardwareAccelerated && Vector<double>.Count >= 2;

    public static BatchMode Mode = ModeFromEnvironment();

    public static int BatchSize = BatchSizeFromEnvironment();

    public static long VerifyCompared;
    public static long VerifyMismatched;

    public static readonly long[] Escalation = new long[9];

    public static string EscalationReport()
    {
        var n = new[] { "orbitEnd", "branch", "ball", "supInf", "denom", "cap", "escMargin", "colour", "rebaseCap" };
        var parts = new List<string>();
        for (var i = 0; i < n.Length; i++) if (Escalation[i] != 0) parts.Add($"{n[i]}={Escalation[i]}");
        return parts.Count == 0 ? "none" : string.Join(" ", parts);
    }

    static int _reportBudget = 8;

    static BatchMode ModeFromEnvironment()
    {
        if (!VectorCapable) return BatchMode.Scalar;
        var raw = Environment.GetEnvironmentVariable("FRACTALANIMATOR_KERNELBATCH");
        if (string.IsNullOrEmpty(raw)) return BatchMode.Scalar;
        if (raw.Equals("scalar", StringComparison.OrdinalIgnoreCase)) return BatchMode.Scalar;
        if (raw.Equals("batch", StringComparison.OrdinalIgnoreCase)) return BatchMode.Batch;
        if (raw.Equals("verify", StringComparison.OrdinalIgnoreCase)) return BatchMode.Verify;
        return BatchMode.Batch;
    }

    static int BatchSizeFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("FRACTALANIMATOR_KERNELBATCHSIZE");
        return int.TryParse(raw, out var value) && value > 0 ? value : 64;
    }

    public static void ResetVerification()
    {
        Interlocked.Exchange(ref VerifyCompared, 0);
        Interlocked.Exchange(ref VerifyMismatched, 0);
        Interlocked.Exchange(ref _reportBudget, 8);
    }

    public static void Report(string line)
    {
        if (Interlocked.Decrement(ref _reportBudget) < 0) return;
        Console.Error.WriteLine(line);
    }

    static readonly HashSet<string> Announced = [];

    public static void Announce(string line)
    {
        lock (Announced)
        {
            if (!Announced.Add(line)) return;
        }
        Console.Error.WriteLine(line);
    }

    public static double[] Magnitudes(double[] magnitudeSquared, int length)
    {
        var table = new double[Math.Max(length, 1)];
        for (var i = 0; i < length; i++) table[i] = Math.Sqrt(magnitudeSquared[i]);
        return table;
    }
}

readonly struct VI
{
    public readonly Vector<double> Lo;
    public readonly Vector<double> Hi;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VI(Vector<double> lo, Vector<double> hi) { Lo = lo; Hi = hi; }
}

static class VectorInterval
{
    const long AbsMask = 0x7FFFFFFFFFFFFFFF;
    const long SignMask = unchecked((long)0x8000000000000000);
    const long InfBits = 0x7FF0000000000000;
    const long PosInfKey = 0x7FF0000000000000;
    const long NegInfKey = -9218868437227405312;

    static readonly Vector<long> AbsMaskV = new(AbsMask);
    static readonly Vector<long> SignMaskV = new(SignMask);
    static readonly Vector<long> InfBitsV = new(InfBits);
    static readonly Vector<long> PosInfKeyV = new(PosInfKey);
    static readonly Vector<long> NegInfKeyV = new(NegInfKey);
    static readonly Vector<long> MinV = new(long.MinValue);
    static readonly Vector<double> NaNV = new(double.NaN);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<double> Neg(Vector<double> v) =>
        Vector.AsVectorDouble(Vector.Xor(Vector.AsVectorInt64(v), SignMaskV));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<double> Abs(Vector<double> v) =>
        Vector.AsVectorDouble(Vector.BitwiseAnd(Vector.AsVectorInt64(v), AbsMaskV));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<double> IsNaN(Vector<double> v) =>
        Vector.OnesComplement(Vector.Equals<double>(v, v));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<double> IsNegative(Vector<double> v) =>
        Vector.AsVectorDouble(Vector.LessThan(Vector.AsVectorInt64(v), Vector<long>.Zero));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Vector<double> IsFinite(Vector<double> v) =>
        Vector.AsVectorDouble(Vector.LessThan(
            Vector.BitwiseAnd(Vector.AsVectorInt64(v), AbsMaskV), InfBitsV));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<double> Nudge(Vector<double> v, long steps)
    {
        var bits = Vector.AsVectorInt64(v);
        var finite = Vector.LessThan(Vector.BitwiseAnd(bits, AbsMaskV), InfBitsV);
        var negative = Vector.LessThan(bits, Vector<long>.Zero);
        var key = Vector.ConditionalSelect(negative, MinV - bits, bits);
        key += new Vector<long>(steps);
        key = Vector.ConditionalSelect(Vector.LessThan(key, NegInfKeyV), NegInfKeyV, key);
        key = Vector.ConditionalSelect(Vector.GreaterThan(key, PosInfKeyV), PosInfKeyV, key);
        var negativeKey = Vector.LessThan(key, Vector<long>.Zero);
        var moved = Vector.ConditionalSelect(negativeKey, MinV - key, key);
        if (steps > 0)
            moved = Vector.ConditionalSelect(Vector.Equals(key, Vector<long>.Zero), MinV, moved);
        return Vector.AsVectorDouble(Vector.ConditionalSelect(finite, moved, bits));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<double> Down(Vector<double> v) => Nudge(v, -1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<double> Up(Vector<double> v) => Nudge(v, 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<double> MaxNan(Vector<double> a, Vector<double> b)
    {
        var picked = Vector.ConditionalSelect(Vector.LessThan<double>(b, a), a, b);
        return Vector.ConditionalSelect(IsNaN(a), a, picked);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<double> MinNan(Vector<double> a, Vector<double> b)
    {
        var picked = Vector.ConditionalSelect(Vector.LessThan<double>(a, b), a, b);
        return Vector.ConditionalSelect(IsNaN(a), a, picked);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<double> MinExact(Vector<double> a, Vector<double> b)
    {
        var different = Vector.OnesComplement(Vector.Equals<double>(a, b));
        var unequalBranch = Vector.ConditionalSelect(IsNaN(a), a,
            Vector.ConditionalSelect(Vector.LessThan<double>(a, b), a, b));
        var equalBranch = Vector.ConditionalSelect(IsNegative(a), a, b);
        return Vector.ConditionalSelect(different, unequalBranch, equalBranch);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector<double> IsEmpty(in VI a) => IsNaN(a.Lo) | IsNaN(a.Hi);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VI Empty() => new(NaNV, NaNV);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VI Exact(double value)
    {
        var v = new Vector<double>(value);
        return new VI(v, v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VI Select(Vector<double> condition, in VI whenSet, in VI whenClear) =>
        new(Vector.ConditionalSelect(condition, whenSet.Lo, whenClear.Lo),
            Vector.ConditionalSelect(condition, whenSet.Hi, whenClear.Hi));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VI Ball(Vector<double> centre, Vector<double> radius)
    {
        var bad = IsNaN(centre) | IsNaN(radius);
        var r = Abs(radius);
        var ball = new VI(Down(centre - r), Up(centre + r));
        return Select(bad, Empty(), ball);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VI Add(in VI a, in VI b) => new(Down(a.Lo + b.Lo), Up(a.Hi + b.Hi));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VI Sub(in VI a, in VI b) => new(Down(a.Lo - b.Hi), Up(a.Hi - b.Lo));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VI AddScalar(in VI a, double b)
    {
        var v = new Vector<double>(b);
        return new VI(Down(a.Lo + v), Up(a.Hi + v));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VI MulScalar(in VI a, double b)
    {
        var v = new Vector<double>(b);
        return b >= 0
            ? new VI(Down(a.Lo * v), Up(a.Hi * v))
            : new VI(Down(a.Hi * v), Up(a.Lo * v));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VI Mul(in VI a, in VI b)
    {
        var p1 = a.Lo * b.Lo;
        var p2 = a.Lo * b.Hi;
        var p3 = a.Hi * b.Lo;
        var p4 = a.Hi * b.Hi;
        var lo = Down(MinNan(MinNan(p1, p2), MinNan(p3, p4)));
        var hi = Up(MaxNan(MaxNan(p1, p2), MaxNan(p3, p4)));
        return new VI(lo, hi);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VI AbsInterval(in VI a)
    {
        var zero = Vector<double>.Zero;
        var loNonNegative = Vector.GreaterThanOrEqual<double>(a.Lo, zero);
        var hiNonPositive = Vector.LessThanOrEqual<double>(a.Hi, zero);
        var mirrored = new VI(Neg(a.Hi), Neg(a.Lo));
        var straddling = new VI(zero, MaxNan(Neg(a.Lo), a.Hi));
        return Select(loNonNegative, a, Select(hiNonPositive, mirrored, straddling));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VI MaxConstant(in VI a, double c)
    {
        var v = new Vector<double>(c);
        return new VI(MaxNan(a.Lo, v), MaxNan(a.Hi, v));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VI Sqrt(in VI a)
    {
        var zero = Vector<double>.Zero;
        var negativeHi = Vector.LessThan<double>(a.Hi, zero);
        var nonPositiveLo = Vector.LessThanOrEqual<double>(a.Lo, zero);
        var lo = Vector.ConditionalSelect(nonPositiveLo, zero, Down(Vector.SquareRoot(a.Lo)));
        var hi = Up(Vector.SquareRoot(a.Hi));
        return Select(negativeHi, Empty(), new VI(lo, hi));
    }

    public static VI Log(in VI a)
    {
        var empty = Vector.LessThanOrEqual<double>(a.Lo, Vector<double>.Zero);
        LogPair(a.Lo, a.Hi, out var logLo, out var logHi);
        var loose = new VI(Nudge(logLo, -Interval.TranscendentalUlps),
                           Nudge(logHi, Interval.TranscendentalUlps));
        return Select(empty, Empty(), loose);
    }

    [SkipLocalsInit]
    static void LogPair(Vector<double> a, Vector<double> b, out Vector<double> ra, out Vector<double> rb)
    {
        var width = Vector<double>.Count;
        Span<double> buffer = stackalloc double[2 * Vector<double>.Count];
        a.CopyTo(buffer);
        b.CopyTo(buffer[width..]);
        for (var i = 0; i < buffer.Length; i++) buffer[i] = Math.Log(buffer[i]);
        ra = new Vector<double>(buffer);
        rb = new Vector<double>(buffer[width..]);
    }
}

public static class CertifiedIteratorBatch<T, TFractal, TColor>
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

    const double TrapExponentLeafOffset = 0.59;

    public static readonly bool VectorTrapEnabled =
        CertifiedIteratorBatch.VectorCapable
        && Environment.GetEnvironmentVariable("FRACTALANIMATOR_KERNELVECTORTRAP") != "0"
        && typeof(TColor) == typeof(BStyleTrapAtom)
        && ProbeVectorTrap();

    sealed class Workspace
    {
        public int Capacity;
        public Cx<T>[] Delta = [];
        public int[] ScaleExp = [];
        public int[] RefIndex = [];
        public int[] Slot = [];
        public int[] DecoupledCount = [];
        public int[] RebaseCount = [];
        public byte[] Stopped = [];
        public byte[] DecoupledStep = [];
        public double[] RelErr = [];
        public double[] RelErrOld = [];
        public double[] TrapMin = [];
        public double[] TrapErr = [];
        public double[] FinalMagSq = [];
        public double[] FinalPtErr = [];
        public double[] PrevOffMag = [];
        public double[] SrcMag = [];
        public double[] SrcPtMag = [];
        public double[] PrevAbsErr = [];
        public double[] OffMag = [];
        public double[] PtMag = [];
        public double[] RefMag = [];
        public double[] MagSq = [];
        public double[] Re = [];
        public double[] Im = [];
        public double[] DisplayErr = [];
        public double[] OkMask = [];
        public double[] ContMask = [];
        public double[] DecoupledMask = [];
    }

    static readonly double MaskSet = BitConverter.Int64BitsToDouble(-1L);

    [ThreadStatic] static Workspace? _workspace;

    static Workspace Rent(int count)
    {
        var width = Vector<double>.Count;
        var padded = ((count + width - 1) / width) * width;
        var ws = _workspace ??= new Workspace();
        if (ws.Capacity < padded)
        {
            ws.Capacity = padded;
            ws.Delta = new Cx<T>[padded];
            ws.ScaleExp = new int[padded];
            ws.RefIndex = new int[padded];
            ws.Slot = new int[padded];
            ws.DecoupledCount = new int[padded];
            ws.RebaseCount = new int[padded];
            ws.Stopped = new byte[padded];
            ws.DecoupledStep = new byte[padded];
            ws.RelErr = new double[padded];
            ws.RelErrOld = new double[padded];
            ws.TrapMin = new double[padded];
            ws.TrapErr = new double[padded];
            ws.FinalMagSq = new double[padded];
            ws.FinalPtErr = new double[padded];
            ws.PrevOffMag = new double[padded];
            ws.SrcMag = new double[padded];
            ws.SrcPtMag = new double[padded];
            ws.PrevAbsErr = new double[padded];
            ws.OffMag = new double[padded];
            ws.PtMag = new double[padded];
            ws.RefMag = new double[padded];
            ws.MagSq = new double[padded];
            ws.Re = new double[padded];
            ws.Im = new double[padded];
            ws.DisplayErr = new double[padded];
            ws.OkMask = new double[padded];
            ws.ContMask = new double[padded];
            ws.DecoupledMask = new double[padded];
        }
        return ws;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Vector<double> LD(double[] a, int i) => Vector.LoadUnsafe(ref a[0], (nuint)i);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void ST(double[] a, int i, Vector<double> v) => Vector.StoreUnsafe(v, ref a[0], (nuint)i);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static ColorState ColorOf(Workspace ws, int i) => new()
    {
        TrapMin = ws.TrapMin[i],
        TrapMinError = ws.TrapErr[i],
        FinalMagnitudeSquared = ws.FinalMagSq[i],
        FinalPointError = ws.FinalPtErr[i],
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Benign(Workspace ws, int i)
    {
        ws.PrevOffMag[i] = 0.0;
        ws.SrcMag[i] = 1.0;
        ws.SrcPtMag[i] = 1.0;
        ws.PrevAbsErr[i] = 0.0;
        ws.OffMag[i] = 0.0;
        ws.PtMag[i] = 1.0;
        ws.RefMag[i] = 1.0;
        ws.MagSq[i] = 1.0;
        ws.Re[i] = 1.0;
        ws.Im[i] = 1.0;
        ws.RelErrOld[i] = 0.0;
        ws.DisplayErr[i] = 0.0;
        ws.DecoupledMask[i] = 0.0;
    }

    static void FillPad(Workspace ws, int n)
    {
        var width = Vector<double>.Count;
        var padded = ((n + width - 1) / width) * width;
        for (var i = n; i < padded; i++)
        {
            Benign(ws, i);
            ws.RelErr[i] = 0.0;
            ws.TrapMin[i] = 1e5;
            ws.TrapErr[i] = 0.0;
            ws.FinalMagSq[i] = 1.0;
            ws.FinalPtErr[i] = 0.0;
            ws.OkMask[i] = 0.0;
            ws.ContMask[i] = 0.0;
            ws.Stopped[i] = 1;
            ws.DecoupledStep[i] = 0;
        }
    }

    public static void Iterate(
        ReferenceOrbit<T> orbit, double[] magnitude, ReadOnlySpan<Cx<T>> deltas, int count,
        int scaleExp, int maxIterations, Span<PixelResult> results)
    {
        if (count <= 0) return;

        var mode = CertifiedIteratorBatch.Mode;
        if (mode == BatchMode.Scalar || !CertifiedIteratorBatch.VectorCapable)
        {
            for (var j = 0; j < count; j++)
                results[j] = CertifiedIterator<T, TFractal, TColor>.Iterate(orbit, deltas[j], scaleExp, maxIterations);
            return;
        }

        if (mode == BatchMode.Verify)
        {
            CertifiedIteratorBatch.Announce(
                $"batch-verify rung={T.RungName} lanes={Vector<double>.Count} " +
                $"batchSize={CertifiedIteratorBatch.BatchSize} vectorTrap={VectorTrapEnabled}");
            var batched = new PixelResult[count];
            RunBatch(orbit, magnitude, deltas, count, scaleExp, maxIterations, batched);
            long mismatched = 0;
            for (var j = 0; j < count; j++)
            {
                results[j] = CertifiedIterator<T, TFractal, TColor>.Iterate(orbit, deltas[j], scaleExp, maxIterations);
                if (Same(results[j], batched[j])) continue;
                mismatched++;
                CertifiedIteratorBatch.Report(
                    $"batch-mismatch rung={T.RungName} vectorTrap={VectorTrapEnabled} " +
                    $"scalar=({results[j].Verdict},{results[j].Iterations},{results[j].Color.TrapMin:G17},{results[j].Color.TrapMinError:G17}," +
                    $"{results[j].Color.FinalMagnitudeSquared:G17},{results[j].Color.FinalPointError:G17},{results[j].DecoupledSteps},{results[j].Rebases}) " +
                    $"batch=({batched[j].Verdict},{batched[j].Iterations},{batched[j].Color.TrapMin:G17},{batched[j].Color.TrapMinError:G17}," +
                    $"{batched[j].Color.FinalMagnitudeSquared:G17},{batched[j].Color.FinalPointError:G17},{batched[j].DecoupledSteps},{batched[j].Rebases})");
            }
            Interlocked.Add(ref CertifiedIteratorBatch.VerifyCompared, count);
            if (mismatched != 0) Interlocked.Add(ref CertifiedIteratorBatch.VerifyMismatched, mismatched);
            return;
        }

        RunBatch(orbit, magnitude, deltas, count, scaleExp, maxIterations, results);
    }

    static bool Same(in PixelResult a, in PixelResult b) =>
        a.Verdict == b.Verdict
        && a.Iterations == b.Iterations
        && a.DecoupledSteps == b.DecoupledSteps
        && a.Rebases == b.Rebases
        && BitConverter.DoubleToInt64Bits(a.Color.TrapMin) == BitConverter.DoubleToInt64Bits(b.Color.TrapMin)
        && BitConverter.DoubleToInt64Bits(a.Color.TrapMinError) == BitConverter.DoubleToInt64Bits(b.Color.TrapMinError)
        && BitConverter.DoubleToInt64Bits(a.Color.FinalMagnitudeSquared) == BitConverter.DoubleToInt64Bits(b.Color.FinalMagnitudeSquared)
        && BitConverter.DoubleToInt64Bits(a.Color.FinalPointError) == BitConverter.DoubleToInt64Bits(b.Color.FinalPointError);

    static void Retire(Workspace ws, int i, Span<PixelResult> results, PixelVerdict verdict, int iterations)
    {
        results[ws.Slot[i]] = new PixelResult
        {
            Verdict = verdict,
            Iterations = iterations,
            Color = ColorOf(ws, i),
            DecoupledSteps = ws.DecoupledCount[i],
            Rebases = ws.RebaseCount[i],
        };
        ws.Stopped[i] = 1;
    }

    static int Compact(Workspace ws, int n)
    {
        var m = 0;
        for (var i = 0; i < n; i++)
        {
            if (ws.Stopped[i] != 0) continue;
            if (m != i)
            {
                ws.Delta[m] = ws.Delta[i];
                ws.ScaleExp[m] = ws.ScaleExp[i];
                ws.RefIndex[m] = ws.RefIndex[i];
                ws.Slot[m] = ws.Slot[i];
                ws.DecoupledCount[m] = ws.DecoupledCount[i];
                ws.RebaseCount[m] = ws.RebaseCount[i];
                ws.RelErr[m] = ws.RelErr[i];
                ws.TrapMin[m] = ws.TrapMin[i];
                ws.TrapErr[m] = ws.TrapErr[i];
                ws.FinalMagSq[m] = ws.FinalMagSq[i];
                ws.FinalPtErr[m] = ws.FinalPtErr[i];
            }
            m++;
        }
        return m;
    }

    static void RunBatch(
        ReferenceOrbit<T> orbit, double[] magnitude, ReadOnlySpan<Cx<T>> deltas, int count,
        int scaleExp0, int maxIterations, Span<PixelResult> results)
    {
        var width = Vector<double>.Count;
        var ws = Rent(count);
        var n = count;

        for (var i = 0; i < n; i++)
        {
            ws.Delta[i] = deltas[i];
            ws.ScaleExp[i] = scaleExp0;
            ws.RefIndex[i] = 0;
            ws.Slot[i] = i;
            ws.DecoupledCount[i] = 0;
            ws.RebaseCount[i] = 0;
            ws.RelErr[i] = 0.0;
            ws.TrapMin[i] = 1e5;
            ws.TrapErr[i] = 0.0;
            ws.FinalMagSq[i] = 1.0;
            ws.FinalPtErr[i] = 0.0;
        }
        FillPad(ws, n);

        var limit = maxIterations - 1;
        var rungUlp = T.RelativeUlp;
        var tableUlp = orbit.TableRelativeUlp;
        var arithmeticFloor = CoupledArithmeticUlps * rungUlp;
        var tableFloor = TableUlps * tableUlp;
        var bailout = TFractal.BailoutSquared;
        var truncationCoefficient = TFractal.TruncationCoefficient;
        var orbitLength = orbit.Length;
        var useVectorTrap = VectorTrapEnabled;

        var vArithmeticFloor = new Vector<double>(arithmeticFloor);
        var vTableFloor = new Vector<double>(tableFloor);
        var vTruncation = new Vector<double>(truncationCoefficient);
        var vCapInflation = new Vector<double>(CapInflation);
        var vCap = new Vector<double>(RelativeErrorCap);
        var vNarrowing = new Vector<double>(DoubleNarrowingUlp);
        var vBailout = new Vector<double>(bailout);
        var vOne = new Vector<double>(1.0);
        var vFive = new Vector<double>(5.0);
        var vTwo = new Vector<double>(2.0);
        var vZero = Vector<double>.Zero;

        for (var iteration = 0; iteration < limit && n > 0; iteration++)
        {
            var anyStopped = false;
            var anyDecoupled = false;
            var coupledLanes = 0;

            for (var i = 0; i < n; i++)
            {
                ws.Stopped[i] = 0;
                ws.DecoupledStep[i] = 0;

                var referenceIndex = ws.RefIndex[i];
                if (referenceIndex + 1 >= orbitLength)
                {
                    Interlocked.Increment(ref CertifiedIteratorBatch.Escalation[0]);
                    Retire(ws, i, results, PixelVerdict.NeedsHigherPrecision, iteration);
                    Benign(ws, i);
                    anyStopped = true;
                    continue;
                }

                var previousOffset = Cx<T>.ScaleB(ws.Delta[i], ws.ScaleExp[i]);
                var previousOffsetMagnitude = Math.Sqrt(previousOffset.MagnitudeSquared);
                var relativeError = ws.RelErr[i];
                var previousAbsoluteError = relativeError * previousOffsetMagnitude * CapInflation;

                TFractal.Step(orbit, referenceIndex, ref ws.Delta[i], ref ws.ScaleExp[i], out var decoupled, relativeError, out var branchUndecidable);
                if (branchUndecidable) { Interlocked.Increment(ref CertifiedIteratorBatch.Escalation[1]); Retire(ws, i, results, PixelVerdict.NeedsHigherPrecision, iteration); continue; }
                ws.RefIndex[i] = referenceIndex + 1;

                var offset = Cx<T>.ScaleB(ws.Delta[i], ws.ScaleExp[i]);
                var point = orbit.Z[referenceIndex + 1] + offset;
                var re = point.Re.ToDouble();
                var im = point.Im.ToDouble();
                var magnitudeSquared = re * re + im * im;

                ws.RelErrOld[i] = relativeError;
                ws.PrevOffMag[i] = previousOffsetMagnitude;
                ws.PrevAbsErr[i] = previousAbsoluteError;
                ws.SrcMag[i] = magnitude[referenceIndex];
                ws.OffMag[i] = Math.Sqrt(offset.MagnitudeSquared);
                ws.RefMag[i] = magnitude[referenceIndex + 1];
                ws.MagSq[i] = magnitudeSquared;
                ws.PtMag[i] = Math.Sqrt(magnitudeSquared);
                ws.Re[i] = re;
                ws.Im[i] = im;
                ws.DecoupledMask[i] = decoupled ? MaskSet : 0.0;

                if (decoupled)
                {
                    ws.DecoupledCount[i]++;
                    ws.DecoupledStep[i] = 1;
                    anyDecoupled = true;
                    var sourcePoint = orbit.Z[referenceIndex] + previousOffset;
                    ws.SrcPtMag[i] = Math.Sqrt(sourcePoint.MagnitudeSquared);
                }
                else
                {
                    coupledLanes++;
                }
            }

            if (anyDecoupled)
            {
                for (var i = 0; i < n; i++)
                {
                    if (ws.Stopped[i] != 0 || ws.DecoupledStep[i] == 0) continue;

                    var ballRadius = ws.PrevAbsErr[i];
                    var sourcePointMagnitude = ws.SrcPtMag[i];
                    var innerRadius = sourcePointMagnitude - ballRadius;
                    if (!(innerRadius > 0.5 * sourcePointMagnitude))
                    {
                        Interlocked.Increment(ref CertifiedIteratorBatch.Escalation[2]);
                        Retire(ws, i, results, PixelVerdict.NeedsHigherPrecision, iteration);
                        Benign(ws, i);
                        anyStopped = true;
                        continue;
                    }

                    var supDerivative = TFractal.DerivativeSupremum(orbit, innerRadius);
                    if (!double.IsFinite(supDerivative))
                    {
                        Interlocked.Increment(ref CertifiedIteratorBatch.Escalation[3]);
                        Retire(ws, i, results, PixelVerdict.NeedsHigherPrecision, iteration);
                        Benign(ws, i);
                        anyStopped = true;
                        continue;
                    }

                    var denominator = ws.OffMag[i] - supDerivative * ballRadius;
                    if (!(denominator > 0.0))
                    {
                        Interlocked.Increment(ref CertifiedIteratorBatch.Escalation[4]);
                        Retire(ws, i, results, PixelVerdict.NeedsHigherPrecision, iteration);
                        Benign(ws, i);
                        anyStopped = true;
                        continue;
                    }

                    var amplification = supDerivative * ws.PrevOffMag[i] / denominator;
                    ws.RelErr[i] = (ws.RelErrOld[i] + TableUlps * tableUlp + CoupledArithmeticUlps * rungUlp) * amplification
                                   + CoupledArithmeticUlps * rungUlp;
                }
            }

            var everyLaneDecoupled = coupledLanes == 0;

            for (var i = 0; i < n; i += width)
            {
                Vector<double> relativeError;
                if (everyLaneDecoupled)
                {
                    relativeError = LD(ws.RelErr, i);
                }
                else
                {
                    var sourceMagnitude = LD(ws.SrcMag, i);
                    var previousOffsetMagnitude = LD(ws.PrevOffMag, i);
                    var previousAbsoluteError = LD(ws.PrevAbsErr, i);
                    var previousRelativeError = LD(ws.RelErrOld, i);

                    var u = Vector.ConditionalSelect(
                        Vector.GreaterThan<double>(sourceMagnitude, vZero),
                        (previousOffsetMagnitude + previousAbsoluteError) / sourceMagnitude,
                        vZero);
                    var truncation = vTruncation * u * u;
                    var coupled = previousRelativeError * (vOne + vFive * u)
                                  + vArithmeticFloor + vTableFloor + truncation;

                    relativeError = anyDecoupled
                        ? Vector.ConditionalSelect(LD(ws.DecoupledMask, i), LD(ws.RelErr, i), coupled)
                        : coupled;
                    ST(ws.RelErr, i, relativeError);
                }

                var offsetMagnitude = LD(ws.OffMag, i);
                var pointMagnitude = LD(ws.PtMag, i);
                var referenceMagnitude = LD(ws.RefMag, i);
                var magnitudeSquared = LD(ws.MagSq, i);

                var capOk = Vector.LessThanOrEqual<double>(relativeError, vCap);
                var displayError = relativeError * offsetMagnitude * vCapInflation
                                   + vTableFloor * referenceMagnitude
                                   + vArithmeticFloor * pointMagnitude
                                   + vNarrowing * pointMagnitude;

                ST(ws.FinalMagSq, i, Vector.ConditionalSelect(capOk, magnitudeSquared, LD(ws.FinalMagSq, i)));
                ST(ws.FinalPtErr, i, Vector.ConditionalSelect(capOk, displayError, LD(ws.FinalPtErr, i)));

                var escapeUncertainty = vTwo * pointMagnitude * displayError + displayError * displayError;
                var escapeMargin = VectorInterval.Abs(magnitudeSquared - vBailout);
                var marginOk = Vector.GreaterThan<double>(escapeMargin, escapeUncertainty);
                var ok = capOk & marginOk;
                var escaped = Vector.GreaterThan<double>(magnitudeSquared, vBailout);
                var keep = Vector.AndNot(ok, escaped);

                ST(ws.OkMask, i, ok);
                ST(ws.ContMask, i, keep);

                if (useVectorTrap)
                {
                    var trapMin = LD(ws.TrapMin, i);
                    var trapErr = LD(ws.TrapErr, i);
                    var nextMin = trapMin;
                    var nextErr = trapErr;
                    AccumulateVector(LD(ws.Re, i), LD(ws.Im, i), displayError, ref nextMin, ref nextErr);
                    ST(ws.TrapMin, i, Vector.ConditionalSelect(keep, nextMin, trapMin));
                    ST(ws.TrapErr, i, Vector.ConditionalSelect(keep, nextErr, trapErr));
                }
                else
                {
                    ST(ws.DisplayErr, i, displayError);
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (ws.Stopped[i] != 0) continue;
                if (BitConverter.DoubleToInt64Bits(ws.ContMask[i]) != 0) continue;

                anyStopped = true;
                if (BitConverter.DoubleToInt64Bits(ws.OkMask[i]) == 0)
                {
                    Interlocked.Increment(ref CertifiedIteratorBatch.Escalation[ws.RelErr[i] > RelativeErrorCap ? 5 : 6]);
                    Retire(ws, i, results, PixelVerdict.NeedsHigherPrecision, iteration);
                    Benign(ws, i);
                    continue;
                }

                var certified = TColor.IsCertified(ColorOf(ws, i), iteration + 1);
                if (!certified) Interlocked.Increment(ref CertifiedIteratorBatch.Escalation[7]);
                Retire(ws, i, results,
                    certified ? PixelVerdict.Escaped : PixelVerdict.NeedsHigherPrecision,
                    certified ? iteration + 1 : iteration);
                Benign(ws, i);
            }

            if (!useVectorTrap)
            {
                for (var i = 0; i < n; i++)
                {
                    if (ws.Stopped[i] != 0) continue;
                    var color = ColorOf(ws, i);
                    TColor.Accumulate(ref color, ws.Re[i], ws.Im[i], ws.DisplayErr[i]);
                    ws.TrapMin[i] = color.TrapMin;
                    ws.TrapErr[i] = color.TrapMinError;
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (ws.Stopped[i] != 0) continue;

                var offsetMagnitude = ws.OffMag[i];
                if (KernelOptions.RebaseEnabled && ws.DecoupledStep[i] != 0 && offsetMagnitude > 0.0)
                {
                    var re = ws.Re[i];
                    var im = ws.Im[i];
                    var candidate = orbit.Nearest.Query(re, im);
                    if (candidate >= 0 && candidate + 1 < orbitLength)
                    {
                        var candidatePoint = orbit.Z[candidate];
                        var dr = re - candidatePoint.Re.ToDouble();
                        var di = im - candidatePoint.Im.ToDouble();
                        var candidateDistance = Math.Sqrt(dr * dr + di * di);
                        if (candidateDistance > 0.0 && candidateDistance < RebaseGainThreshold * offsetMagnitude)
                        {
                            var rebased = ws.RelErr[i] * (offsetMagnitude / candidateDistance)
                                          + (2.0 * TableUlps * tableUlp + CoupledArithmeticUlps * rungUlp)
                                            * ws.PtMag[i] / candidateDistance;
                            if (rebased <= RelativeErrorCap)
                            {
                                var point = orbit.Z[ws.RefIndex[i]] + Cx<T>.ScaleB(ws.Delta[i], ws.ScaleExp[i]);
                                ws.Delta[i] = point - candidatePoint;
                                ws.ScaleExp[i] = 0;
                                ws.RefIndex[i] = candidate;
                                ws.RebaseCount[i]++;
                                ws.RelErr[i] = rebased;
                            }
                        }
                    }
                }

                var leading = Math.Max(Math.Abs(ws.Delta[i].Re.HighPart), Math.Abs(ws.Delta[i].Im.HighPart));
                if (leading > RenormThreshold || (leading > 0.0 && leading < 1.0e-15))
                {
                    var k = Math.ILogB(leading);
                    ws.Delta[i] = Cx<T>.ScaleB(ws.Delta[i], -k);
                    ws.ScaleExp[i] += k;
                }
            }

            if (anyStopped)
            {
                n = Compact(ws, n);
                FillPad(ws, n);
            }
        }

        for (var i = 0; i < n; i++)
        {
            var color = ColorOf(ws, i);
            var certified = TColor.IsCertified(color, maxIterations);
            results[ws.Slot[i]] = new PixelResult
            {
                Verdict = certified ? PixelVerdict.Interior : PixelVerdict.NeedsHigherPrecision,
                Iterations = certified ? maxIterations : limit,
                Color = color,
                DecoupledSteps = ws.DecoupledCount[i],
                Rebases = ws.RebaseCount[i],
            };
        }
    }

    static VI TrapIntervalVector(Vector<double> re, Vector<double> im, Vector<double> pointError)
    {
        var five = new Vector<double>(5.0);
        var radius = five * pointError;

        var x = VectorInterval.Ball(five * re, radius);
        var y = VectorInterval.AddScalar(
            VectorInterval.AbsInterval(VectorInterval.Ball(five * im, radius)), TrapExponentLeafOffset);

        var norm = VectorInterval.Sqrt(
            VectorInterval.Add(VectorInterval.Mul(x, x), VectorInterval.Mul(y, y)));
        var leaf = VectorInterval.Sub(norm, VectorInterval.Exact(TrapExponentLeafOffset));

        var a = VectorInterval.Log(VectorInterval.MaxConstant(leaf, 1e-10));
        var emptyA = VectorInterval.IsEmpty(a);

        var b = VectorInterval.Log(
            VectorInterval.MaxConstant(VectorInterval.AbsInterval(a), 1e-10));
        var emptyB = VectorInterval.IsEmpty(b);

        var inner = VectorInterval.AddScalar(
            VectorInterval.MulScalar(VectorInterval.AbsInterval(b), 0.4), 0.04);

        var result = VectorInterval.Log(inner);
        return VectorInterval.Select(emptyA | emptyB, VectorInterval.Empty(), result);
    }

    static void AccumulateVector(
        Vector<double> re, Vector<double> im, Vector<double> pointError,
        ref Vector<double> trapMin, ref Vector<double> trapErr)
    {
        var trapBall = TrapIntervalVector(re, im, pointError);
        var empty = VectorInterval.IsEmpty(trapBall);

        var infinity = new Vector<double>(double.PositiveInfinity);
        var nan = new Vector<double>(double.NaN);
        var half = new Vector<double>(0.5);

        var undefined = VectorInterval.IsNaN(trapMin);
        var lo = Vector.ConditionalSelect(undefined, infinity, trapMin - trapErr);
        var hi = Vector.ConditionalSelect(undefined, infinity, trapMin + trapErr);

        var newLo = VectorInterval.MinExact(lo, trapBall.Lo);
        var newHi = VectorInterval.MinExact(hi, trapBall.Hi);

        trapMin = Vector.ConditionalSelect(empty, nan, half * (newLo + newHi));
        trapErr = Vector.ConditionalSelect(empty, infinity, half * (newHi - newLo));
    }

    static bool ProbeVectorTrap()
    {
        var width = Vector<double>.Count;
        double[] points =
        [
            0.0, -0.0, 1e-300, -1e-300, 1e-16, 0.01, -0.01, 0.05, -0.05, 0.117, -0.117,
            0.118, -0.118, 0.2, -0.2, 0.5, -0.5, 1.0, -1.0, 3.0, -3.0, 5.0, -5.0,
            31.9, -31.9, 1e5, -1e5, 1e150, double.NaN, double.PositiveInfinity,
        ];
        double[] errors = [0.0, 1e-300, 1e-18, 1e-13, 1e-9, 1e-5, 1e-2, 0.5, 1e3, double.NaN];

        var re = new double[width];
        var im = new double[width];
        var scalarMin = new double[width];
        var scalarErr = new double[width];

        foreach (var error in errors)
        {
            for (var s = 0; s < points.Length; s++)
            {
                for (var lane = 0; lane < width; lane++)
                {
                    re[lane] = points[(s + 3 * lane) % points.Length];
                    im[lane] = points[(s + 5 * lane + 1) % points.Length];
                    scalarMin[lane] = 1e5;
                    scalarErr[lane] = 0.0;
                }

                var vectorMin = new Vector<double>(1e5);
                var vectorErr = Vector<double>.Zero;
                var vre = new Vector<double>(re);
                var vim = new Vector<double>(im);
                var vpe = new Vector<double>(error);

                for (var round = 0; round < 3; round++)
                {
                    for (var lane = 0; lane < width; lane++)
                    {
                        var state = new ColorState
                        {
                            TrapMin = scalarMin[lane],
                            TrapMinError = scalarErr[lane],
                            FinalMagnitudeSquared = 1.0,
                            FinalPointError = 0.0,
                        };
                        TColor.Accumulate(ref state, re[lane], im[lane], error);
                        scalarMin[lane] = state.TrapMin;
                        scalarErr[lane] = state.TrapMinError;
                    }

                    AccumulateVector(vre, vim, vpe, ref vectorMin, ref vectorErr);

                    for (var lane = 0; lane < width; lane++)
                    {
                        if (BitConverter.DoubleToInt64Bits(vectorMin[lane]) != BitConverter.DoubleToInt64Bits(scalarMin[lane]))
                            return false;
                        if (BitConverter.DoubleToInt64Bits(vectorErr[lane]) != BitConverter.DoubleToInt64Bits(scalarErr[lane]))
                            return false;
                    }
                }
            }
        }

        return true;
    }
}
