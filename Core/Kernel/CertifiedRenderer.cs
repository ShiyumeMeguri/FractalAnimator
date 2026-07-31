using FractalAnimator.Core.Atoms;

namespace FractalAnimator.Core.Kernel;

public sealed class RenderStats
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required double Zooms { get; init; }
    public required int MaxIterations { get; init; }
    public required int[] RetiredPerRung { get; init; }
    public required string[] RungNames { get; init; }
    public required double[] RungMilliseconds { get; init; }
    public int Uncertified { get; set; }
    public long TotalDecoupledSteps { get; set; }
    public long TotalRebases { get; set; }
    public double BakeMilliseconds { get; set; }
    public double TotalMilliseconds { get; set; }

    public int TotalPixels => Width * Height;
}

public static class CertifiedRenderer
{
    public static RenderStats Render(
        PowerJuliaBaker.BakedOrbit baked, double zooms, int width, int height, int maxIterations,
        float[] outputRgb, CancellationToken token = default)
    {
        var spacingLog2 = 2.0 - zooms;
        var scaleExp = (int)Math.Floor(spacingLog2);
        var mantissa = Math.Pow(2.0, spacingLog2 - scaleExp);
        var offsetToDelta = mantissa / width;
        var centerX = width / 2.0;
        var centerY = height / 2.0;

        var rungNames = new[] { "FP32", "FF32", "FP64", "DD", "QD", "AP" };
        var stats = new RenderStats
        {
            Width = width, Height = height, Zooms = zooms, MaxIterations = maxIterations,
            RetiredPerRung = new int[rungNames.Length],
            RungNames = rungNames,
            RungMilliseconds = new double[rungNames.Length],
        };

        var pending = new int[width * height];
        for (var i = 0; i < pending.Length; i++) pending[i] = i;
        var pendingCount = pending.Length;

        var total = System.Diagnostics.Stopwatch.StartNew();
        pendingCount = RunRung<Fp32>(baked, 0, pending, pendingCount, width, offsetToDelta, centerX, centerY, scaleExp, maxIterations, outputRgb, stats, token);
        pendingCount = RunRung<FloatFloat>(baked, 1, pending, pendingCount, width, offsetToDelta, centerX, centerY, scaleExp, maxIterations, outputRgb, stats, token);
        pendingCount = RunRung<Fp64>(baked, 2, pending, pendingCount, width, offsetToDelta, centerX, centerY, scaleExp, maxIterations, outputRgb, stats, token);
        pendingCount = RunRung<DoubleDouble>(baked, 3, pending, pendingCount, width, offsetToDelta, centerX, centerY, scaleExp, maxIterations, outputRgb, stats, token);
        pendingCount = RunRung<QuadDouble>(baked, 4, pending, pendingCount, width, offsetToDelta, centerX, centerY, scaleExp, maxIterations, outputRgb, stats, token);
        pendingCount = RunArbitraryPrecision(baked, pending, pendingCount, width, offsetToDelta, centerX, centerY, scaleExp, maxIterations, outputRgb, stats, token);
        total.Stop();

        stats.Uncertified = pendingCount;
        stats.TotalMilliseconds = total.Elapsed.TotalMilliseconds;
        return stats;
    }

    static int RunArbitraryPrecision(
        PowerJuliaBaker.BakedOrbit baked, int[] pending, int pendingCount, int width,
        double offsetToDelta, double centerX, double centerY, int scaleExp, int maxIterations,
        float[] outputRgb, RenderStats stats, CancellationToken token)
    {
        if (pendingCount == 0) return 0;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var survived = new bool[pendingCount];
        var centerReal = baked.CenterReal;
        var centerImag = baked.CenterImag;
        var juliaReal = baked.JuliaReal;
        var juliaImag = baked.JuliaImag;
        var powerReal = baked.Payload[PowerJuliaAtom<DoubleDouble>.PayloadPowerReal];
        var powerImag = baked.Payload[PowerJuliaAtom<DoubleDouble>.PayloadPowerImag];

        Parallel.For(0, pendingCount, new ParallelOptions { CancellationToken = token }, k =>
        {
            var index = pending[k];
            var x = index % width;
            var y = index / width;
            var ok = ArbitraryPrecisionFallback.TryResolve(
                centerReal, centerImag, juliaReal, juliaImag, powerReal, powerImag,
                (x - centerX) * offsetToDelta, (centerY - y) * offsetToDelta, scaleExp,
                maxIterations, baked.LeafOffset,
                out var r, out var g, out var b, out _);
            if (!ok) { survived[k] = true; return; }
            var o = index * 3;
            outputRgb[o + 0] = (float)r;
            outputRgb[o + 1] = (float)g;
            outputRgb[o + 2] = (float)b;
        });

        var remaining = 0;
        for (var k = 0; k < pendingCount; k++)
            if (survived[k]) pending[remaining++] = pending[k];

        sw.Stop();
        stats.RetiredPerRung[5] = pendingCount - remaining;
        stats.RungMilliseconds[5] = sw.Elapsed.TotalMilliseconds;
        return remaining;
    }

    static int RunRung<T>(
        PowerJuliaBaker.BakedOrbit baked, int rung, int[] pending, int pendingCount, int width,
        double offsetToDelta, double centerX, double centerY, int scaleExp, int maxIterations,
        float[] outputRgb, RenderStats stats, CancellationToken token)
        where T : struct, IPrecision<T>
    {
        if (pendingCount == 0) return 0;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var orbit = PowerJuliaBaker.Narrow<T>(baked);

        var survived = new bool[pendingCount];
        var decoupled = new long[Environment.ProcessorCount];
        var rebases = new long[Environment.ProcessorCount];

        var magnitude = CertifiedIteratorBatch.Magnitudes(orbit.MagnitudeSquared, orbit.Length);
        var batchSize = Math.Max(1, CertifiedIteratorBatch.BatchSize);

        var partitioner = System.Collections.Concurrent.Partitioner.Create(0, pendingCount);
        Parallel.ForEach(partitioner, new ParallelOptions { CancellationToken = token }, range =>
        {
            var slot = Environment.CurrentManagedThreadId % decoupled.Length;
            long localDecoupled = 0, localRebases = 0;
            var deltas = new Cx<T>[batchSize];
            var batchResults = new PixelResult[batchSize];

            for (var k0 = range.Item1; k0 < range.Item2; k0 += batchSize)
            {
                var take = Math.Min(batchSize, range.Item2 - k0);
                for (var j = 0; j < take; j++)
                {
                    var index = pending[k0 + j];
                    var x = index % width;
                    var y = index / width;
                    deltas[j] = Cx<T>.FromDouble((x - centerX) * offsetToDelta, (centerY - y) * offsetToDelta);
                }

                CertifiedIteratorBatch<T, PowerJuliaAtom<T>, BStyleTrapAtom>
                    .Iterate(orbit, magnitude, deltas, take, scaleExp, maxIterations, batchResults);

                for (var j = 0; j < take; j++)
                {
                    var k = k0 + j;
                    var index = pending[k];
                    var result = batchResults[j];

                    localDecoupled += result.DecoupledSteps;
                    localRebases += result.Rebases;

                    if (result.Verdict == PixelVerdict.NeedsHigherPrecision) { survived[k] = true; continue; }

                    var (r, g, b) = BStyleTrapAtom.Resolve(result.Color, result.Iterations);
                    var o = index * 3;
                    outputRgb[o + 0] = (float)r;
                    outputRgb[o + 1] = (float)g;
                    outputRgb[o + 2] = (float)b;
                }
            }
            Interlocked.Add(ref decoupled[slot], localDecoupled);
            Interlocked.Add(ref rebases[slot], localRebases);
        });

        var remaining = 0;
        for (var k = 0; k < pendingCount; k++)
            if (survived[k]) pending[remaining++] = pending[k];

        sw.Stop();
        stats.RetiredPerRung[rung] = pendingCount - remaining;
        stats.RungMilliseconds[rung] = sw.Elapsed.TotalMilliseconds;
        foreach (var d in decoupled) stats.TotalDecoupledSteps += d;
        foreach (var r in rebases) stats.TotalRebases += r;
        return remaining;
    }
}
