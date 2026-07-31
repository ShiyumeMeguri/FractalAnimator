using System.Globalization;
using FractalAnimator.Core;
using FractalAnimator.Core.Atoms;
using FractalAnimator.Core.Kernel;

namespace FractalAnimator.Diagnostics;

public static class TrajectoryDiff
{
    public static int Run(string[] args)
    {
        var zooms = Arg(args, 1, 60.0);
        var size = (int)Arg(args, 2, 200.0);
        var iterations = (int)Arg(args, 3, 300.0);
        var px = (int)Arg(args, 4, 50.0);
        var py = (int)Arg(args, 5, 50.0);
        var steps = (int)Arg(args, 6, 200.0);

        var cx = BigDecimalCompat.Parse("1.69606094102335990831239662074185263380097653556446974121776163540637298024853");
        var cy = BigDecimalCompat.Parse("2.08049902493822965286894308802169375262087436820149099900777715195242489638537");

        const double powerReal = -2.23;
        const double powerImag = 0.0;
        const double leafOffset = 0.59;
        var kReDouble = -77781 * 1e-5;
        var kImDouble = -23656 * 1e-5;

        var kReRound = BigDecimalCompat.FromDouble(kReDouble);
        var kImRound = BigDecimalCompat.FromDouble(kImDouble);
        var kReExact = BigDecimalCompat.FromDoubleExact(kReDouble);
        var kImExact = BigDecimalCompat.FromDoubleExact(kImDouble);

        Console.WriteLine($"zooms={zooms} size={size} iterations={iterations} pixel=({px},{py})");
        Console.WriteLine($"K as OLD engine builds it  (FromDouble)      re = {kReRound.ToPlainString()}");
        Console.WriteLine($"K as NEW baker builds it   (FromDoubleExact) re = {kReExact.ToPlainString()}");
        Console.WriteLine($"K as OLD engine builds it  (FromDouble)      im = {kImRound.ToPlainString()}");
        Console.WriteLine($"K as NEW baker builds it   (FromDoubleExact) im = {kImExact.ToPlainString()}");
        var kDiffRe = kReExact.Subtract(kReRound, new MathContextCompat(60)).ToDouble();
        var kDiffIm = kImExact.Subtract(kImRound, new MathContextCompat(60)).ToDouble();
        Console.WriteLine($"K difference (exact-rounded) = ({kDiffRe:E6}, {kDiffIm:E6})");

        var spacingLog2 = 2.0 - zooms;
        var scaleExp0 = (int)Math.Floor(spacingLog2);
        var mantissa = Math.Pow(2.0, spacingLog2 - scaleExp0);
        var offsetToDelta = mantissa / size;
        var half = size / 2.0;
        var deltaCRe = (px - half) * offsetToDelta;
        var deltaCIm = (half - py) * offsetToDelta;
        var pixelPhysical = offsetToDelta * Math.ScaleB(1.0, scaleExp0);
        Console.WriteLine($"scaleExp={scaleExp0} offsetToDelta={offsetToDelta:E6} deltaC=({deltaCRe:E6},{deltaCIm:E6})");
        Console.WriteLine($"physical pixel size={pixelPhysical:E6}  view width={pixelPhysical * size:E6}");
        Console.WriteLine();

        var oldRound = DeepOrbitTrapEngine.BuildReference(cx, cy, kReRound, kImRound, powerReal, powerImag, zooms, iterations, default);
        var oldExact = DeepOrbitTrapEngine.BuildReference(cx, cy, kReExact, kImExact, powerReal, powerImag, zooms, iterations, default);
        var baked = PowerJuliaBaker.Bake(cx, cy, kReDouble, kImDouble, powerReal, powerImag, iterations);
        var newOrbit = PowerJuliaBaker.Narrow<DoubleDouble>(baked);

        Console.WriteLine("reference orbit head: OLD(FromDouble K) vs NEW baker");
        Console.WriteLine($"{"n",3}  {"old.Re",26} {"old.Im",26}  {"new.Re",26} {"new.Im",26}  {"|Zold-Znew|",12}");
        for (var n = 0; n < 8; n++)
        {
            var zo = oldRound.Z[n];
            var zn = newOrbit.Z[n];
            var dr = zo.Re.ToDouble() - zn.Re.ToDouble();
            var di = zo.Im.ToDouble() - zn.Im.ToDouble();
            Console.WriteLine($"{n,3}  {zo.Re.ToDouble(),26:F20} {zo.Im.ToDouble(),26:F20}  {zn.Re.ToDouble(),26:F20} {zn.Im.ToDouble(),26:F20}  {Math.Sqrt(dr * dr + di * di),12:E3}");
        }
        Console.WriteLine();

        Console.WriteLine("reference orbit head: OLD(FromDoubleExact K) vs NEW baker");
        Console.WriteLine($"{"n",3}  {"|Zoldexact-Znew|",18}");
        for (var n = 0; n < 8; n++)
        {
            var zo = oldExact.Z[n];
            var zn = newOrbit.Z[n];
            var dr = zo.Re.ToDouble() - zn.Re.ToDouble();
            var di = zo.Im.ToDouble() - zn.Im.ToDouble();
            Console.WriteLine($"{n,3}  {Math.Sqrt(dr * dr + di * di),18:E3}");
        }
        Console.WriteLine();

        Console.WriteLine("reference orbit separation over the whole run (old FromDouble K vs new baker K)");
        Console.WriteLine($"{"n",4}  {"|Zold|",12} {"|Znew|",12}  {"|Zold-Znew|",12}");
        for (var n = 0; n <= Math.Min(iterations, steps); n += Math.Max(1, Math.Min(iterations, steps) / 40))
        {
            var zo = oldRound.Z[n];
            var zn = newOrbit.Z[n];
            var dr = zo.Re.ToDouble() - zn.Re.ToDouble();
            var di = zo.Im.ToDouble() - zn.Im.ToDouble();
            Console.WriteLine($"{n,4}  {Math.Sqrt(oldRound.MagnitudeSquared[n]),12:E3} {Math.Sqrt(newOrbit.MagnitudeSquared[n]),12:E3}  {Math.Sqrt(dr * dr + di * di),12:E3}");
        }
        Console.WriteLine();

        var oldRoundTrace = TraceOld(oldRound, deltaCRe, deltaCIm, scaleExp0, iterations, leafOffset, steps);
        var oldExactTrace = TraceOld(oldExact, deltaCRe, deltaCIm, scaleExp0, iterations, leafOffset, steps);
        var newTrace = TraceNew(newOrbit, deltaCRe, deltaCIm, scaleExp0, iterations, steps);

        Console.WriteLine("per-iteration trajectory: OLD engine on its own reference vs NEW kernel");
        Console.WriteLine($"{"n",4}  {"old.Re",18} {"old.Im",18} {"oldBr",6} {"oldTrap",11}  {"new.Re",18} {"new.Im",18} {"newBr",6} {"newTrap",11}  {"relDiff|z|",11}");
        var firstDivergence = -1;
        var count = Math.Min(oldRoundTrace.Count, newTrace.Count);
        for (var i = 0; i < count; i++)
        {
            var o = oldRoundTrace[i];
            var w = newTrace[i];
            var dr = o.Re - w.Re;
            var di = o.Im - w.Im;
            var scale = Math.Max(Math.Sqrt(o.Re * o.Re + o.Im * o.Im), 1e-300);
            var rel = Math.Sqrt(dr * dr + di * di) / scale;
            if (firstDivergence < 0 && rel > 1e-12) firstDivergence = o.N;
            var show = i < 12 || (firstDivergence >= 0 && o.N >= firstDivergence - 2 && o.N <= firstDivergence + 6) || i % 10 == 0 || i >= count - 4;
            if (show)
                Console.WriteLine($"{o.N,4}  {o.Re,18:F12} {o.Im,18:F12} {(o.Decoupled ? "DEC" : "cpl"),6} {o.Trap,11:F6}  {w.Re,18:F12} {w.Im,18:F12} {(w.Decoupled ? "DEC" : "cpl"),6} {w.Trap,11:F6}  {rel,11:E3}");
        }
        Console.WriteLine();
        Console.WriteLine($"FIRST DIVERGENT ITERATION (relative |z| difference > 1e-12): {(firstDivergence < 0 ? "none within traced range" : firstDivergence.ToString(CultureInfo.InvariantCulture))}");
        Console.WriteLine();

        Report("OLD engine, K = FromDouble  (what --deepglitchrate renders)", oldRoundTrace);
        Report("OLD engine, K = FromDoubleExact (baker's K, old loop)", oldExactTrace);
        Report("NEW kernel, K = FromDoubleExact (what --kernelframe renders)", newTrace);
        Console.WriteLine();

        Console.WriteLine("escape iteration across a row of pixels (y fixed at the traced pixel)");
        Console.WriteLine($"{"px",5}  {"oldRoundIt",11} {"oldRoundTrap",14}  {"oldExactIt",11} {"oldExactTrap",14}  {"newIt",11} {"newTrap",14}");
        for (var x = 0; x < size; x += Math.Max(1, size / 16))
        {
            var dcr = (x - half) * offsetToDelta;
            var a = TraceOld(oldRound, dcr, deltaCIm, scaleExp0, iterations, leafOffset, steps);
            var b = TraceOld(oldExact, dcr, deltaCIm, scaleExp0, iterations, leafOffset, steps);
            var c = TraceNew(newOrbit, dcr, deltaCIm, scaleExp0, iterations, steps);
            Console.WriteLine($"{x,5}  {Escape(a),11} {Trap(a),14:F6}  {Escape(b),11} {Trap(b),14:F6}  {Escape(c),11} {Trap(c),14:F6}");
        }
        return 0;
    }

    readonly record struct Sample(int N, double Re, double Im, double MagnitudeSquared, bool Decoupled, double Trap, bool Escaped);

    static string Escape(List<Sample> trace)
    {
        for (var i = 0; i < trace.Count; i++)
            if (trace[i].Escaped) return trace[i].N.ToString(CultureInfo.InvariantCulture);
        return "none";
    }

    static double Trap(List<Sample> trace) => trace.Count == 0 ? double.NaN : trace[^1].Trap;

    static void Report(string label, List<Sample> trace)
    {
        var decoupledCount = 0;
        var firstDecoupled = -1;
        foreach (var s in trace)
        {
            if (!s.Decoupled) continue;
            decoupledCount++;
            if (firstDecoupled < 0) firstDecoupled = s.N;
        }
        var escape = Escape(trace);
        Console.WriteLine($"{label}: escapeIteration={escape} decoupledSteps={decoupledCount} firstDecoupledAt={(firstDecoupled < 0 ? "never" : firstDecoupled.ToString(CultureInfo.InvariantCulture))} trapMin={Trap(trace):F7}");
    }

    static List<Sample> TraceOld(DeepOrbitTrapEngine.DeepReference reference, double deltaCRe, double deltaCIm,
        int scaleExp, int maxIterations, double leafOffset, int steps)
    {
        var samples = new List<Sample>();
        var delta = new DdComplex(deltaCRe, deltaCIm);
        var trap = 1e5;
        var limit = Math.Min(Math.Min(maxIterations - 1, reference.Length - 1), steps);
        for (var n = 0; n < limit; n++)
        {
            DeepOrbitTrapEngine.PerturbStep(reference, n, ref delta, ref scaleExp);
            var decoupled = DeepOrbitTrapEngine.LastStepLeftPerturbativeRegime;
            var v = reference.Z[n + 1] + DdComplex.ScaleB(delta, scaleExp);
            var magnitudeSquared = v.MagnitudeSquared;
            var re = v.Re.ToDouble();
            var im = v.Im.ToDouble();
            if (magnitudeSquared > 1024.0)
            {
                samples.Add(new Sample(n + 1, re, im, magnitudeSquared, decoupled, trap, true));
                return samples;
            }
            trap = Math.Min(trap, BStyleColoring.LeafTrapValue(re, im, leafOffset));
            samples.Add(new Sample(n + 1, re, im, magnitudeSquared, decoupled, trap, false));

            var maxAbs = Math.Max(Math.Abs(delta.Re.Hi), Math.Abs(delta.Im.Hi));
            if (maxAbs > 1.0e15)
            {
                var k = Math.ILogB(maxAbs);
                delta = DdComplex.ScaleB(delta, -k);
                scaleExp += k;
            }
        }
        return samples;
    }

    static List<Sample> TraceNew(ReferenceOrbit<DoubleDouble> orbit, double deltaCRe, double deltaCIm,
        int scaleExp, int maxIterations, int steps)
    {
        var samples = new List<Sample>();
        var delta = Cx<DoubleDouble>.FromDouble(deltaCRe, deltaCIm);
        var color = ColorState.Fresh(1e5);
        var referenceIndex = 0;
        var limit = Math.Min(maxIterations - 1, steps);
        for (var n = 0; n < limit; n++)
        {
            if (referenceIndex + 1 >= orbit.Length) return samples;
            PowerJuliaAtom<DoubleDouble>.Step(orbit, referenceIndex, ref delta, ref scaleExp, out var decoupled, 0.0, out _);
            referenceIndex++;

            var offset = Cx<DoubleDouble>.ScaleB(delta, scaleExp);
            var point = orbit.Z[referenceIndex] + offset;
            var re = point.Re.ToDouble();
            var im = point.Im.ToDouble();
            var magnitudeSquared = re * re + im * im;
            if (magnitudeSquared > PowerJuliaAtom<DoubleDouble>.BailoutSquared)
            {
                samples.Add(new Sample(n + 1, re, im, magnitudeSquared, decoupled, color.TrapMin, true));
                return samples;
            }
            BStyleTrapAtom.Accumulate(ref color, re, im, 0.0);
            samples.Add(new Sample(n + 1, re, im, magnitudeSquared, decoupled, color.TrapMin, false));

            var leading = Math.Max(Math.Abs(delta.Re.HighPart), Math.Abs(delta.Im.HighPart));
            if (leading > 1.0e15 || (leading > 0.0 && leading < 1.0e-15))
            {
                var k = Math.ILogB(leading);
                delta = Cx<DoubleDouble>.ScaleB(delta, -k);
                scaleExp += k;
            }
        }
        return samples;
    }

    static double Arg(string[] args, int index, double fallback) =>
        args.Length > index && double.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
