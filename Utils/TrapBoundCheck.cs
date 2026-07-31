using FractalAnimator.Core.Atoms;
using FractalAnimator.Core.Kernel;

namespace FractalAnimator.Diagnostics;

public static class TrapBoundCheck
{
    public static int Run(int samples = 4000000, int seed = 20260730)
    {
        var roundingFailures = Interval.VerifyDirectedRounding();
        Console.WriteLine($"directed rounding: {roundingFailures} disagreements with Math.BitDecrement/BitIncrement");

        var rng = new Random(seed);
        var violations = 0;
        long pruned = 0, evaluated = 0;
        var tightest = double.PositiveInfinity;

        for (var i = 0; i < samples; i++)
        {
            var scale = Math.Pow(10.0, rng.NextDouble() * 8.0 - 5.0);
            var re = (rng.NextDouble() * 2.0 - 1.0) * scale;
            var im = (rng.NextDouble() * 2.0 - 1.0) * scale;
            var err = Math.Pow(10.0, rng.NextDouble() * 14.0 - 20.0) * Math.Max(scale, 1e-6);

            var seedTrap = SampleTrapHi(rng);
            var state = ColorState.Fresh(seedTrap);
            var reference = ColorState.Fresh(seedTrap);

            var radiusSquared = BStyleTrapAtom.RadiusSquaredForTest(re, im, err);
            if (radiusSquared.IsEmpty) continue;

            var excluded = BStyleTrapAtom.ExcludedForTest(ref state, radiusSquared);

            var trap = BStyleTrapAtom.TrapIntervalForTest(re, im, err);
            if (trap.IsEmpty) continue;

            reference.TrapLo = Math.Min(reference.TrapLo, trap.Lo);
            reference.TrapHi = Math.Min(reference.TrapHi, trap.Hi);

            if (excluded)
            {
                pruned++;
                if (reference.TrapHi != seedTrap)
                {
                    violations++;
                    if (violations <= 5)
                        Console.WriteLine($"  VIOLATION pruned but min improved: re={re:E3} im={im:E3} err={err:E3} " +
                                          $"seedHi={seedTrap:G17} trap=[{trap.Lo:G17},{trap.Hi:G17}]");
                }
                var slack = trap.Lo - seedTrap;
                if (slack < tightest) tightest = slack;
            }
            else evaluated++;
        }

        var total = pruned + evaluated;
        Console.WriteLine($"trap prune: {total} samples, {violations} violations, " +
                          $"pruneRate={(total > 0 ? 100.0 * pruned / total : 0):0.00}% tightestSlack={tightest:E3}");
        return violations == 0 && roundingFailures == 0 ? 0 : 1;
    }

    static double SampleTrapHi(Random rng)
    {
        var roll = rng.NextDouble();
        if (roll < 0.55) return -3.3 + rng.NextDouble() * 0.25;
        if (roll < 0.75) return -3.22 + rng.NextDouble() * 0.02;
        if (roll < 0.9) return rng.NextDouble() * 6.0 - 3.0;
        return 1e5;
    }
}
