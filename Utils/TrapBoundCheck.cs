using FractalAnimator.Core.Atoms;

namespace FractalAnimator.Diagnostics;

public static class TrapBoundCheck
{
    public static int Run(int samples = 400000, int seed = 20260730)
    {
        var rng = new Random(seed);
        var violations = 0;
        var worstSlack = double.PositiveInfinity;
        var checkedCount = 0;

        for (var i = 0; i < samples; i++)
        {
            var scale = Math.Pow(10.0, rng.NextDouble() * 8.0 - 5.0);
            var re = (rng.NextDouble() * 2.0 - 1.0) * scale;
            var im = (rng.NextDouble() * 2.0 - 1.0) * scale;
            var err = Math.Pow(10.0, rng.NextDouble() * 14.0 - 20.0) * Math.Max(scale, 1e-6);

            var exact = BStyleTrapAtom.TrapIntervalForTest(re, im, err);
            if (exact.IsEmpty) continue;
            var bound = BStyleTrapAtom.TrapLowerBoundForTest(re, im, err);
            checkedCount++;

            if (bound > exact.Lo)
            {
                violations++;
                if (violations <= 5)
                    Console.WriteLine($"  VIOLATION re={re:E3} im={im:E3} err={err:E3} bound={bound:G17} intervalLo={exact.Lo:G17}");
            }
            var slack = exact.Lo - bound;
            if (slack < worstSlack) worstSlack = slack;
        }

        Console.WriteLine($"trap lower bound: {checkedCount} samples, {violations} violations, tightest slack={worstSlack:E3}");
        return violations == 0 ? 0 : 1;
    }
}
