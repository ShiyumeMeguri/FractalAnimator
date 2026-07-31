using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using FractalAnimator.Core;
using FractalAnimator.Core.Atoms;
using FractalAnimator.Core.Kernel;

namespace FractalAnimator.Diagnostics;

public static class DecouplingAudit
{
    const string CentreReal = "1.69606094102335990831239662074185263380097653556446974121776163540637298024853";
    const string CentreImag = "2.08049902493822965286894308802169375262087436820149099900777715195242489638537";
    const double CoupledThresholdSquared = 1.0e-16;
    const double RenormLarge = 1.0e15;
    const double RenormSmall = 1.0e-15;
    const double RebaseGainThreshold = 0.5;
    const double RelativeErrorCap = 0.1;
    const double CapInflation = 1.0 / (1.0 - RelativeErrorCap);
    const double DoubleNarrowingUlp = 1.1102230246251565e-16;
    const double CoupledArithmeticUlps = 2.0;
    const double TableUlps = 1.0;

    sealed class StepRecord
    {
        public int N;
        public int ReferenceIndex;
        public int ScaleExpBefore;
        public int ScaleExpAfter;
        public double DeltaLeadingBefore;
        public double OffsetMagnitudeBefore;
        public double UMagnitudeBefore;
        public double PointMagnitudeSquared;
        public bool Decoupled;
        public string Event = "";
    }

    sealed class TraceResult
    {
        public string Name = "";
        public List<StepRecord> Steps = [];
        public int FirstDecoupled = -1;
        public int DecoupledSteps;
        public int TerminatedAt = -1;
        public string Reason = "";
        public double Trap = 1e5;
        public int RenormLargeCount;
        public int RenormSmallCount;
        public int Rebases;
        public ColorState Color;
    }

    static int DistinctColors<T>(
        ReferenceOrbit<T> orbit, int size, double offsetToDelta, double half, int scaleExp0, int maxIterations, int stride)
        where T : struct, IPrecision<T>
    {
        var seen = new HashSet<int>();
        for (var y = 0; y < size; y += stride)
        for (var x = 0; x < size; x += stride)
        {
            var t = TraceNew<T>(orbit, (x - half) * offsetToDelta, (half - y) * offsetToDelta, scaleExp0, maxIterations, true);
            var (r, g, b) = BStyleTrapAtom.Resolve(t.Color, t.TerminatedAt);
            var rr = (byte)Math.Clamp((int)(r * 255), 0, 255);
            var gg = (byte)Math.Clamp((int)(g * 255), 0, 255);
            var bb = (byte)Math.Clamp((int)(b * 255), 0, 255);
            seen.Add((rr << 16) | (gg << 8) | bb);
        }
        return seen.Count;
    }

    [ModuleInitializer]
    internal static void Hook()
    {
        if (Environment.GetEnvironmentVariable("FRACTAL_DECOUPLING_AUDIT") is null) return;
        try { Run(); }
        catch (Exception ex) { Console.WriteLine("AUDIT FAILED: " + ex); }
        Console.Out.Flush();
        Environment.Exit(0);
    }

    static double EnvDouble(string name, double fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    static int EnvInt(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return raw is not null && int.TryParse(raw, out var v) ? v : fallback;
    }

    public static void Run()
    {
        var zooms = EnvDouble("AUDIT_ZOOMS", 60.0);
        var size = EnvInt("AUDIT_SIZE", 200);
        var iterations = EnvInt("AUDIT_ITERATIONS", 300);
        var tableRows = EnvInt("AUDIT_TABLE", 130);

        var cx = BigDecimalCompat.Parse(CentreReal);
        var cy = BigDecimalCompat.Parse(CentreImag);
        var p = SelfTest.CorruptionParams();
        var kRe = p.JuliaCXRaw * 1e-5;
        var kIm = p.JuliaCYRaw * 1e-5;
        var leafOffset = p.LeafOrbitOffset;

        var spacingLog2 = 2.0 - zooms;
        var scaleExp0 = (int)Math.Floor(spacingLog2);
        var mantissa = Math.Pow(2.0, spacingLog2 - scaleExp0);
        var offsetToDelta = mantissa / size;
        var half = size / 2.0;

        Console.WriteLine($"== DecouplingAudit zooms={zooms} size={size} iterations={iterations} ==");
        Console.WriteLine($"power=({p.PowerReal},{p.PowerImag}) K=({kRe:G17},{kIm:G17}) leafOffset={leafOffset}");
        Console.WriteLine($"scaleExp0={scaleExp0} mantissa={mantissa:G17} offsetToDelta={offsetToDelta:G17} half={half}");

        var oldReference = DeepOrbitTrapEngine.BuildReference(
            cx, cy, BigDecimalCompat.FromDouble(kRe), BigDecimalCompat.FromDouble(kIm),
            p.PowerReal, p.PowerImag, zooms, iterations, CancellationToken.None);
        var baked = PowerJuliaBaker.Bake(cx, cy, kRe, kIm, p.PowerReal, p.PowerImag, iterations);
        var orbitDd = PowerJuliaBaker.Narrow<DoubleDouble>(baked);
        var orbit32 = PowerJuliaBaker.Narrow<Fp32>(baked);

        Console.WriteLine($"old reference length={oldReference.Length} new baked length={baked.Length} bakedPrecision={baked.Precision}");
        Console.WriteLine($"old refEscapeIteration={FirstAbove(oldReference.MagnitudeSquared, oldReference.Length, 1024.0)} " +
                          $"new refEscapeIteration={FirstAbove(baked.MagnitudeSquared, baked.Length, 1024.0)}");
        Console.WriteLine($"reference |Z| divergence between engines: {ReferenceDivergence(oldReference, baked)}");
        Console.WriteLine($"mean log2 expansion over first 120 steps = {MeanLog2Expansion(baked, 120):F4} (factor {Math.Pow(2.0, MeanLog2Expansion(baked, 120)):F4}/iteration)");
        Console.WriteLine();

        (int x, int y)[] probes =
        [
            (50, 50), (100, 100), (150, 150), (25, 150), (175, 25), (10, 190), (199, 0), (100, 30),
        ];

        var first = true;
        foreach (var (px, py) in probes)
        {
            var dcr = (px - half) * offsetToDelta;
            var dci = (half - py) * offsetToDelta;
            Console.WriteLine($"--- pixel ({px},{py}) deltaC=({dcr:G17},{dci:G17}) physicalOffset={Math.Sqrt(dcr * dcr + dci * dci) * Math.ScaleB(1.0, scaleExp0):E3}");

            var oldTrace = TraceOld(oldReference, dcr, dci, scaleExp0, iterations, leafOffset);
            var newDd = TraceNew<DoubleDouble>(orbitDd, dcr, dci, scaleExp0, iterations, true);
            var newDdNoRebase = TraceNew<DoubleDouble>(orbitDd, dcr, dci, scaleExp0, iterations, false);
            var new32 = TraceNew<Fp32>(orbit32, dcr, dci, scaleExp0, iterations, true);

            Report(oldTrace);
            Report(newDd);
            Report(newDdNoRebase);
            Report(new32);
            Compare(oldTrace, newDd);
            Compare(oldTrace, new32);

            if (first)
            {
                first = false;
                PrintTable(oldTrace, newDd, new32, tableRows);
            }
            Console.WriteLine();
        }

        Console.WriteLine("=== renormalisation events (new kernel, DD, pixel (50,50)) ===");
        var renormTrace = TraceNew<DoubleDouble>(orbitDd, (50 - half) * offsetToDelta, (half - 50) * offsetToDelta, scaleExp0, iterations, true);
        foreach (var s in renormTrace.Steps)
            if (s.Event.Length > 0)
                Console.WriteLine($"  n={s.N,3} scaleExp {s.ScaleExpBefore,5} -> {s.ScaleExpAfter,5} deltaLeading={s.DeltaLeadingBefore:E3} {s.Event}");

        Console.WriteLine();
        ReferenceOrbitAudit(cx, cy, kRe, kIm, p.PowerReal, p.PowerImag, Math.Min(iterations, EnvInt("AUDIT_REFSTEPS", 150)));

        Console.WriteLine();
        Console.WriteLine("=== crossover test: swap the Julia constant representation between the engines ===");
        var oldExactK = DeepOrbitTrapEngine.BuildReference(
            cx, cy, BigDecimalCompat.FromDoubleExact(kRe), BigDecimalCompat.FromDoubleExact(kIm),
            p.PowerReal, p.PowerImag, zooms, iterations, CancellationToken.None);
        var kernelRoundedK = BuildKernelOrbit<DoubleDouble>(
            cx, cy, BigDecimalCompat.FromDouble(kRe), BigDecimalCompat.FromDouble(kIm),
            p.PowerReal, p.PowerImag, kRe, kIm, iterations, baked.Precision);
        var kernelExactK = BuildKernelOrbit<DoubleDouble>(
            cx, cy, BigDecimalCompat.FromDoubleExact(kRe), BigDecimalCompat.FromDoubleExact(kIm),
            p.PowerReal, p.PowerImag, kRe, kIm, iterations, baked.Precision);
        Console.WriteLine($"  hand-built kernel orbit with exact K matches PowerJuliaBaker.Bake: {OrbitsMatch(kernelExactK, orbitDd)}");
        var stride = Math.Max(1, EnvInt("AUDIT_STRIDE", 4));
        Console.WriteLine($"  new-kernel distinctColors over the {size}x{size} view (stride {stride}): " +
                          $"Kexact(shipping)={DistinctColors(kernelExactK, size, offsetToDelta, half, scaleExp0, iterations, stride)} " +
                          $"Kround(old-engine representation)={DistinctColors(kernelRoundedK, size, offsetToDelta, half, scaleExp0, iterations, stride)}");
        for (var px = 0; px < size; px += size / 10)
        {
            var dcr = (px - half) * offsetToDelta;
            var dci = (half - 100) * offsetToDelta;
            var oldRounded = TraceOld(oldReference, dcr, dci, scaleExp0, iterations, leafOffset);
            var oldExact = TraceOld(oldExactK, dcr, dci, scaleExp0, iterations, leafOffset);
            var newExact = TraceNew<DoubleDouble>(kernelExactK, dcr, dci, scaleExp0, iterations, true);
            var newRounded = TraceNew<DoubleDouble>(kernelRoundedK, dcr, dci, scaleExp0, iterations, true);
            Console.WriteLine($"  px={px,4} OLD/Kround end={oldRounded.TerminatedAt,4} trap={oldRounded.Trap,11:G8} | " +
                              $"OLD/Kexact end={oldExact.TerminatedAt,4} trap={oldExact.Trap,11:G8} | " +
                              $"NEW/Kexact end={newExact.TerminatedAt,4} trap={newExact.Trap,11:G8} | " +
                              $"NEW/Kround end={newRounded.TerminatedAt,4} trap={newRounded.Trap,11:G8}");
        }

        Console.WriteLine();
        Console.WriteLine("=== spread across a row: escape iteration and first decoupling, new(DD) vs old ===");
        for (var px = 0; px < size; px += size / 20)
        {
            var dcr = (px - half) * offsetToDelta;
            var dci = (half - 100) * offsetToDelta;
            var o = TraceOld(oldReference, dcr, dci, scaleExp0, iterations, leafOffset);
            var d = TraceNew<DoubleDouble>(orbitDd, dcr, dci, scaleExp0, iterations, true);
            var f = TraceNew<Fp32>(orbit32, dcr, dci, scaleExp0, iterations, true);
            Console.WriteLine($"  px={px,4} OLD(dec@{o.FirstDecoupled,4} end={o.TerminatedAt,4} {o.Reason,-22} trap={o.Trap,12:G8})  " +
                              $"DD(dec@{d.FirstDecoupled,4} end={d.TerminatedAt,4} {d.Reason,-22})  " +
                              $"FP32(dec@{f.FirstDecoupled,4} end={f.TerminatedAt,4} {f.Reason,-22})");
        }
    }

    static bool OrbitsMatch<T>(ReferenceOrbit<T> a, ReferenceOrbit<T> b) where T : struct, IPrecision<T>
    {
        if (a.Length != b.Length) return false;
        for (var n = 0; n < a.Length; n++)
        {
            if (a.Z[n].Re.HighPart != b.Z[n].Re.HighPart) return false;
            if (a.Z[n].Im.HighPart != b.Z[n].Im.HighPart) return false;
        }
        return true;
    }

    static ReferenceOrbit<T> BuildKernelOrbit<T>(
        BigDecimalCompat cRe, BigDecimalCompat cIm, BigDecimalCompat kRe, BigDecimalCompat kIm,
        double powerReal, double powerImag, double juliaRealPayload, double juliaImagPayload,
        int iterations, int precision)
        where T : struct, IPrecision<T>
    {
        var ctx = new MathContextCompat(precision);
        var length = iterations + 1;
        var z = new Cx<T>[length];
        var aux = new Cx<T>[3][];
        for (var a = 0; a < 3; a++) aux[a] = new Cx<T>[length];
        var magnitudeSquared = new double[length];
        var expansion = new double[length];
        var leadingRe = new double[length];
        var leadingIm = new double[length];

        var zr = cRe;
        var zi = cIm;
        z[0] = Narrow<T>(zr, zi, ctx);
        leadingRe[0] = zr.ToDouble();
        leadingIm[0] = zi.ToDouble();
        magnitudeSquared[0] = leadingRe[0] * leadingRe[0] + leadingIm[0] * leadingIm[0];

        for (var n = 0; n < iterations; n++)
        {
            var (b1Re, b1Im) = DeepOrbitTrapEngine.ComplexPow(zr, zi, powerReal - 1, powerImag, ctx);
            var (d1Re, d1Im) = Scale(b1Re, b1Im, powerReal, powerImag, ctx);
            aux[PowerJuliaAtom<T>.AuxDeriv][n] = Narrow<T>(d1Re, d1Im, ctx);
            var d1r = d1Re.ToDouble();
            var d1i = d1Im.ToDouble();
            expansion[n] = Math.Sqrt(d1r * d1r + d1i * d1i);

            var (b2Re, b2Im) = DeepOrbitTrapEngine.ComplexPow(zr, zi, powerReal - 2, powerImag, ctx);
            var (s2Re, s2Im) = Scale(b2Re, b2Im, powerReal, powerImag, ctx);
            var (t2Re, t2Im) = Scale(s2Re, s2Im, powerReal - 1, powerImag, ctx);
            var halfConstant = BigDecimalCompat.FromDoubleExact(0.5);
            aux[PowerJuliaAtom<T>.AuxDeriv2Half][n] = Narrow<T>(t2Re.Multiply(halfConstant, ctx), t2Im.Multiply(halfConstant, ctx), ctx);

            var (powRe, powIm) = DeepOrbitTrapEngine.ComplexPow(zr, zi, powerReal, powerImag, ctx);
            aux[PowerJuliaAtom<T>.AuxPow][n] = Narrow<T>(powRe, powIm, ctx);

            zr = powRe.Add(kRe, ctx);
            zi = powIm.Add(kIm, ctx);
            z[n + 1] = Narrow<T>(zr, zi, ctx);
            leadingRe[n + 1] = zr.ToDouble();
            leadingIm[n + 1] = zi.ToDouble();
            magnitudeSquared[n + 1] = leadingRe[n + 1] * leadingRe[n + 1] + leadingIm[n + 1] * leadingIm[n + 1];
        }
        expansion[iterations] = expansion[Math.Max(0, iterations - 1)];

        return new ReferenceOrbit<T>
        {
            Z = z,
            Aux = aux,
            MagnitudeSquared = magnitudeSquared,
            ExpansionMagnitude = expansion,
            Length = length,
            Payload = [powerReal, powerImag, juliaRealPayload, juliaImagPayload],
            TableRelativeUlp = T.RelativeUlp,
            Nearest = NearestPointIndex.Build(leadingRe, leadingIm, length),
        };
    }

    static (BigDecimalCompat, BigDecimalCompat) Scale(
        BigDecimalCompat re, BigDecimalCompat im, double sr, double si, MathContextCompat ctx)
    {
        var a = BigDecimalCompat.FromDoubleExact(sr);
        var b = BigDecimalCompat.FromDoubleExact(si);
        return (re.Multiply(a, ctx).Subtract(im.Multiply(b, ctx), ctx),
                re.Multiply(b, ctx).Add(im.Multiply(a, ctx), ctx));
    }

    static Cx<T> Narrow<T>(BigDecimalCompat re, BigDecimalCompat im, MathContextCompat ctx)
        where T : struct, IPrecision<T> => new(NarrowScalar<T>(re, ctx), NarrowScalar<T>(im, ctx));

    static T NarrowScalar<T>(BigDecimalCompat value, MathContextCompat ctx) where T : struct, IPrecision<T>
    {
        Span<double> limbs = stackalloc double[4];
        var remainder = value;
        for (var i = 0; i < 4; i++)
        {
            limbs[i] = remainder.ToDouble();
            if (!double.IsFinite(limbs[i])) { limbs[i] = 0.0; break; }
            remainder = remainder.Subtract(BigDecimalCompat.FromDoubleExact(limbs[i]), ctx);
        }
        var result = T.Zero;
        for (var i = 3; i >= 0; i--) result = T.Add(result, T.FromDouble(limbs[i]));
        return result;
    }

    sealed class BigOrbit
    {
        public required string Name;
        public required BigDecimalCompat[] Re;
        public required BigDecimalCompat[] Im;
        public required double[] Magnitude;
    }

    static BigOrbit RunBigOrbit(
        string name, BigDecimalCompat cRe, BigDecimalCompat cIm, BigDecimalCompat kRe, BigDecimalCompat kIm,
        double powerReal, double powerImag, int steps, int precision)
    {
        var ctx = new MathContextCompat(precision);
        var re = new BigDecimalCompat[steps + 1];
        var im = new BigDecimalCompat[steps + 1];
        var magnitude = new double[steps + 1];
        var zr = cRe;
        var zi = cIm;
        re[0] = zr; im[0] = zi;
        magnitude[0] = Math.Sqrt(zr.ToDouble() * zr.ToDouble() + zi.ToDouble() * zi.ToDouble());
        for (var n = 0; n < steps; n++)
        {
            var (pr, pi) = DeepOrbitTrapEngine.ComplexPow(zr, zi, powerReal, powerImag, ctx);
            zr = pr.Add(kRe, ctx);
            zi = pi.Add(kIm, ctx);
            re[n + 1] = zr; im[n + 1] = zi;
            var r = zr.ToDouble();
            var i = zi.ToDouble();
            magnitude[n + 1] = Math.Sqrt(r * r + i * i);
        }
        return new BigOrbit { Name = name, Re = re, Im = im, Magnitude = magnitude };
    }

    static void ReferenceOrbitAudit(
        BigDecimalCompat cRe, BigDecimalCompat cIm, double kReDouble, double kImDouble,
        double powerReal, double powerImag, int steps)
    {
        Console.WriteLine($"=== reference-orbit audit ({steps} steps) ===");
        var kRounded = (BigDecimalCompat.FromDouble(kReDouble), BigDecimalCompat.FromDouble(kImDouble));
        var kExact = (BigDecimalCompat.FromDoubleExact(kReDouble), BigDecimalCompat.FromDoubleExact(kImDouble));
        Console.WriteLine($"  K rounded (FromDouble, used by DeepOrbitTrapEngine.Render callers) = {kRounded.Item1.ToPlainString()}, {kRounded.Item2.ToPlainString()}");
        Console.WriteLine($"  K exact   (FromDoubleExact, used by PowerJuliaBaker.Bake)          = {kExact.Item1.ToPlainString()}, {kExact.Item2.ToPlainString()}");

        var truthRounded = RunBigOrbit("truth300/Krounded", cRe, cIm, kRounded.Item1, kRounded.Item2, powerReal, powerImag, steps, 300);
        var truthExact = RunBigOrbit("truth300/Kexact", cRe, cIm, kExact.Item1, kExact.Item2, powerReal, powerImag, steps, 300);
        var oldStyle = RunBigOrbit("prec117/Krounded", cRe, cIm, kRounded.Item1, kRounded.Item2, powerReal, powerImag, steps, 117);
        var bakeStyle = RunBigOrbit("prec137/Kexact", cRe, cIm, kExact.Item1, kExact.Item2, powerReal, powerImag, steps, 137);
        var bakePrecRounded = RunBigOrbit("prec137/Krounded", cRe, cIm, kRounded.Item1, kRounded.Item2, powerReal, powerImag, steps, 137);

        foreach (var o in new[] { truthRounded, truthExact, oldStyle, bakeStyle, bakePrecRounded })
            Console.WriteLine($"  {o.Name,-20} escapeIteration={FirstAboveMagnitude(o.Magnitude, 32.0),4}");

        Divergence(truthRounded, oldStyle);
        Divergence(truthRounded, bakePrecRounded);
        Divergence(truthExact, bakeStyle);
        Divergence(truthRounded, truthExact);
        Divergence(oldStyle, bakeStyle);

        Console.WriteLine("    n |  |Z| prec117/Krounded |   |Z| prec137/Kexact |    |Z| truth/Krounded |      |Z| truth/Kexact");
        for (var n = 0; n <= steps; n++)
        {
            if (n < steps - 60 && n % 10 != 0) continue;
            Console.WriteLine($"  {n,3} | {oldStyle.Magnitude[n],21:E6} | {bakeStyle.Magnitude[n],20:E6} | {truthRounded.Magnitude[n],21:E6} | {truthExact.Magnitude[n],21:E6}");
        }
    }

    static int FirstAboveMagnitude(double[] magnitude, double bound)
    {
        for (var i = 0; i < magnitude.Length; i++) if (magnitude[i] > bound) return i;
        return -1;
    }

    static void Divergence(BigOrbit a, BigOrbit b)
    {
        var ctx = new MathContextCompat(60);
        var n = Math.Min(a.Re.Length, b.Re.Length);
        var firstVisible = -1;
        var sb = new StringBuilder();
        sb.Append($"  divergence {a.Name} vs {b.Name}: ");
        for (var i = 0; i < n; i++)
        {
            var dr = a.Re[i].Subtract(b.Re[i], ctx).ToDouble();
            var di = a.Im[i].Subtract(b.Im[i], ctx).ToDouble();
            var d = Math.Sqrt(dr * dr + di * di);
            var scale = Math.Max(a.Magnitude[i], 1e-300);
            if (firstVisible < 0 && d / scale > 1e-6) firstVisible = i;
            if (i % 20 == 0 || i == n - 1) sb.Append($"n{i}={d:E1} ");
        }
        sb.Append($"| firstRelative>1e-6 at n={firstVisible}");
        Console.WriteLine(sb.ToString());
    }

    static void Report(TraceResult t)
    {
        Console.WriteLine($"  {t.Name,-18} firstDecoupled={t.FirstDecoupled,4} decoupledSteps={t.DecoupledSteps,4} " +
                          $"end={t.TerminatedAt,4} reason={t.Reason,-24} trap={t.Trap,12:G8} " +
                          $"renormLarge={t.RenormLargeCount} renormSmall={t.RenormSmallCount} rebases={t.Rebases}");
    }

    static void Compare(TraceResult a, TraceResult b)
    {
        var n = Math.Min(a.Steps.Count, b.Steps.Count);
        var scaleDivergence = -1;
        var uDivergence = -1;
        for (var i = 0; i < n; i++)
        {
            if (scaleDivergence < 0 && a.Steps[i].ScaleExpBefore != b.Steps[i].ScaleExpBefore) scaleDivergence = i;
            var ua = a.Steps[i].UMagnitudeBefore;
            var ub = b.Steps[i].UMagnitudeBefore;
            var scale = Math.Max(Math.Abs(ua), Math.Abs(ub));
            if (uDivergence < 0 && scale > 0 && Math.Abs(ua - ub) > 1e-6 * scale) uDivergence = i;
        }
        Console.WriteLine($"    compare {a.Name} vs {b.Name}: sharedSteps={n} firstScaleExpDivergence={scaleDivergence} firstUDivergence={uDivergence}" +
                          (uDivergence >= 0
                              ? $" (|u|A={a.Steps[uDivergence].UMagnitudeBefore:E6} |u|B={b.Steps[uDivergence].UMagnitudeBefore:E6}" +
                                $" scaleExpA={a.Steps[uDivergence].ScaleExpBefore} scaleExpB={b.Steps[uDivergence].ScaleExpBefore})"
                              : ""));
    }

    static void PrintTable(TraceResult oldTrace, TraceResult newDd, TraceResult new32, int rows)
    {
        Console.WriteLine("    n |  OLD sExp     old|u|  dec |   DD sExp      dd|u|  dec |  F32 sExp     f32|u|  dec");
        var count = Math.Min(rows, Math.Max(oldTrace.Steps.Count, Math.Max(newDd.Steps.Count, new32.Steps.Count)));
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            sb.Clear();
            sb.Append($"  {i,3} |");
            Cell(sb, oldTrace, i);
            sb.Append(" |");
            Cell(sb, newDd, i);
            sb.Append(" |");
            Cell(sb, new32, i);
            Console.WriteLine(sb.ToString());
        }
    }

    static void Cell(StringBuilder sb, TraceResult t, int i)
    {
        if (i >= t.Steps.Count) { sb.Append("      ---        ---  ---"); return; }
        var s = t.Steps[i];
        sb.Append($" {s.ScaleExpBefore,8} {s.UMagnitudeBefore,10:E3} {(s.Decoupled ? "  D" : "  .")}");
    }

    static int FirstAbove(double[] magnitudeSquared, int length, double bound)
    {
        for (var i = 0; i < length; i++) if (magnitudeSquared[i] > bound) return i;
        return -1;
    }

    static string ReferenceDivergence(DeepOrbitTrapEngine.DeepReference oldReference, PowerJuliaBaker.BakedOrbit baked)
    {
        var n = Math.Min(oldReference.Length, baked.Length);
        var worst = 0.0;
        var worstAt = -1;
        for (var i = 0; i < n; i++)
        {
            var a = Math.Sqrt(oldReference.MagnitudeSquared[i]);
            var b = Math.Sqrt(baked.MagnitudeSquared[i]);
            var scale = Math.Max(Math.Abs(a), Math.Abs(b));
            if (scale <= 0) continue;
            var relative = Math.Abs(a - b) / scale;
            if (relative > worst) { worst = relative; worstAt = i; }
        }
        return $"worstRelative={worst:E3} at n={worstAt}";
    }

    static double MeanLog2Expansion(PowerJuliaBaker.BakedOrbit baked, int steps)
    {
        var total = 0.0;
        var count = 0;
        for (var i = 0; i < Math.Min(steps, baked.Length); i++)
        {
            var e = baked.ExpansionMagnitude[i];
            if (!(e > 0.0)) continue;
            total += Math.Log2(e);
            count++;
        }
        return count == 0 ? 0.0 : total / count;
    }

    static TraceResult TraceOld(
        DeepOrbitTrapEngine.DeepReference reference, double deltaCReal, double deltaCImag,
        int scaleExp0, int maxIterations, double leafOffset)
    {
        var t = new TraceResult { Name = "OLD(dd)" };
        var delta = new DdComplex(deltaCReal, deltaCImag);
        var scaleExp = scaleExp0;
        var trap = 1e5;
        var limit = Math.Min(maxIterations - 1, reference.Length - 1);
        var nonPerturbativeSteps = 0;

        for (var n = 0; n < limit; n++)
        {
            var previousOffset = DdComplex.ScaleB(delta, scaleExp);
            var zMagSq = reference.MagnitudeSquared[n];
            var uMagSq = zMagSq > 0 ? previousOffset.MagnitudeSquared / zMagSq : 0.0;
            var record = new StepRecord
            {
                N = n,
                ReferenceIndex = n,
                ScaleExpBefore = scaleExp,
                DeltaLeadingBefore = Math.Max(Math.Abs(delta.Re.Hi), Math.Abs(delta.Im.Hi)),
                OffsetMagnitudeBefore = Math.Sqrt(previousOffset.MagnitudeSquared),
                UMagnitudeBefore = Math.Sqrt(uMagSq),
            };

            DeepOrbitTrapEngine.PerturbStep(reference, n, ref delta, ref scaleExp);
            record.Decoupled = DeepOrbitTrapEngine.LastStepLeftPerturbativeRegime;
            record.ScaleExpAfter = scaleExp;
            if (record.Decoupled)
            {
                nonPerturbativeSteps++;
                t.DecoupledSteps++;
                if (t.FirstDecoupled < 0) t.FirstDecoupled = n;
            }

            var v = reference.Z[n + 1] + DdComplex.ScaleB(delta, scaleExp);
            var magnitude = v.MagnitudeSquared;
            record.PointMagnitudeSquared = magnitude;
            t.Steps.Add(record);

            if (magnitude > 1024.0) { t.TerminatedAt = n + 1; t.Reason = "escaped"; t.Trap = trap; return t; }
            if (magnitude < DeepOrbitTrapEngine.GlitchFactor * reference.MagnitudeSquared[n + 1])
            { t.TerminatedAt = n + 1; t.Reason = "glitch:cancellation"; t.Trap = trap; return t; }
            if (nonPerturbativeSteps > DeepOrbitTrapEngine.MaxNonPerturbativeSteps)
            { t.TerminatedAt = n + 1; t.Reason = "glitch:nonPerturbativeBudget"; t.Trap = trap; return t; }

            trap = Math.Min(trap, BStyleColoring.LeafTrapValue(v.Re.ToDouble(), v.Im.ToDouble(), leafOffset));

            var maxAbs = Math.Max(Math.Abs(delta.Re.Hi), Math.Abs(delta.Im.Hi));
            if (maxAbs > RenormLarge)
            {
                var k = Math.ILogB(maxAbs);
                delta = DdComplex.ScaleB(delta, -k);
                scaleExp += k;
                t.RenormLargeCount++;
                record.Event += $"renormLarge(k={k}) ";
                record.ScaleExpAfter = scaleExp;
            }
        }

        t.TerminatedAt = maxIterations;
        t.Reason = "interior";
        t.Trap = trap;
        return t;
    }

    static TraceResult TraceNew<T>(
        ReferenceOrbit<T> orbit, double deltaCReal, double deltaCImag,
        int scaleExp0, int maxIterations, bool rebaseEnabled)
        where T : struct, IPrecision<T>
    {
        var t = new TraceResult { Name = $"NEW({T.RungName},{(rebaseEnabled ? "rebase" : "norebase")})" };
        var delta = Cx<T>.FromDouble(deltaCReal, deltaCImag);
        var scaleExp = scaleExp0;
        var color = ColorState.Fresh(1e5);

        var relativeError = 0.0;
        var referenceIndex = 0;
        var limit = maxIterations - 1;
        var rungUlp = T.RelativeUlp;
        var tableUlp = orbit.TableRelativeUlp;

        for (var iteration = 0; iteration < limit; iteration++)
        {
            if (referenceIndex + 1 >= orbit.Length)
            { t.TerminatedAt = iteration; t.Reason = "escalate:orbitExhausted"; t.Trap = color.TrapMin; t.Color = color; return t; }

            var previousOffset = Cx<T>.ScaleB(delta, scaleExp);
            var previousOffsetMagnitude = Math.Sqrt(previousOffset.MagnitudeSquared);
            var deltaLeading = Math.Max(Math.Abs(delta.Re.HighPart), Math.Abs(delta.Im.HighPart));
            var zMagSq = orbit.MagnitudeSquared[referenceIndex];

            var record = new StepRecord
            {
                N = iteration,
                ReferenceIndex = referenceIndex,
                ScaleExpBefore = scaleExp,
                DeltaLeadingBefore = deltaLeading,
                OffsetMagnitudeBefore = previousOffsetMagnitude,
                UMagnitudeBefore = zMagSq > 0 ? Math.Sqrt(previousOffset.MagnitudeSquared / zMagSq) : 0.0,
            };
            t.Steps.Add(record);

            if (deltaLeading > 0.0 && previousOffsetMagnitude <= 0.0)
            { t.TerminatedAt = iteration; t.Reason = "escalate:offsetUnderflow"; t.Trap = color.TrapMin; t.Color = color; return t; }

            var sourceMagnitude = Math.Sqrt(orbit.MagnitudeSquared[referenceIndex]);
            var sourcePoint = orbit.Z[referenceIndex] + previousOffset;
            var sourcePointMagnitude = Math.Sqrt(sourcePoint.MagnitudeSquared);
            var previousAbsoluteError = relativeError * previousOffsetMagnitude * CapInflation;

            PowerJuliaAtom<T>.Step(orbit, referenceIndex, ref delta, ref scaleExp, out var decoupled, relativeError, out var branchUndecidable);
            record.Decoupled = decoupled;
            record.ScaleExpAfter = scaleExp;
            if (branchUndecidable)
            { t.TerminatedAt = iteration; t.Reason = "escalate:branchUndecidable"; t.Trap = color.TrapMin; t.Color = color; return t; }
            referenceIndex++;
            if (decoupled)
            {
                t.DecoupledSteps++;
                if (t.FirstDecoupled < 0) t.FirstDecoupled = iteration;
            }

            var offset = Cx<T>.ScaleB(delta, scaleExp);
            var offsetMagnitude = Math.Sqrt(offset.MagnitudeSquared);
            var point = orbit.Z[referenceIndex] + offset;
            var re = point.Re.ToDouble();
            var im = point.Im.ToDouble();
            var magnitudeSquared = re * re + im * im;
            var pointMagnitude = Math.Sqrt(magnitudeSquared);
            var referenceMagnitude = Math.Sqrt(orbit.MagnitudeSquared[referenceIndex]);
            record.PointMagnitudeSquared = magnitudeSquared;

            if (decoupled)
            {
                var ballRadius = previousAbsoluteError;
                var innerRadius = sourcePointMagnitude - ballRadius;
                if (!(innerRadius > 0.5 * sourcePointMagnitude))
                { t.TerminatedAt = iteration; t.Reason = "escalate:innerRadius"; t.Trap = color.TrapMin; t.Color = color; return t; }

                var supDerivative = PowerJuliaAtom<T>.DerivativeSupremum(orbit, innerRadius);
                if (!double.IsFinite(supDerivative))
                { t.TerminatedAt = iteration; t.Reason = "escalate:supDerivative"; t.Trap = color.TrapMin; t.Color = color; return t; }

                var denominator = offsetMagnitude - supDerivative * ballRadius;
                if (!(denominator > 0.0))
                { t.TerminatedAt = iteration; t.Reason = "escalate:denominator"; t.Trap = color.TrapMin; t.Color = color; return t; }

                var amplification = supDerivative * previousOffsetMagnitude / denominator;
                relativeError = (relativeError + TableUlps * tableUlp + CoupledArithmeticUlps * rungUlp) * amplification
                                + CoupledArithmeticUlps * rungUlp;
            }
            else
            {
                var u = sourceMagnitude > 0
                    ? (previousOffsetMagnitude + previousAbsoluteError) / sourceMagnitude
                    : 0.0;
                var truncation = PowerJuliaAtom<T>.TruncationCoefficient * u * u;
                relativeError = relativeError * (1.0 + 5.0 * u)
                                + CoupledArithmeticUlps * rungUlp
                                + TableUlps * tableUlp
                                + truncation;
            }

            if (!(relativeError <= RelativeErrorCap))
            { t.TerminatedAt = iteration; t.Reason = "escalate:relativeErrorCap"; t.Trap = color.TrapMin; t.Color = color; return t; }

            var displayError = relativeError * offsetMagnitude * CapInflation
                               + TableUlps * tableUlp * referenceMagnitude
                               + CoupledArithmeticUlps * rungUlp * pointMagnitude
                               + DoubleNarrowingUlp * pointMagnitude;

            color.FinalMagnitudeSquared = magnitudeSquared;
            color.FinalPointError = displayError;

            var bailout = PowerJuliaAtom<T>.BailoutSquared;
            var escapeUncertainty = 2.0 * pointMagnitude * displayError + displayError * displayError;
            var escapeMargin = Math.Abs(magnitudeSquared - bailout);
            if (!(escapeMargin > escapeUncertainty))
            { t.TerminatedAt = iteration; t.Reason = "escalate:escapeMargin"; t.Trap = color.TrapMin; t.Color = color; return t; }

            if (magnitudeSquared > bailout)
            {
                t.TerminatedAt = iteration + 1;
                t.Reason = BStyleTrapAtom.IsCertified(color, iteration + 1) ? "escaped" : "escalate:uncertifiedEscape";
                t.Trap = color.TrapMin; t.Color = color;
                return t;
            }

            BStyleTrapAtom.Accumulate(ref color, re, im, displayError);

            if (rebaseEnabled && decoupled && offsetMagnitude > 0.0)
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
                        record.Event += $"rebase({referenceIndex}->{candidate}) ";
                        referenceIndex = candidate;
                        t.Rebases++;
                        relativeError = relativeError * (offsetMagnitude / candidateDistance)
                                        + (2.0 * TableUlps * tableUlp + CoupledArithmeticUlps * rungUlp)
                                          * pointMagnitude / candidateDistance;
                        if (!(relativeError <= RelativeErrorCap))
                        { t.TerminatedAt = iteration; t.Reason = "escalate:rebaseError"; t.Trap = color.TrapMin; t.Color = color; return t; }
                    }
                }
            }

            var leading = Math.Max(Math.Abs(delta.Re.HighPart), Math.Abs(delta.Im.HighPart));
            if (leading > RenormLarge || (leading > 0.0 && leading < RenormSmall))
            {
                var k = Math.ILogB(leading);
                delta = Cx<T>.ScaleB(delta, -k);
                scaleExp += k;
                if (leading > RenormLarge) t.RenormLargeCount++; else t.RenormSmallCount++;
                record.Event += $"{(leading > RenormLarge ? "renormLarge" : "renormSmall")}(k={k},leading={leading:E3}) ";
            }
            record.ScaleExpAfter = scaleExp;
        }

        t.TerminatedAt = maxIterations;
        t.Reason = BStyleTrapAtom.IsCertified(color, maxIterations) ? "interior" : "escalate:uncertifiedInterior";
        t.Trap = color.TrapMin; t.Color = color;
        return t;
    }
}
