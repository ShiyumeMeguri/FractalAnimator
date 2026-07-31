using System.Diagnostics;
using System.Globalization;
using System.Text;
using FractalAnimator.Core;

namespace FractalAnimator;

public readonly record struct BenchResult
{
    public required string Scenario { get; init; }

    public required double Zooms { get; init; }

    public required int Resolution { get; init; }

    public required int Iterations { get; init; }

    public required double WallMilliseconds { get; init; }

    public required int ReferenceBuilds { get; init; }

    public required int InitialGlitches { get; init; }

    public required int DistinctColors { get; init; }

    public required ulong PixelHash { get; init; }

    public required long ManagedMemoryDeltaBytes { get; init; }

    public required long AllocatedBytes { get; init; }

    public required int Gen0Collections { get; init; }
}

public readonly record struct BenchScenario(string Name, double Zooms, string Why);

public static class FractalStressHarness
{
    public const string DeepCenterReal =
        "1.69606094102335990831239662074185263380097653556446974121776163540637298024853";

    public const string DeepCenterImag =
        "2.08049902493822965286894308802169375262087436820149099900777715195242489638537";

    public static readonly IReadOnlyList<BenchScenario> ScenarioLadder =
    [
        new("shallow", 0.0, "double regime; Taylor fast path only, repair never fires"),

        new("double-wall", 45.0, "~10^14: direct double dies; perturbation becomes load-bearing"),

        new("dead-band", 60.0, "former void band; exact step + branch-cut path carry the result"),

        new("mid-deep", 120.0, "shared with --pixeltruth/--glitchthresholdsweep for escalation"),

        new("deep", 200.0, "reference-orbit precision dominates; delta rescaling exercised hard"),

        new("very-deep", 300.0, "~190-digit reference; BigDecimal cost and repair budget at their worst"),

    ];

    static readonly BigDecimalCompat CenterReal = BigDecimalCompat.Parse(DeepCenterReal);
    static readonly BigDecimalCompat CenterImag = BigDecimalCompat.Parse(DeepCenterImag);

    const string GlitchLogMarker = "initial glitches: ";

    public static IReadOnlyList<BenchResult> RunSuite(int resolution, int iterations,
        Action<BenchResult>? onScenarioComplete = null, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(resolution, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 2);

        var results = new List<BenchResult>(ScenarioLadder.Count);
        var originalOut = Console.Out;
        var previousDebug = DeepOrbitTrapEngine.DebugLogging;
        DeepOrbitTrapEngine.DebugLogging = true;
        var capture = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(capture);
            WarmUp(iterations, token);
            Console.SetOut(originalOut);

            foreach (var scenario in ScenarioLadder)
            {
                token.ThrowIfCancellationRequested();
                var result = Measure(scenario.Name, scenario.Zooms, resolution, iterations, capture,
                    originalOut, token);
                results.Add(result);
                onScenarioComplete?.Invoke(result);
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            DeepOrbitTrapEngine.DebugLogging = previousDebug;
        }
        return results;
    }

    public static BenchResult RunScenario(string name, double zooms, int resolution, int iterations,
        CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(resolution, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 2);

        var originalOut = Console.Out;
        var previousDebug = DeepOrbitTrapEngine.DebugLogging;
        DeepOrbitTrapEngine.DebugLogging = true;
        var capture = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            return Measure(name, zooms, resolution, iterations, capture, originalOut, token);
        }
        finally
        {
            Console.SetOut(originalOut);
            DeepOrbitTrapEngine.DebugLogging = previousDebug;
        }
    }

    static BenchResult Measure(string name, double zooms, int resolution, int iterations,
        StringWriter capture, TextWriter originalOut, CancellationToken token)
    {
        var p = SelfTest.CorruptionParams();
        p.Iterations = iterations;
        var juliaReal = BigDecimalCompat.FromDouble(p.JuliaCXRaw * 1e-5);
        var juliaImag = BigDecimalCompat.FromDouble(p.JuliaCYRaw * 1e-5);

        var rgb = new float[resolution * resolution * 3];
        capture.GetStringBuilder().Clear();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        var managedBefore = GC.GetTotalMemory(forceFullCollection: false);
        var gen0Before = GC.CollectionCount(0);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        DeepOrbitTrapEngine.RepairReferenceBuilds = 0;

        Console.SetOut(capture);
        var sw = Stopwatch.StartNew();
        DeepOrbitTrapEngine.Render(CenterReal, CenterImag, juliaReal, juliaImag,
            p.PowerReal, p.PowerImag, zooms, resolution, resolution, iterations,
            p.LeafOrbitOffset, p.ColorR, p.ColorG, p.ColorB, rgb, token);
        sw.Stop();
        Console.SetOut(originalOut);

        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        var gen0After = GC.CollectionCount(0);
        var managedAfter = GC.GetTotalMemory(forceFullCollection: false);

        var allocated = allocatedAfter >= allocatedBefore ? allocatedAfter - allocatedBefore : -1L;

        return new BenchResult
        {
            Scenario = name,
            Zooms = zooms,
            Resolution = resolution,
            Iterations = iterations,
            WallMilliseconds = sw.Elapsed.TotalMilliseconds,
            ReferenceBuilds = DeepOrbitTrapEngine.RepairReferenceBuilds,
            InitialGlitches = ParseInitialGlitches(capture.GetStringBuilder().ToString()),
            DistinctColors = CountDistinctColors(rgb, resolution * resolution),
            PixelHash = PixelFingerprint(rgb),
            ManagedMemoryDeltaBytes = managedAfter - managedBefore,
            AllocatedBytes = allocated,
            Gen0Collections = gen0After - gen0Before,
        };
    }

    static void WarmUp(int iterations, CancellationToken token)
    {
        const int warmSize = 64;
        var p = SelfTest.CorruptionParams();
        var juliaReal = BigDecimalCompat.FromDouble(p.JuliaCXRaw * 1e-5);
        var juliaImag = BigDecimalCompat.FromDouble(p.JuliaCYRaw * 1e-5);
        var rgb = new float[warmSize * warmSize * 3];
        foreach (var zooms in new[] { 0.0, 120.0 })
        {
            DeepOrbitTrapEngine.Render(CenterReal, CenterImag, juliaReal, juliaImag,
                p.PowerReal, p.PowerImag, zooms, warmSize, warmSize, iterations,
                p.LeafOrbitOffset, p.ColorR, p.ColorG, p.ColorB, rgb, token);
        }
        DeepOrbitTrapEngine.RepairReferenceBuilds = 0;
    }

    public static string FormatReport(IReadOnlyList<BenchResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.AppendLine("FractalStressHarness — canonical deep-zoom acceptance ladder");
        sb.AppendLine($"  centre   re = {DeepCenterReal}");
        sb.AppendLine($"           im = {DeepCenterImag}");
        sb.AppendFormat(ci, "  engine   GlitchFactor={0:E0}  RepairTileSize={1} (floor {2})  MaxNonPerturbativeSteps={3}",
            DeepOrbitTrapEngine.GlitchFactor, DeepOrbitTrapEngine.RepairTileSize,
            DeepOrbitTrapEngine.MinRepairTileSize, DeepOrbitTrapEngine.MaxNonPerturbativeSteps).AppendLine();
        sb.AppendFormat(ci, "  machine  {0} logical cores  ServerGC={1}  {2}",
            Environment.ProcessorCount, System.Runtime.GCSettings.IsServerGC,
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription).AppendLine();
        sb.AppendLine("  WARNING  the ladder is frozen. Changing a rung, the centre, or CorruptionParams()");
        sb.AppendLine("           invalidates EVERY prior baseline — re-measure the whole ladder, never mix rows.");
        sb.AppendLine();

        const string rule =
            "-----------------------------------------------------------------------------------------------------------------------------";
        sb.AppendFormat(ci, "{0,-12} {1,7} {2,6} {3,6} {4,12} {5,6} {6,8} {7,8} {8,20} {9,12} {10,12} {11,5}",
            "Scenario", "Zooms", "Res", "Iters", "Wall(ms)", "Refs", "Glitches", "Colors", "PixelHash",
            "HeapDelta", "Allocated", "GCs").AppendLine();
        sb.AppendLine(rule);

        double totalMs = 0;
        long totalRefs = 0, totalGlitches = 0, totalAlloc = 0, totalGcs = 0;
        bool glitchesKnown = true, allocKnown = true;
        foreach (var r in results)
        {
            sb.AppendFormat(ci, "{0,-12} {1,7:0.0} {2,6} {3,6} {4,12:0.0} {5,6} {6,8} {7,8} {8,20} {9,12} {10,12} {11,5}",
                r.Scenario, r.Zooms, r.Resolution, r.Iterations, r.WallMilliseconds, r.ReferenceBuilds,
                r.InitialGlitches < 0 ? "n/a" : r.InitialGlitches.ToString(ci),
                r.DistinctColors, "0x" + r.PixelHash.ToString("X16", ci),
                FormatBytes(r.ManagedMemoryDeltaBytes, signed: true),
                r.AllocatedBytes < 0 ? "n/a" : FormatBytes(r.AllocatedBytes, signed: false),
                r.Gen0Collections).AppendLine();
            totalMs += r.WallMilliseconds;
            totalRefs += r.ReferenceBuilds;
            totalGcs += r.Gen0Collections;
            if (r.InitialGlitches < 0) glitchesKnown = false; else totalGlitches += r.InitialGlitches;
            if (r.AllocatedBytes < 0) allocKnown = false; else totalAlloc += r.AllocatedBytes;
        }

        sb.AppendLine(rule);
        sb.AppendFormat(ci, "{0,-12} {1,7} {2,6} {3,6} {4,12:0.0} {5,6} {6,8} {7,8} {8,20} {9,12} {10,12} {11,5}",
            "TOTAL", "", "", "", totalMs, totalRefs, glitchesKnown ? totalGlitches.ToString(ci) : "n/a",
            "", "", "", allocKnown ? FormatBytes(totalAlloc, signed: false) : "n/a", totalGcs).AppendLine();
        sb.AppendFormat(ci, "{0,-12} {1:0.000} s wall across {2} scenarios", "", totalMs / 1000.0, results.Count)
            .AppendLine();
        return sb.ToString();
    }

    static int ParseInitialGlitches(string log)
    {
        var start = log.IndexOf(GlitchLogMarker, StringComparison.Ordinal);
        if (start < 0) return -1;
        start += GlitchLogMarker.Length;
        var end = start;
        while (end < log.Length && char.IsAsciiDigit(log[end])) end++;
        return end > start && int.TryParse(log.AsSpan(start, end - start), NumberStyles.None,
            CultureInfo.InvariantCulture, out var n) ? n : -1;
    }

    static int CountDistinctColors(float[] rgb, int pixelCount)
    {
        var seen = new HashSet<int>();
        for (var i = 0; i < pixelCount; i++)
        {
            var j = i * 3;
            seen.Add((ToByte(rgb[j]) << 16) | (ToByte(rgb[j + 1]) << 8) | ToByte(rgb[j + 2]));
        }
        return seen.Count;
    }

    static ulong PixelFingerprint(float[] rgb)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in rgb)
        {
            hash ^= ToByte(c);
            hash *= 1099511628211UL;
        }
        return hash;
    }

    static byte ToByte(double c) =>
        double.IsNaN(c) ? (byte)0 : (byte)Math.Clamp((int)Math.Round(c * 255.0), 0, 255);

    static string FormatBytes(long bytes, bool signed)
    {
        var sign = signed && bytes >= 0 ? "+" : bytes < 0 ? "-" : "";
        var v = (double)Math.Abs(bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var u = 0;
        while (v >= 1024.0 && u < units.Length - 1) { v /= 1024.0; u++; }
        return string.Format(CultureInfo.InvariantCulture, u == 0 ? "{0}{1:0} {2}" : "{0}{1:0.0} {2}",
            sign, v, units[u]);
    }
}
