using System.Globalization;
using FractalAnimator.Core;
using FractalAnimator.Core.Atoms;
using FractalAnimator.Core.Kernel;

namespace FractalAnimator.Diagnostics;

public static class OrbitTableAudit
{
    public const string CenterRealText = "1.69606094102335990831239662074185263380097653556446974121776163540637298024853";
    public const string CenterImagText = "2.08049902493822965286894308802169375262087436820149099900777715195242489638537";

    const double PowerReal = -2.23;
    const double PowerImag = 0.0;

    static readonly double JuliaReal = -77781.0 * 1e-5;
    static readonly double JuliaImag = -23656.0 * 1e-5;

    sealed class TruthOrbit
    {
        public required BigDecimalCompat[] ZRe;
        public required BigDecimalCompat[] ZIm;
        public required BigDecimalCompat[] PowRe;
        public required BigDecimalCompat[] PowIm;
        public required BigDecimalCompat[] D1Re;
        public required BigDecimalCompat[] D1Im;
        public required BigDecimalCompat[] D2Re;
        public required BigDecimalCompat[] D2Im;
        public required int Precision;
    }

    public static int Run() => Run(300, 60.0, 300, true);

    public static int Run(int iterations, double zooms, int truthPrecision, bool includeRenderComparison = true)
    {
        var cx = BigDecimalCompat.Parse(CenterRealText);
        var cy = BigDecimalCompat.Parse(CenterImagText);
        var work = new MathContextCompat(Math.Max(truthPrecision + 60, 260));

        var kDecimalRe = BigDecimalCompat.FromDouble(JuliaReal);
        var kDecimalIm = BigDecimalCompat.FromDouble(JuliaImag);
        var kExactRe = BigDecimalCompat.FromDoubleExact(JuliaReal);
        var kExactIm = BigDecimalCompat.FromDoubleExact(JuliaImag);

        Console.WriteLine("=== ORBIT TABLE AUDIT ===");
        Console.WriteLine($"center      = ({CenterRealText}, {CenterImagText})");
        Console.WriteLine($"power       = ({PowerReal}, {PowerImag})   iterations={iterations}   zooms={zooms}");
        Console.WriteLine();
        Console.WriteLine("--- [INPUT] Julia constant K as each engine encodes it ---");
        Console.WriteLine($"OLD  DeepOrbitTrapEngine  <- BigDecimalCompat.FromDouble(k)      re={kDecimalRe.ToPlainString()}");
        Console.WriteLine($"NEW  PowerJuliaBaker      <- BigDecimalCompat.FromDoubleExact(k) re={kExactRe.ToPlainString()}");
        var kDiffRe = kExactRe.Subtract(kDecimalRe, work);
        var kDiffIm = kExactIm.Subtract(kDecimalIm, work);
        var kRel = RelDiff(kExactRe, kExactIm, kDecimalRe, kDecimalIm, work);
        Console.WriteLine($"K difference: dRe={kDiffRe.ToDouble():E6} dIm={kDiffIm.ToDouble():E6} relative={kRel:E6}");
        Console.WriteLine();

        var swTruth = System.Diagnostics.Stopwatch.StartNew();
        var truth = BuildTruth(cx, cy, kExactRe, kExactIm, iterations, truthPrecision);
        swTruth.Stop();
        Console.WriteLine($"[truth] independent BigDecimal orbit at {truthPrecision} digits built in {swTruth.ElapsedMilliseconds} ms");

        var swBake = System.Diagnostics.Stopwatch.StartNew();
        var baked = PowerJuliaBaker.Bake(cx, cy, JuliaReal, JuliaImag, PowerReal, PowerImag, iterations);
        swBake.Stop();
        Console.WriteLine($"[new]   PowerJuliaBaker.Bake precision={baked.Precision} in {swBake.ElapsedMilliseconds} ms");

        var swOld = System.Diagnostics.Stopwatch.StartNew();
        var oldShipped = DeepOrbitTrapEngine.BuildReference(cx, cy, kDecimalRe, kDecimalIm, PowerReal, PowerImag, zooms, iterations, default);
        var oldExactK = DeepOrbitTrapEngine.BuildReference(cx, cy, kExactRe, kExactIm, PowerReal, PowerImag, zooms, iterations, default);
        swOld.Stop();
        var oldPrecision = Math.Max(30, (int)(zooms * 0.30103) + (int)(iterations * 0.25) + 24);
        Console.WriteLine($"[old]   DeepOrbitTrapEngine.BuildReference precision={oldPrecision} (x2 variants) in {swOld.ElapsedMilliseconds} ms");
        Console.WriteLine();

        var narrowedDd = PowerJuliaBaker.Narrow<DoubleDouble>(baked);

        var status = 0;
        status |= CompareZ(iterations, work, truth, baked, narrowedDd, oldShipped, oldExactK);
        status |= CompareAux(iterations, work, truth, baked, narrowedDd, oldShipped, oldExactK);
        NormalisationCheck(work, truth, baked, oldExactK);
        LimbOrderVerdict(work, cx, truth);
        Dynamics(iterations, truth, baked, oldShipped, oldExactK, work);
        if (includeRenderComparison)
            RenderComparison(cx, cy, kDecimalRe, kDecimalIm, kExactRe, kExactIm, baked, zooms, 120, iterations);
        return status;
    }

    static void RenderComparison(BigDecimalCompat cx, BigDecimalCompat cy,
        BigDecimalCompat kDecimalRe, BigDecimalCompat kDecimalIm,
        BigDecimalCompat kExactRe, BigDecimalCompat kExactIm,
        PowerJuliaBaker.BakedOrbit baked, double zooms, int size, int iterations)
    {
        Console.WriteLine("--- [DIAG2] same view rendered by the old engine under each K encoding ---");
        const double leafOffset = 0.59;
        const double colorR = 10.0, colorG = 2.5, colorB = 0.75;

        foreach (var (label, kRe, kIm) in new[]
                 {
                     ("old  K=FromDouble      (shipped --deepglitchrate)", kDecimalRe, kDecimalIm),
                     ("old  K=FromDoubleExact (what the baker uses)     ", kExactRe, kExactIm),
                 })
        {
            var rgb = new float[size * size * 3];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            DeepOrbitTrapEngine.Render(cx, cy, kRe, kIm, PowerReal, PowerImag, zooms, size, size,
                iterations, leafOffset, colorR, colorG, colorB, rgb);
            sw.Stop();
            Console.WriteLine($"  {label}  distinctColors={DistinctColors(rgb, size)}  {sw.ElapsedMilliseconds}ms");

            var reference = DeepOrbitTrapEngine.BuildReference(cx, cy, kRe, kIm, PowerReal, PowerImag, zooms, iterations, default);
            var spacingLog2 = 2.0 - zooms;
            var scaleExp = (int)Math.Floor(spacingLog2);
            var mantissa = Math.Pow(2.0, spacingLog2 - scaleExp);
            var offsetToDelta = mantissa / size;
            var half = size / 2.0;
            var minIters = int.MaxValue;
            var maxIters = int.MinValue;
            var glitched = 0;
            var distinctIters = new SortedSet<int>();
            for (var y = 0; y < size; y += 8)
                for (var x = 0; x < size; x += 8)
                {
                    var iters = DeepOrbitTrapEngine.IteratePixel(reference,
                        (x - half) * offsetToDelta, (half - y) * offsetToDelta, scaleExp, iterations,
                        leafOffset, out _, out _, out var isGlitch);
                    if (isGlitch) { glitched++; continue; }
                    distinctIters.Add(iters);
                    if (iters < minIters) minIters = iters;
                    if (iters > maxIters) maxIters = iters;
                }
            Console.WriteLine($"       un-repaired escape iterations over a 15x15 probe grid: min={minIters} max={maxIters} distinct={distinctIters.Count} glitched={glitched}");
        }

        var kernelRgb = new float[size * size * 3];
        var kernelStats = CertifiedRenderer.Render(baked, zooms, size, size, iterations, kernelRgb);
        Console.WriteLine($"  new  CertifiedRenderer                            distinctColors={DistinctColors(kernelRgb, size)}  uncertified={kernelStats.Uncertified}  {kernelStats.TotalMilliseconds:0}ms");
        Console.WriteLine();
    }

    static int DistinctColors(float[] rgb, int size)
    {
        var seen = new HashSet<int>();
        for (var y = 0; y < size; y += 2)
            for (var x = 0; x < size; x += 2)
            {
                var i = (y * size + x) * 3;
                var r = (byte)Math.Clamp((int)(rgb[i + 0] * 255), 0, 255);
                var g = (byte)Math.Clamp((int)(rgb[i + 1] * 255), 0, 255);
                var b = (byte)Math.Clamp((int)(rgb[i + 2] * 255), 0, 255);
                seen.Add((r << 16) | (g << 8) | b);
            }
        return seen.Count;
    }

    static TruthOrbit BuildTruth(BigDecimalCompat cx, BigDecimalCompat cy,
        BigDecimalCompat kRe, BigDecimalCompat kIm, int iterations, int precision)
    {
        var ctx = new MathContextCompat(precision);
        var length = iterations + 1;
        var zRe = new BigDecimalCompat[length];
        var zIm = new BigDecimalCompat[length];
        var powRe = new BigDecimalCompat[length];
        var powIm = new BigDecimalCompat[length];
        var d1Re = new BigDecimalCompat[length];
        var d1Im = new BigDecimalCompat[length];
        var d2Re = new BigDecimalCompat[length];
        var d2Im = new BigDecimalCompat[length];

        var half = BigDecimalCompat.FromDoubleExact(0.5);
        var pRe = BigDecimalCompat.FromDoubleExact(PowerReal);
        var pIm = BigDecimalCompat.FromDoubleExact(PowerImag);
        var pm1Re = BigDecimalCompat.FromDoubleExact(PowerReal - 1.0);
        var pm1Im = BigDecimalCompat.FromDoubleExact(PowerImag);

        var zr = cx;
        var zi = cy;
        zRe[0] = zr; zIm[0] = zi;

        for (var n = 0; n < iterations; n++)
        {
            var (a1r, a1i) = DeepOrbitTrapEngine.ComplexPow(zr, zi, PowerReal - 1.0, PowerImag, ctx);
            var (s1r, s1i) = Scale(a1r, a1i, pRe, pIm, ctx);
            d1Re[n] = s1r; d1Im[n] = s1i;

            var (a2r, a2i) = DeepOrbitTrapEngine.ComplexPow(zr, zi, PowerReal - 2.0, PowerImag, ctx);
            var (t1r, t1i) = Scale(a2r, a2i, pRe, pIm, ctx);
            var (t2r, t2i) = Scale(t1r, t1i, pm1Re, pm1Im, ctx);
            d2Re[n] = t2r.Multiply(half, ctx); d2Im[n] = t2i.Multiply(half, ctx);

            var (pr, pi) = DeepOrbitTrapEngine.ComplexPow(zr, zi, PowerReal, PowerImag, ctx);
            powRe[n] = pr; powIm[n] = pi;

            zr = pr.Add(kRe, ctx);
            zi = pi.Add(kIm, ctx);
            zRe[n + 1] = zr; zIm[n + 1] = zi;
        }
        powRe[iterations] = BigDecimalCompat.Zero; powIm[iterations] = BigDecimalCompat.Zero;
        d1Re[iterations] = BigDecimalCompat.Zero; d1Im[iterations] = BigDecimalCompat.Zero;
        d2Re[iterations] = BigDecimalCompat.Zero; d2Im[iterations] = BigDecimalCompat.Zero;

        return new TruthOrbit
        {
            ZRe = zRe, ZIm = zIm, PowRe = powRe, PowIm = powIm,
            D1Re = d1Re, D1Im = d1Im, D2Re = d2Re, D2Im = d2Im,
            Precision = precision,
        };
    }

    static (BigDecimalCompat, BigDecimalCompat) Scale(
        BigDecimalCompat re, BigDecimalCompat im, BigDecimalCompat sr, BigDecimalCompat si, MathContextCompat ctx) =>
        (re.Multiply(sr, ctx).Subtract(im.Multiply(si, ctx), ctx),
         re.Multiply(si, ctx).Add(im.Multiply(sr, ctx), ctx));

    static int CompareZ(int iterations, MathContextCompat ctx, TruthOrbit truth,
        PowerJuliaBaker.BakedOrbit baked, ReferenceOrbit<DoubleDouble> narrowed,
        DeepOrbitTrapEngine.DeepReference oldShipped, DeepOrbitTrapEngine.DeepReference oldExactK)
    {
        Console.WriteLine("--- [A1] Z_n : baked limbs vs old DoubleDouble tables vs independent truth ---");

        var series = new (string Name, Func<int, (BigDecimalCompat, BigDecimalCompat)> Get)[]
        {
            ("newLimbs(exact sum)   vs truth", n => (BigLimbs(baked.ZReal, n, ctx), BigLimbs(baked.ZImag, n, ctx))),
            ("newNarrowed<DD>       vs truth", n => (Big(narrowed.Z[n].Re, ctx), Big(narrowed.Z[n].Im, ctx))),
            ("old(K=FromDoubleExact) vs truth", n => (Big(oldExactK.Z[n].Re, ctx), Big(oldExactK.Z[n].Im, ctx))),
            ("old(K=FromDouble,shipped) vs truth", n => (Big(oldShipped.Z[n].Re, ctx), Big(oldShipped.Z[n].Im, ctx))),
        };

        var status = 0;
        foreach (var (name, get) in series)
        {
            var worst = 0.0;
            var worstN = 0;
            var firstBad = -1;
            for (var n = 0; n <= iterations; n++)
            {
                var (r, i) = get(n);
                var d = RelDiff(truth.ZRe[n], truth.ZIm[n], r, i, ctx);
                if (d > worst) { worst = d; worstN = n; }
                if (firstBad < 0 && d > 1e-28) firstBad = n;
            }
            Console.WriteLine($"  {name,-36} maxRel={worst:E3} at n={worstN,4}  firstExceeding1e-28: n={firstBad}");
            if (name.StartsWith("newLimbs") && worst > 1e-28) status |= 1;
        }

        Console.WriteLine("  per-n sample (relative difference vs truth):");
        Console.WriteLine($"    {"n",4} {"newLimbs",12} {"newNarrowDD",12} {"oldExactK",12} {"oldShipped",12}");
        foreach (var n in SampleIndices(iterations))
        {
            var a = RelDiff(truth.ZRe[n], truth.ZIm[n], BigLimbs(baked.ZReal, n, ctx), BigLimbs(baked.ZImag, n, ctx), ctx);
            var b = RelDiff(truth.ZRe[n], truth.ZIm[n], Big(narrowed.Z[n].Re, ctx), Big(narrowed.Z[n].Im, ctx), ctx);
            var c = RelDiff(truth.ZRe[n], truth.ZIm[n], Big(oldExactK.Z[n].Re, ctx), Big(oldExactK.Z[n].Im, ctx), ctx);
            var d = RelDiff(truth.ZRe[n], truth.ZIm[n], Big(oldShipped.Z[n].Re, ctx), Big(oldShipped.Z[n].Im, ctx), ctx);
            Console.WriteLine($"    {n,4} {a,12:E3} {b,12:E3} {c,12:E3} {d,12:E3}");
        }
        Console.WriteLine();
        return status;
    }

    static int CompareAux(int iterations, MathContextCompat ctx, TruthOrbit truth,
        PowerJuliaBaker.BakedOrbit baked, ReferenceOrbit<DoubleDouble> narrowed,
        DeepOrbitTrapEngine.DeepReference oldShipped, DeepOrbitTrapEngine.DeepReference oldExactK)
    {
        Console.WriteLine("--- [A2] aux tables : Z^p, f'(Z), f''(Z)/2 ---");

        var tables = new (string Name, int Aux, BigDecimalCompat[] TRe, BigDecimalCompat[] TIm, DdComplex[] OldExact, DdComplex[] OldShipped)[]
        {
            ("Z^p        ", PowerJuliaAtom<DoubleDouble>.AuxPow, truth.PowRe, truth.PowIm, oldExactK.Pow, oldShipped.Pow),
            ("f'(Z)      ", PowerJuliaAtom<DoubleDouble>.AuxDeriv, truth.D1Re, truth.D1Im, oldExactK.Deriv, oldShipped.Deriv),
            ("f''(Z)/2   ", PowerJuliaAtom<DoubleDouble>.AuxDeriv2Half, truth.D2Re, truth.D2Im, oldExactK.Deriv2Half, oldShipped.Deriv2Half),
        };

        var status = 0;
        foreach (var (name, aux, tRe, tIm, oldExact, oldShip) in tables)
        {
            var worstNew = 0.0; var worstNewN = 0;
            var worstNarrow = 0.0; var worstNarrowN = 0;
            var worstOldExact = 0.0; var worstOldExactN = 0;
            var worstOldShip = 0.0; var worstOldShipN = 0;
            for (var n = 0; n < iterations; n++)
            {
                var a = RelDiff(tRe[n], tIm[n], BigLimbs(baked.AuxReal[aux], n, ctx), BigLimbs(baked.AuxImag[aux], n, ctx), ctx);
                if (a > worstNew) { worstNew = a; worstNewN = n; }
                var b = RelDiff(tRe[n], tIm[n], Big(narrowed.Aux[aux][n].Re, ctx), Big(narrowed.Aux[aux][n].Im, ctx), ctx);
                if (b > worstNarrow) { worstNarrow = b; worstNarrowN = n; }
                var c = RelDiff(tRe[n], tIm[n], Big(oldExact[n].Re, ctx), Big(oldExact[n].Im, ctx), ctx);
                if (c > worstOldExact) { worstOldExact = c; worstOldExactN = n; }
                var d = RelDiff(tRe[n], tIm[n], Big(oldShip[n].Re, ctx), Big(oldShip[n].Im, ctx), ctx);
                if (d > worstOldShip) { worstOldShip = d; worstOldShipN = n; }
            }
            Console.WriteLine($"  {name} newLimbs   maxRel={worstNew:E3} at n={worstNewN,4}");
            Console.WriteLine($"  {name} newNarrDD  maxRel={worstNarrow:E3} at n={worstNarrowN,4}");
            Console.WriteLine($"  {name} oldExactK  maxRel={worstOldExact:E3} at n={worstOldExactN,4}");
            Console.WriteLine($"  {name} oldShipped maxRel={worstOldShip:E3} at n={worstOldShipN,4}");
            if (worstNew > 1e-28) status |= 2;
        }
        Console.WriteLine();
        return status;
    }

    static void NormalisationCheck(MathContextCompat ctx, TruthOrbit truth,
        PowerJuliaBaker.BakedOrbit baked, DeepOrbitTrapEngine.DeepReference oldExactK)
    {
        Console.WriteLine("--- [A2-NORM] scaling convention : table / (analytic value) ---");
        Console.WriteLine("  expected 1 for both engines if f'(Z)=p*Z^(p-1) and f''(Z)/2=(1/2)p(p-1)Z^(p-2)");
        Console.WriteLine($"    {"n",3} {"table",10} {"engine",6} {"ratioRe",22} {"ratioIm",14}");
        foreach (var n in new[] { 0, 1, 2, 7, 33, 100 })
        {
            Report("f'(Z)", "new", n, truth.D1Re[n], truth.D1Im[n],
                BigLimbs(baked.AuxReal[PowerJuliaAtom<DoubleDouble>.AuxDeriv], n, ctx),
                BigLimbs(baked.AuxImag[PowerJuliaAtom<DoubleDouble>.AuxDeriv], n, ctx), ctx);
            Report("f'(Z)", "old", n, truth.D1Re[n], truth.D1Im[n],
                Big(oldExactK.Deriv[n].Re, ctx), Big(oldExactK.Deriv[n].Im, ctx), ctx);
            Report("f''/2", "new", n, truth.D2Re[n], truth.D2Im[n],
                BigLimbs(baked.AuxReal[PowerJuliaAtom<DoubleDouble>.AuxDeriv2Half], n, ctx),
                BigLimbs(baked.AuxImag[PowerJuliaAtom<DoubleDouble>.AuxDeriv2Half], n, ctx), ctx);
            Report("f''/2", "old", n, truth.D2Re[n], truth.D2Im[n],
                Big(oldExactK.Deriv2Half[n].Re, ctx), Big(oldExactK.Deriv2Half[n].Im, ctx), ctx);
        }
        Console.WriteLine();

        static void Report(string table, string engine, int n,
            BigDecimalCompat tr, BigDecimalCompat ti, BigDecimalCompat ar, BigDecimalCompat ai, MathContextCompat ctx)
        {
            var den = tr.Multiply(tr, ctx).Add(ti.Multiply(ti, ctx), ctx);
            if (den.UnscaledValue.IsZero) { Console.WriteLine($"    {n,3} {table,10} {engine,6} (truth is zero)"); return; }
            var rr = ar.Multiply(tr, ctx).Add(ai.Multiply(ti, ctx), ctx).Divide(den, ctx);
            var ri = ai.Multiply(tr, ctx).Subtract(ar.Multiply(ti, ctx), ctx).Divide(den, ctx);
            Console.WriteLine($"    {n,3} {table,10} {engine,6} {rr.ToDouble(),22:G17} {ri.ToDouble(),14:E3}");
        }
    }

    static void LimbOrderVerdict(MathContextCompat ctx, BigDecimalCompat center, TruthOrbit truth)
    {
        Console.WriteLine("--- [A3] BigDecimalCompat.ToLimbs ordering and PowerJuliaBaker.Sum direction ---");

        var samples = new (string Name, BigDecimalCompat Value)[]
        {
            ("center.Re", center),
            ("Z_1.Re", truth.ZRe[1]),
            ("Z_17.Im", truth.ZIm[17]),
            ("f'(Z_5).Re", truth.D1Re[5]),
        };

        var allDescending = true;
        Span<double> limbs = stackalloc double[4];
        foreach (var (name, value) in samples)
        {
            value.ToLimbs(limbs);
            var descending = true;
            for (var i = 1; i < 4; i++)
                if (limbs[i] != 0.0 && Math.Abs(limbs[i]) >= Math.Abs(limbs[i - 1])) descending = false;
            if (!descending) allDescending = false;

            Console.WriteLine($"  {name}: value={value.ToDouble():E6}");
            var partial = BigDecimalCompat.Zero;
            for (var i = 0; i < 4; i++)
            {
                partial = partial.Add(BigDecimalCompat.FromDoubleExact(limbs[i]), ctx);
                var residual = value.Subtract(partial, ctx);
                var rel = RelDiff(value, BigDecimalCompat.Zero, partial, BigDecimalCompat.Zero, ctx);
                Console.WriteLine($"    limb[{i}]={limbs[i]:E17}  |limb|={Math.Abs(limbs[i]):E3}  residualAfterSum={residual.ToDouble():E3}  relErrorOfPrefix={rel:E3}");
            }

            var forward = SumForward<DoubleDouble>(limbs);
            var backward = SumBackward<DoubleDouble>(limbs);
            var fRel = RelDiff(value, BigDecimalCompat.Zero, Big(forward, ctx), BigDecimalCompat.Zero, ctx);
            var bRel = RelDiff(value, BigDecimalCompat.Zero, Big(backward, ctx), BigDecimalCompat.Zero, ctx);
            var direct = value.ToDoubleDouble();
            var dRel = RelDiff(value, BigDecimalCompat.Zero, Big(direct, ctx), BigDecimalCompat.Zero, ctx);
            Console.WriteLine($"    DD sum largest-first={fRel:E3}  smallest-first(shipped Sum)={bRel:E3}  ToDoubleDouble()={dRel:E3}");
            Console.WriteLine($"    descending order: {descending}");
        }
        Console.WriteLine($"  VERDICT: ToLimbs emits limb[0] largest = {allDescending}; PowerJuliaBaker.Sum iterates limbs.Length-1 -> 0, i.e. smallest-magnitude first, which is the numerically correct direction.");
        Console.WriteLine();
    }

    static void Dynamics(int iterations, TruthOrbit truth, PowerJuliaBaker.BakedOrbit baked,
        DeepOrbitTrapEngine.DeepReference oldShipped, DeepOrbitTrapEngine.DeepReference oldExactK, MathContextCompat ctx)
    {
        Console.WriteLine("--- [DIAG] where each reference orbit first crosses the |z|^2 > 1024 bailout ---");
        Console.WriteLine($"  new baked  : {FirstCrossing(baked.MagnitudeSquared, iterations)}");
        Console.WriteLine($"  old shipped: {FirstCrossing(oldShipped.MagnitudeSquared, iterations)}");
        Console.WriteLine($"  old exactK : {FirstCrossing(oldExactK.MagnitudeSquared, iterations)}");
        var truthMag = new double[iterations + 1];
        for (var n = 0; n <= iterations; n++)
        {
            var r = truth.ZRe[n].ToDouble();
            var i = truth.ZIm[n].ToDouble();
            truthMag[n] = r * r + i * i;
        }
        Console.WriteLine($"  truth      : {FirstCrossing(truthMag, iterations)}");
        Console.WriteLine();

        Console.WriteLine("  cumulative linear amplification prod|f'(Z_k)| (a pixel decouples once it reaches ~1/u0):");
        var logSum = 0.0;
        var reported = 0;
        for (var n = 0; n < iterations && reported < 12; n++)
        {
            logSum += Math.Log10(Math.Max(baked.ExpansionMagnitude[n], 1e-300));
            if (n % 10 == 9)
            {
                Console.WriteLine($"    n={n + 1,4}  log10 prod|f'| = {logSum,10:F3}   |Z_n|^2={baked.MagnitudeSquared[n + 1]:E3}");
                reported++;
            }
        }
        Console.WriteLine();
    }

    static string FirstCrossing(double[] magnitudeSquared, int iterations)
    {
        for (var n = 0; n <= iterations && n < magnitudeSquared.Length; n++)
            if (magnitudeSquared[n] > 1024.0) return $"first n with |Z|^2>1024 is {n} (|Z|^2={magnitudeSquared[n]:E4})";
        return "never crosses 1024 within the table";
    }

    static IEnumerable<int> SampleIndices(int iterations)
    {
        var set = new SortedSet<int> { 0, 1, 2, 5, 10, 20, 40, 60, 80, 90, 100, 104, 105, 106, 120, 150, 180, 200, 250, iterations };
        foreach (var n in set) if (n >= 0 && n <= iterations) yield return n;
    }

    static T SumForward<T>(ReadOnlySpan<double> limbs) where T : struct, IPrecision<T>
    {
        var value = T.Zero;
        for (var i = 0; i < limbs.Length; i++) value = T.Add(value, T.FromDouble(limbs[i]));
        return value;
    }

    static T SumBackward<T>(ReadOnlySpan<double> limbs) where T : struct, IPrecision<T>
    {
        var value = T.Zero;
        for (var i = limbs.Length - 1; i >= 0; i--) value = T.Add(value, T.FromDouble(limbs[i]));
        return value;
    }

    static BigDecimalCompat Big(DoubleDouble d, MathContextCompat ctx) =>
        BigDecimalCompat.FromDoubleExact(d.Hi).Add(BigDecimalCompat.FromDoubleExact(d.Lo), ctx);

    static BigDecimalCompat BigLimbs(double[][] limbs, int n, MathContextCompat ctx)
    {
        var value = BigDecimalCompat.Zero;
        for (var i = limbs.Length - 1; i >= 0; i--)
            value = value.Add(BigDecimalCompat.FromDoubleExact(limbs[i][n]), ctx);
        return value;
    }

    static double RelDiff(BigDecimalCompat ar, BigDecimalCompat ai,
        BigDecimalCompat br, BigDecimalCompat bi, MathContextCompat ctx)
    {
        var dr = ar.Subtract(br, ctx);
        var di = ai.Subtract(bi, ctx);
        var num = dr.Multiply(dr, ctx).Add(di.Multiply(di, ctx), ctx);
        if (num.UnscaledValue.IsZero) return 0.0;
        var den = ar.Multiply(ar, ctx).Add(ai.Multiply(ai, ctx), ctx);
        if (den.UnscaledValue.IsZero) return double.PositiveInfinity;
        var ratio = num.Divide(den, ctx).ToDouble();
        return ratio <= 0.0 ? 0.0 : Math.Sqrt(ratio);
    }

    public static int RunFromArgs(string[] args)
    {
        var iterations = args.Length > 0 && int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var it) ? it : 300;
        var zooms = args.Length > 1 && double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ? z : 60.0;
        var precision = args.Length > 2 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pr) ? pr : 300;
        var renders = args.Length <= 3 || !args[3].Equals("norender", StringComparison.OrdinalIgnoreCase);
        return Run(iterations, zooms, precision, renders);
    }
}
