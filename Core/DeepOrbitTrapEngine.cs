namespace FractalAnimator.Core;

/// <summary>
/// Unlimited-depth zoom of a Julia set z -&gt; z^p + K (p a general complex power, not just 2) with
/// FractalParadise's orbit-trap "B-style" coloring.
///
/// This is the deep-zoom version of <see cref="ShaderFractal"/> — it uses perturbation so the zoom can
/// descend forever instead of dissolving at double precision (~10^15). The high-precision reference
/// orbit is the "reset/rebase" trick: the center is held in arbitrary precision (<see cref="BigDecimalCompat"/>),
/// each pixel iterates a rescaled delta in double, and the true orbit point is reconstructed as
/// <c>Z_n + scale·delta_n</c> to feed the orbit traps.
///
/// For power exactly 2 the perturbation recurrence <c>delta' = 2*Z*delta + delta^2</c> is exact (no
/// truncation — (Z+d)^2 really is Z^2 + 2Zd + d^2). For a general non-integer power there is no finite
/// polynomial recurrence, so this uses the standard second-order Taylor expansion of f around Z_n:
///   f(Z+d) ~= f(Z) + f'(Z)*d + (1/2)f''(Z)*d^2,  so  delta' = D1*delta + D2H*delta^2
/// with D1 = f'(Z_n) = p*Z_n^(p-1) and D2H = (1/2)f''(Z_n) = (1/2)p(p-1)Z_n^(p-2), both evaluated in
/// plain double (they're smooth functions of the O(1)-magnitude reference point, not of the deep-zoom
/// coordinate itself, so double precision is exact for these purposes) and precomputed once per
/// reference-orbit iteration — shared by every pixel, not recomputed per pixel. This reduces to the
/// existing exact power-2 recurrence when p=2 (D1=2Z, D2H=1), by construction.
///
/// Unlike z^2 (entire, smooth everywhere), z^p for non-integer p has a genuine singularity at z=0: if
/// the reference orbit passes close to zero at some iteration, f'/f'' blow up there and the 2nd-order
/// truncation stops being valid for every pixel from that iteration on (confirmed empirically — with no
/// glitch handling, a shallow-zoom comparison against direct double iteration matched almost exactly for
/// ~20 iterations, then diverged hard and stayed diverged). This is the classic perturbation "glitch"
/// (Pauldelbrot criterion: the reconstructed orbit collapses far below the reference), fixed the
/// standard way — detect it and re-iterate affected pixels against a fresh reference orbit rebased at
/// one of them, repeating until resolved.
/// </summary>
public static class DeepOrbitTrapEngine
{
    const double RenormThreshold = 1.0e15;
    const double BoundSquared = 1024.0;          // |z| > 32, matching the shaders
    const double ReferenceEscapeSquared = 1.0e6;
    /// <summary>
    /// Pauldelbrot threshold on |v|²/|Z|². The reconstruction v = Z + d cancels, leaving an absolute
    /// error of about one ulp of |Z|, so the surviving relative accuracy is roughly |v|/|Z| × ulp.
    ///
    /// This is a *magnitude ratio*, not a measurement of lost precision, so it has to be calibrated to
    /// the arithmetic underneath it. The classic 1e-6 (flagging once |v| drops to 1e-3|Z|) is right for
    /// plain double, where that cancellation leaves ~13 digits. The pixel loop now carries double-double
    /// (~32 digits), so the same cancellation still leaves ~29 — flagging there would send thousands of
    /// perfectly good pixels into an arbitrary-precision repair that has nothing to fix. (Measured: with
    /// the double-precision threshold left in place, switching to double-double changed the glitch count
    /// by 1%, because the test was firing on geometry rather than on precision.)
    /// At 1e-32 the flag means |v| &lt; 1e-16|Z|, i.e. the cancellation has eaten enough of the ~32 digits
    /// that what remains is no better than a perfect double.
    /// </summary>
    public static double GlitchFactor = 1.0e-32;
    const int MaxRebasePasses = 6;

    /// <summary>Test hook: prints glitch counts and a cost breakdown per repair pass to stdout.</summary>
    public static bool DebugLogging;

    /// <summary>How many arbitrary-precision reference orbits the last repair pass had to build. This is
    /// the number that must not scale with output resolution.</summary>
    public static int RepairReferenceBuilds;

    /// <summary>
    /// Tile edge (pixels) for the first glitch-repair pass; each later pass halves it down to a floor.
    /// One arbitrary-precision reference orbit is built per tile that still contains a glitch, so this
    /// sets how many of those expensive builds a frame pays for. Bigger tiles mean fewer builds but a
    /// more distant anchor per pixel, hence more passes — a genuine tradeoff, not a quality knob:
    /// unrepaired pixels simply get retried, so fidelity is unchanged either way.
    /// </summary>
    public static int RepairTileSize = 32;

    /// <summary>Floor the repair tile shrinks to; anchors tighter than this stop paying for themselves.</summary>
    public const int MinRepairTileSize = 16;

    /// <summary>
    /// How many iterations a pixel may spend outside the perturbative regime before its accumulated
    /// error is judged to have eaten the double-double mantissa. Each such step multiplies the error by
    /// ~1.6 (0.204 decimal digits), and there are ~32 digits to spend, so ~100 steps still leaves a
    /// ~1e-10 relative error — comfortably below anything the 8-bit output can show.
    /// </summary>
    public const int MaxNonPerturbativeSteps = 100;

    /// <summary>z^p in arbitrary precision — the same log/exp formula as <see cref="ShaderFractal.Pow"/>,
    /// just evaluated with <see cref="BigDecimalCompat"/> transcendentals instead of <see cref="double"/>.</summary>
    public static (BigDecimalCompat re, BigDecimalCompat im) ComplexPow(
        BigDecimalCompat zr, BigDecimalCompat zi, double powerReal, double powerImag, MathContextCompat ctx)
    {
        if (zr.UnscaledValue.IsZero && zi.UnscaledValue.IsZero) return (BigDecimalCompat.Zero, BigDecimalCompat.Zero);
        var workCtx = new MathContextCompat(ctx.Precision + 15);
        var two = new BigDecimalCompat(2, 0);
        var modSq = zr.Multiply(zr, workCtx).Add(zi.Multiply(zi, workCtx), workCtx);
        var logR = modSq.Ln(workCtx).Divide(two, workCtx);
        var theta = BigDecimalCompat.Atan2(zi, zr, workCtx);
        var pr = BigDecimalCompat.FromDoubleExact(powerReal);
        var pim = BigDecimalCompat.FromDoubleExact(powerImag);
        var realPart = pr.Multiply(logR, workCtx).Subtract(pim.Multiply(theta, workCtx), workCtx);
        var imagPart = pr.Multiply(theta, workCtx).Add(pim.Multiply(logR, workCtx), workCtx);
        var r = realPart.Exp(workCtx);
        var (sinI, cosI) = BigDecimalCompat.SinCos(imagPart, workCtx);
        return (r.Multiply(cosI, ctx), r.Multiply(sinI, ctx));
    }

    /// <summary>
    /// One reference orbit plus everything the per-pixel perturbation needs from it: the orbit itself,
    /// <c>Z_n^p</c> (the pre-<c>+K</c> value, which the exact perturbation step multiplies through), and
    /// the first two derivatives (used by the small-delta Taylor fast path).
    /// </summary>
    public sealed class DeepReference
    {
        /// <summary>The orbit, carried at double-double so that <c>Z + d</c> survives cancellation.</summary>
        public required DdComplex[] Z;
        public required DdComplex[] Pow;          // Z_n^p
        public required DdComplex[] Deriv;        // f'(Z_n)
        public required DdComplex[] Deriv2Half;   // f''(Z_n)/2
        /// <summary>Plain-double copy of the orbit magnitude², for the bailout and glitch tests that
        /// never need more than double.</summary>
        public required double[] MagnitudeSquared;
        public required int Length;
        public required double PowerReal, PowerImag;
    }

    public static DeepReference BuildReference(BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        BigDecimalCompat juliaReal, BigDecimalCompat juliaImag, double powerReal, double powerImag,
        double zooms, int maxIterations, CancellationToken token)
    {
        var orbit = new DdComplex[maxIterations + 1];
        var powArr = new DdComplex[maxIterations + 1];
        var derivArr = new DdComplex[maxIterations + 1];
        var deriv2Arr = new DdComplex[maxIterations + 1];
        var magSq = new double[maxIterations + 1];
        // The reference orbit's own BigDecimal precision must cover two independent needs: enough digits
        // to represent the *coordinate* at this zoom depth (zooms*0.30103), AND enough digits for the
        // orbit itself to stay numerically valid across `maxIterations` steps of a *chaotic* map — every
        // step's rounding to a fixed precision is itself a perturbation that the map amplifies. Without
        // the second term the reference silently corrupts partway through regardless of zoom.
        // The amplification rate is measured, not guessed: --deeppixeltrace shows the orbit separation
        // growing ~1.6x per iteration (0.205 digits), so 0.25 digits/iteration carries a ~20% margin.
        // This coefficient is performance-critical — BigDecimal multiply cost grows superlinearly in
        // digits, so an inflated value (0.45 was ~2.8x/iter, well above what the map actually does) costs
        // several times the runtime per reference for no added correctness.
        var precision = Math.Max(30, (int)(zooms * 0.30103) + (int)(maxIterations * 0.25) + 24);
        var context = new MathContextCompat(precision);
        var zr = centerReal;
        var zi = centerImag;
        orbit[0] = new DdComplex(zr.ToDoubleDouble(), zi.ToDoubleDouble());
        magSq[0] = orbit[0].MagnitudeSquared;

        for (var n = 0; n < maxIterations; n++)
        {
            token.ThrowIfCancellationRequested();

            // Derivatives at Z_n, shared by every pixel's step from n to n+1. Computed in arbitrary
            // precision and narrowed to double-double: they multiply the delta every iteration, so a
            // merely-double derivative would cap the whole trajectory at double precision no matter how
            // accurately the delta itself is carried.
            var (d1r, d1i) = ComplexPow(zr, zi, powerReal - 1, powerImag, context);
            var (d2r, d2i) = ComplexPow(zr, zi, powerReal - 2, powerImag, context);
            var pDd = new DdComplex(powerReal, powerImag);
            var pm1Dd = new DdComplex(powerReal - 1, powerImag);
            derivArr[n] = pDd * new DdComplex(d1r.ToDoubleDouble(), d1i.ToDoubleDouble());
            deriv2Arr[n] = pDd * pm1Dd * new DdComplex(d2r.ToDoubleDouble(), d2i.ToDoubleDouble()) * new DoubleDouble(0.5);

            var (powR, powI) = ComplexPow(zr, zi, powerReal, powerImag, context);
            powArr[n] = new DdComplex(powR.ToDoubleDouble(), powI.ToDoubleDouble());
            zr = powR.Add(juliaReal, context);
            zi = powI.Add(juliaImag, context);
            orbit[n + 1] = new DdComplex(zr.ToDoubleDouble(), zi.ToDoubleDouble());
            magSq[n + 1] = orbit[n + 1].MagnitudeSquared;
            // No early "escaped" break here. For a negative power the orbit does NOT run away once it
            // passes the bailout — |z|>32 makes |z^p| = |z|^-2.23 tiny, so the very next step folds it
            // back to about K. Truncating the reference there would strand every pixel that has not
            // resolved yet (they would all have to be rebased, which is exactly the expensive,
            // non-converging repair storm this engine was suffering).
        }
        return new DeepReference
        {
            Z = orbit, Pow = powArr, Deriv = derivArr, Deriv2Half = deriv2Arr,
            MagnitudeSquared = magSq,
            Length = maxIterations + 1,
            PowerReal = powerReal, PowerImag = powerImag,
        };
    }

    /// <summary>
    /// Perturbs one pixel and returns its escape iteration (or maxIterations for interior points),
    /// the minimum orbit-trap value, and the final orbit magnitude — everything the coloring needs.
    /// <paramref name="isGlitch"/> is set when the Pauldelbrot criterion fires (the reconstructed orbit
    /// collapsed far below the reference — the 2nd-order approximation broke down, most often because
    /// the reference passed near z=0 where f'/f'' are singular for non-integer powers); the caller must
    /// re-iterate that pixel against a rebased reference rather than trust this result.
    /// </summary>
    // |u| below this uses the Taylor fast path. At |u| = 1e-8 the dropped O(u^3) term is ~1e-24, i.e.
    // 1e-16 relative — under one ulp — so the fast path is not an approximation in that range, and it
    // works in the rescaled representation, which the exact path cannot (d_true underflows when deep).
    const double ExactPathThresholdSquared = 1.0e-16;

    /// <summary>
    /// Advances one pixel's perturbation delta by a single iteration, from reference index
    /// <paramref name="n"/> to <paramref name="n"/>+1. The single source of truth for the recurrence —
    /// <see cref="IteratePixel"/> and the pixel-trace diagnostic both go through here, so a diagnostic
    /// can never quietly drift away from what the renderer actually computes.
    /// </summary>
    /// <summary>
    /// True when the last <see cref="PerturbStep"/> left the perturbative regime (|d| no longer small
    /// next to |Z|). In that regime the step is still exact, but the pixel is effectively iterating a
    /// full-size value, so its rounding error starts amplifying with the map's Lyapunov rate instead of
    /// staying put — which is what actually spoils a pixel, and what a rebased local reference prevents.
    /// </summary>
    [ThreadStatic] public static bool LastStepLeftPerturbativeRegime;

    public static void PerturbStep(DeepReference reference, int n, ref DdComplex delta, ref int scaleExp)
    {
        var z = reference.Z[n];
        var powerReal = reference.PowerReal;
        var powerImag = reference.PowerImag;

        // u = d_true / Z_n. When the zoom is deep enough that d_true underflows, u is 0 and the Taylor
        // path below is exactly right anyway.
        var dTrue = DdComplex.ScaleB(delta, scaleExp);
        var zMagSq = reference.MagnitudeSquared[n];
        var uMagSq = zMagSq > 0 ? dTrue.MagnitudeSquared / zMagSq : 0.0;

        LastStepLeftPerturbativeRegime = uMagSq >= ExactPathThresholdSquared;

        if (uMagSq < ExactPathThresholdSquared)
        {
            // d' = f'(Z)·d + ½f''(Z)·d²   (the dropped O(d³) term is below one double-double ulp here)
            var d1 = reference.Deriv[n];
            var linear = d1 * delta;

            // The quadratic term carries a factor 2^scaleExp, which at depth is astronomically small —
            // deep enough and it cannot move a single bit of a double-double result, so computing it is
            // pure cost. Decide with a cheap double-precision magnitude estimate and only pay for the
            // double-double multiplies when the term can actually matter.
            var deltaMagSq = delta.Re.Hi * delta.Re.Hi + delta.Im.Hi * delta.Im.Hi;
            var linearMagSq = (d1.Re.Hi * d1.Re.Hi + d1.Im.Hi * d1.Im.Hi) * deltaMagSq;
            var d2 = reference.Deriv2Half[n];
            var scale = Math.ScaleB(1.0, scaleExp);
            var quadMagSq = scale * scale * (d2.Re.Hi * d2.Re.Hi + d2.Im.Hi * d2.Im.Hi) * deltaMagSq * deltaMagSq;
            delta = quadMagSq > 1.0e-68 * linearMagSq
                ? linear + DdComplex.ScaleB(d2 * delta * delta, scaleExp)
                : linear;
            return;
        }

        // Exact step — no Taylor truncation at all:
        //   d' = f(Z+d) - f(Z) = Z^p * expm1(p * (Log(Z+d) - Log(Z)))
        // The log difference is built from log1p/atan2 of u, so small d loses nothing to cancellation,
        // and unlike a truncated series this stays exact as |d| approaches |Z| — which is what lets the
        // zoom cross the band where a 2nd-order expansion falls apart.
        var u = dTrue / z;
        var uSq = u.Re * u.Re + u.Im * u.Im;
        var reDL = DoubleDouble.LogP1(u.Re * 2.0 + uSq) * 0.5;
        var imDL = DoubleDouble.Atan2(u.Im, DoubleDouble.One + u.Re);
        var theta = DoubleDouble.Atan2(z.Im, z.Re) + imDL;
        if (theta > DoubleDouble.Pi || !(theta > -DoubleDouble.Pi))
        {
            // The offset point crossed the principal branch cut of z^p, so Z^p*(1+u)^p is on a different
            // branch than the principal (Z+d)^p that direct iteration takes. Here d is large, so a plain
            // difference has no cancellation problem — just take it directly.
            delta = DdComplex.Pow(z + dTrue, powerReal, powerImag) - reference.Pow[n];
        }
        else
        {
            var w = new DdComplex(reDL * powerReal - imDL * powerImag, imDL * powerReal + reDL * powerImag);
            var sinHalf = DoubleDouble.SinCos(w.Im * 0.5).Sin;
            var (sinW, cosW) = DoubleDouble.SinCos(w.Im);
            var em1 = new DdComplex(
                DoubleDouble.ExpM1(w.Re) * cosW - sinHalf * sinHalf * 2.0,
                DoubleDouble.Exp(w.Re) * sinW);
            delta = reference.Pow[n] * em1;
        }
        scaleExp = 0; // d is representable directly now, so drop the rescaling
    }

    public static int IteratePixel(DeepReference reference,
        double deltaCReal, double deltaCImag, int scaleExp, int maxIterations,
        double leafOffset, out double trap, out double finalMagnitudeSquared, out bool isGlitch)
    {
        var delta = new DdComplex(deltaCReal, deltaCImag);
        trap = 1e5;
        finalMagnitudeSquared = 1.0;
        isGlitch = false;
        // Mirror BStyleColoring.BStyle's loop bound exactly: it runs `for (iters = 1; iters < N; iters++)`,
        // so it advances at most N-1 times and its final z is z_{N-1}, not z_N. Taking one extra step here
        // changes the interior pixels' final |z| and therefore their smooth-iteration colour — which
        // showed up as every pixel differing from the direct engine by the identical constant.
        var limit = Math.Min(maxIterations - 1, reference.Length - 1);
        var nonPerturbativeSteps = 0;
        for (var n = 0; n < limit; n++)
        {
            PerturbStep(reference, n, ref delta, ref scaleExp);
            if (LastStepLeftPerturbativeRegime) nonPerturbativeSteps++;

            // The reconstruction that used to be the whole problem. Both operands carry ~32 digits, so
            // even when they cancel down to 1e-3 of their own size the result still keeps ~29 — the
            // regime that produced "glitched" pixels in plain double simply is not reachable here.
            var v = reference.Z[n + 1] + DdComplex.ScaleB(delta, scaleExp);
            var magnitude = v.MagnitudeSquared;
            finalMagnitudeSquared = magnitude;
            if (magnitude > BoundSquared) return n + 1;

            if (magnitude < GlitchFactor * reference.MagnitudeSquared[n + 1])
            {
                isGlitch = true;
                return n + 1;
            }

            // Error budget, not a magnitude heuristic. Every step taken outside the perturbative regime
            // amplifies this pixel's rounding error by the map's Lyapunov factor (~1.6x, measured via
            // --deeppixeltrace), so double-double's ~32 digits are spent after about
            // MaxNonPerturbativeSteps of them. Past that the pixel is tracking noise and needs a rebased
            // local reference — which keeps the delta small and stops the amplification at its source.
            // Cancellation (the check above) is a different, rarer failure; this is the one that actually
            // spoils deep pixels, and adjudicating disputed pixels against full arbitrary-precision
            // iteration is what showed it.
            if (nonPerturbativeSteps > MaxNonPerturbativeSteps)
            {
                isGlitch = true;
                return n + 1;
            }
            trap = Math.Min(trap, BStyleColoring.LeafTrapValue(v.Re.ToDouble(), v.Im.ToDouble(), leafOffset));

            var maxAbs = Math.Max(Math.Abs(delta.Re.Hi), Math.Abs(delta.Im.Hi));
            if (maxAbs > RenormThreshold)
            {
                var k = Math.ILogB(maxAbs);
                delta = DdComplex.ScaleB(delta, -k);
                scaleExp += k;
            }
        }
        return maxIterations;
    }

    public static void Render(BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        BigDecimalCompat juliaReal, BigDecimalCompat juliaImag, double powerReal, double powerImag,
        double zooms, int width, int height,
        int maxIterations, double leafOffset, double colorR, double colorG, double colorB,
        float[] outputRgb, CancellationToken token = default, bool orbitTrap = true,
        DeepReference? sharedReference = null)
    {
        var spacingLog2 = 2.0 - zooms;
        var scaleExp = (int)Math.Floor(spacingLog2);
        var mantissa = Math.Pow(2.0, spacingLog2 - scaleExp);
        var offsetToDelta = mantissa / width;
        var centerX = width / 2.0;
        var centerY = height / 2.0;

        // Building the reference is arbitrary-precision work and dominates a deep frame's cost (measured
        // ~60 of 65s at zoom 164 — the double-precision pixel loop is about a second of it). It depends
        // only on the center, not the zoom, apart from needing *at least* this zoom's digits. So a caller
        // walking a zoom sequence at one fixed center can build it once at the deepest zoom and hand the
        // same orbit to every frame.
        var swReference = System.Diagnostics.Stopwatch.StartNew();
        var reference = sharedReference ?? BuildReference(centerReal, centerImag, juliaReal, juliaImag,
            powerReal, powerImag, zooms, maxIterations, token);
        swReference.Stop();
        var swPixels = System.Diagnostics.Stopwatch.StartNew();

        var glitch = new bool[width * height];
        Parallel.For(0, height, new ParallelOptions { CancellationToken = token }, y =>
        {
            var deltaCImag = (centerY - y) * offsetToDelta;
            var rowBase = y * width;
            for (var x = 0; x < width; x++)
            {
                var deltaCReal = (x - centerX) * offsetToDelta;
                var idx = rowBase + x;
                var iters = IteratePixel(reference, deltaCReal, deltaCImag, scaleExp, maxIterations,
                    leafOffset, out var trap, out var finalMag, out var isGlitch);
                glitch[idx] = isGlitch;
                if (isGlitch) continue;
                WriteColor(outputRgb, idx, orbitTrap, trap, iters, finalMag, colorR, colorG, colorB);
            }
        });

        swPixels.Stop();
        if (DebugLogging) Console.WriteLine($"[deep] initial glitches: {glitch.Count(g => g)}/{glitch.Length}");
        var swRepair = System.Diagnostics.Stopwatch.StartNew();
        RepairGlitches(centerReal, centerImag, juliaReal, juliaImag, powerReal, powerImag, zooms, width, height,
            maxIterations, scaleExp, offsetToDelta, centerX, centerY, leafOffset, colorR, colorG, colorB,
            orbitTrap, outputRgb, glitch, token);
        swRepair.Stop();
        if (DebugLogging)
            Console.WriteLine($"[deep] TIMING reference={swReference.ElapsedMilliseconds}ms pixels={swPixels.ElapsedMilliseconds}ms repair={swRepair.ElapsedMilliseconds}ms (repairReferences={RepairReferenceBuilds})");
    }

    static void WriteColor(float[] outputRgb, int pixelIndex, bool orbitTrap, double trap, int iters,
        double finalMag, double colorR, double colorG, double colorB)
    {
        var outer = orbitTrap ? trap : 0.0;
        var (r, g, b) = BStyleColoring.ColorFromTrap(outer, iters, finalMag, colorR, colorG, colorB);
        var i = pixelIndex * 3;
        outputRgb[i + 0] = (float)r;
        outputRgb[i + 1] = (float)g;
        outputRgb[i + 2] = (float)b;
    }

    /// <summary>
    /// Re-iterates glitched pixels against fresh, spatially-local reference orbits, repeatedly, until
    /// they resolve or the rebase budget is exhausted. Unlike a single global rebase anchor (which only
    /// helps pixels close to that one point — measured to resolve roughly 1 pixel per pass out of tens
    /// of thousands, useless), this tiles the glitched pixels and gives each tile its own reference
    /// centred on the tile, so every pixel gets an anchor that's actually near it. Tiles shrink each pass
    /// (halving down to a floor) so persistent glitches get progressively tighter, cheaper-to-satisfy
    /// anchors instead of retrying the same distance forever.
    /// </summary>
    static void RepairGlitches(BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        BigDecimalCompat juliaReal, BigDecimalCompat juliaImag, double powerReal, double powerImag,
        double zooms, int width, int height, int maxIterations, int scaleExp, double offsetToDelta,
        double centerX, double centerY, double leafOffset, double colorR, double colorG, double colorB,
        bool orbitTrap, float[] outputRgb, bool[] glitch, CancellationToken token)
    {
        var tileSize = RepairTileSize;
        var previousGlitchCount = int.MaxValue;
        var stalledPasses = 0;
        for (var pass = 0; pass < MaxRebasePasses; pass++)
        {
            token.ThrowIfCancellationRequested();
            var glitchCount = 0;
            for (var i = 0; i < glitch.Length; i++)
                if (glitch[i]) glitchCount++;
            if (glitchCount == 0) return;
            // Stop once rebasing stops helping. A handful of pixels can sit on a genuine cancellation
            // that no nearby anchor resolves, and each further pass still pays for a full arbitrary-
            // precision reference per glitched tile — minutes of work to fix nothing.
            // Only judge that while the tiles are already at their floor: a pass that fixes nothing at a
            // large tile size says nothing about the tighter anchors the next pass will try, and giving
            // up there abandons pixels that were about to be repaired.
            if (glitchCount >= previousGlitchCount && tileSize <= MinRepairTileSize)
            {
                if (++stalledPasses >= 2) break;
            }
            else stalledPasses = 0;
            previousGlitchCount = glitchCount;
            if (DebugLogging) Console.WriteLine($"[deep] repair pass {pass}: {glitchCount} glitched, tileSize={tileSize}");

            var scaleBig = BigDecimalCompat.Pow2(scaleExp);
            var roundContext = new MathContextCompat(Math.Max(40, (int)(zooms * 0.30103) + 24));
            var tilesX = (width + tileSize - 1) / tileSize;
            var tilesY = (height + tileSize - 1) / tileSize;

            Parallel.For(0, tilesX * tilesY, new ParallelOptions { CancellationToken = token }, tileIndex =>
            {
                var tx = tileIndex % tilesX;
                var ty = tileIndex / tilesX;
                var x0 = tx * tileSize;
                var x1 = Math.Min(width, x0 + tileSize);
                var y0 = ty * tileSize;
                var y1 = Math.Min(height, y0 + tileSize);

                var anyGlitch = false;
                for (var y = y0; y < y1 && !anyGlitch; y++)
                    for (var x = x0; x < x1; x++)
                        if (glitch[y * width + x]) { anyGlitch = true; break; }
                if (!anyGlitch) return;

                var gx = (x0 + x1 - 1) / 2.0;
                var gy = (y0 + y1 - 1) / 2.0;
                var newCenterReal = centerReal.Add(
                    BigDecimalCompat.FromDouble((gx - centerX) * offsetToDelta).Multiply(scaleBig, roundContext), roundContext);
                var newCenterImag = centerImag.Add(
                    BigDecimalCompat.FromDouble((centerY - gy) * offsetToDelta).Multiply(scaleBig, roundContext), roundContext);

                Interlocked.Increment(ref RepairReferenceBuilds);
                var tileReference = BuildReference(newCenterReal, newCenterImag, juliaReal, juliaImag,
                    powerReal, powerImag, zooms, maxIterations, token);

                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var idx = y * width + x;
                        if (!glitch[idx]) continue;
                        var deltaCReal = (x - gx) * offsetToDelta;
                        var deltaCImag = (gy - y) * offsetToDelta;
                        var iters = IteratePixel(tileReference, deltaCReal, deltaCImag, scaleExp, maxIterations,
                            leafOffset, out var trap, out var finalMag, out var isGlitch);
                        if (isGlitch) continue;
                        glitch[idx] = false;
                        WriteColor(outputRgb, idx, orbitTrap, trap, iters, finalMag, colorR, colorG, colorB);
                    }
                }
            });

            // Halving buys only a small, roughly-constant number of extra safe iterations per halving
            // (each halving shrinks the worst-case seed offset by 2x, and against ~2.3x/iteration chaotic
            // growth that's worth <1 extra iteration) while quadrupling the reference-build cost — not
            // worth chasing below a modest floor. Below that floor, remaining glitches get the "best
            // effort" fallback colour rather than burning the rebase budget on diminishing returns.
            if (tileSize > MinRepairTileSize) tileSize = Math.Max(MinRepairTileSize, tileSize / 2);
        }

        // Anything still glitched after the budget: best-effort interior colour rather than left blank.
        for (var i = 0; i < glitch.Length; i++)
        {
            if (!glitch[i]) continue;
            WriteColor(outputRgb, i, orbitTrap, 0.0, maxIterations, 1.0, colorR, colorG, colorB);
        }
    }

    /// <summary>
    /// Finds a deep zoom center via small probe renders scored by colour variety (mirrors the proven
    /// shallow ShaderTest heuristic), instead of a single pixel's raw escape count. Robust against
    /// locally-uniform "flat" plateaus where a single-pixel-max search has nothing to distinguish
    /// (confirmed needed: the raw escape-count grid search got stuck in exactly such a plateau — an
    /// entire 10000-point grid tied at the same escape count, so tie-breaking just picked whichever grid
    /// corner happened to be scanned first). Each candidate gets a full <see cref="Render"/> (glitch
    /// repair included, so a candidate never fails outright the way a raw un-repaired grid scan does),
    /// scored by distinct-colour count; the search recenters on the richest one.
    /// </summary>
    public static (BigDecimalCompat real, BigDecimalCompat imag) FindDeepCenterByVariety(
        BigDecimalCompat juliaReal, BigDecimalCompat juliaImag, double powerReal, double powerImag,
        BigDecimalCompat startReal, BigDecimalCompat startImag, double startZooms,
        int steps, double stepZoom, int candidateGrid, int probeSize, int maxIterations, double leafOffset,
        double colorR, double colorG, double colorB,
        Action<int, double, BigDecimalCompat, BigDecimalCompat, int>? onStep = null, double searchRadiusFraction = 0.3)
    {
        var centerReal = startReal;
        var centerImag = startImag;
        var zooms = startZooms;
        for (var step = 0; step < steps; step++)
        {
            var spacingLog2 = 2.0 - zooms;
            var scaleExp = (int)Math.Floor(spacingLog2);
            var mantissa = Math.Pow(2.0, spacingLog2 - scaleExp) * searchRadiusFraction;
            var half = candidateGrid / 2.0;
            var offsetToDelta = mantissa / candidateGrid;
            var precision = Math.Max(40, (int)(zooms * 0.30103) + 30);
            var context = new MathContextCompat(precision);
            var power = BigDecimalCompat.Pow2(scaleExp);

            var candidates = new (double dcr, double dci)[candidateGrid * candidateGrid];
            var idx = 0;
            for (var cy = 0; cy < candidateGrid; cy++)
                for (var cx = 0; cx < candidateGrid; cx++)
                    candidates[idx++] = ((cx - half) * offsetToDelta, (half - cy) * offsetToDelta);

            var scores = new int[candidates.Length];
            Parallel.For(0, candidates.Length, ci =>
            {
                var (dcr, dci) = candidates[ci];
                var candReal = centerReal.Add(BigDecimalCompat.FromDouble(dcr).Multiply(power, context), context);
                var candImag = centerImag.Add(BigDecimalCompat.FromDouble(dci).Multiply(power, context), context);
                var rgb = new float[probeSize * probeSize * 3];
                Render(candReal, candImag, juliaReal, juliaImag, powerReal, powerImag, zooms, probeSize, probeSize,
                    maxIterations, leafOffset, colorR, colorG, colorB, rgb);
                scores[ci] = CountDistinctColorsRgb(rgb, probeSize, probeSize);
            });

            var bestIdx = 0;
            for (var i = 1; i < scores.Length; i++) if (scores[i] > scores[bestIdx]) bestIdx = i;
            var (bestDcr, bestDci) = candidates[bestIdx];
            centerReal = centerReal.Add(BigDecimalCompat.FromDouble(bestDcr).Multiply(power, context), context);
            centerImag = centerImag.Add(BigDecimalCompat.FromDouble(bestDci).Multiply(power, context), context);
            zooms += stepZoom;
            onStep?.Invoke(step, zooms, centerReal, centerImag, scores[bestIdx]);
        }
        return (centerReal, centerImag);
    }

    static int CountDistinctColorsRgb(float[] rgb, int width, int height)
    {
        var seen = new HashSet<int>();
        for (var y = 0; y < height; y += 2)
            for (var x = 0; x < width; x += 2)
            {
                var i = (y * width + x) * 3;
                var r = (byte)Math.Clamp((int)(rgb[i + 0] * 255), 0, 255);
                var g = (byte)Math.Clamp((int)(rgb[i + 1] * 255), 0, 255);
                var b = (byte)Math.Clamp((int)(rgb[i + 2] * 255), 0, 255);
                seen.Add((r << 16) | (g << 8) | b);
            }
        return seen.Count;
    }

    /// <summary>
    /// Follows the Julia boundary inward in arbitrary precision: at each step the highest-escape pixel
    /// in the current view becomes the new center (its offset added to the center in BigDecimal), then
    /// zooms in. Yields a deep, high-precision center that stays on the fractal at the target depth.
    /// Glitched grid points are excluded from candidacy (their escape count is not trustworthy).
    /// </summary>
    public static (BigDecimalCompat real, BigDecimalCompat imag) FindDeepCenter(
        BigDecimalCompat juliaReal, BigDecimalCompat juliaImag, double powerReal, double powerImag,
        BigDecimalCompat startReal, BigDecimalCompat startImag, double startZooms,
        int steps, double stepZoom, int gridResolution, int maxIterations, double leafOffset,
        Action<int, double, BigDecimalCompat, BigDecimalCompat, int>? onStep = null, double searchRadiusFraction = 1.0)
    {
        var centerReal = startReal;
        var centerImag = startImag;
        var zooms = startZooms;
        for (var step = 0; step < steps; step++)
        {
            var spacingLog2 = 2.0 - zooms;
            var scaleExp = (int)Math.Floor(spacingLog2);
            var mantissa = Math.Pow(2.0, spacingLog2 - scaleExp) * searchRadiusFraction;
            var offsetToDelta = mantissa / gridResolution;
            var half = gridResolution / 2.0;

            var reference = BuildReference(centerReal, centerImag, juliaReal, juliaImag, powerReal, powerImag,
                zooms, maxIterations, CancellationToken.None);

            // Score candidates by *local gradient* (how much escape count differs from its 4 neighbours),
            // not raw escape count. A single highest-escape pixel is a poor guide once the neighbourhood
            // is dynamically uniform (confirmed empirically: an entire 10000-point grid tied at the same
            // escape count, and tie-breaking just walked to whichever grid corner happened to scan
            // first — a flat plateau, not a boundary). The gradient favours an actual crossing in the
            // dynamics — points near the true Julia boundary, where nearby starting points diverge in
            // fate — over a uniform interior/exterior basin that merely takes a while to decide.
            var escapeGrid = new int[gridResolution, gridResolution];
            var glitchGrid = new bool[gridResolution, gridResolution];
            Parallel.For(0, gridResolution, gy =>
            {
                var dci = (half - gy) * offsetToDelta;
                for (var gx = 0; gx < gridResolution; gx++)
                {
                    var dcr = (gx - half) * offsetToDelta;
                    var iters = IteratePixel(reference, dcr, dci, scaleExp, maxIterations, leafOffset,
                        out _, out _, out var isGlitch);
                    escapeGrid[gy, gx] = isGlitch ? -1 : iters;
                    glitchGrid[gy, gx] = isGlitch;
                }
            });

            var glitchCount = 0;
            var validCount = 0;
            (int escape, double dcr, double dci, int gradient) overall = (-1, 0, 0, -1);
            for (var gy = 1; gy < gridResolution - 1; gy++)
                for (var gx = 1; gx < gridResolution - 1; gx++)
                {
                    if (glitchGrid[gy, gx]) { glitchCount++; continue; }
                    validCount++;
                    var e = escapeGrid[gy, gx];
                    // Confirmed-interior (e==maxIterations) is still allowed as a candidate: a jump from
                    // "escapes at N" right next to "never escapes" is itself a genuine boundary crossing,
                    // arguably the strongest one — excluding it just throws away that signal.
                    var grad = 0;
                    var considered = 0;
                    if (!glitchGrid[gy - 1, gx]) { grad += Math.Abs(escapeGrid[gy - 1, gx] - e); considered++; }
                    if (!glitchGrid[gy + 1, gx]) { grad += Math.Abs(escapeGrid[gy + 1, gx] - e); considered++; }
                    if (!glitchGrid[gy, gx - 1]) { grad += Math.Abs(escapeGrid[gy, gx - 1] - e); considered++; }
                    if (!glitchGrid[gy, gx + 1]) { grad += Math.Abs(escapeGrid[gy, gx + 1] - e); considered++; }
                    if (considered == 0) continue;
                    if (grad > overall.gradient || (grad == overall.gradient && e > overall.escape))
                        overall = (e, (gx - half) * offsetToDelta, (half - gy) * offsetToDelta, grad);
                }
            if (DebugLogging)
            {
                var gridSpan = half * offsetToDelta;
                Console.WriteLine($"    [search] grid={gridResolution * gridResolution} valid={validCount} glitched={glitchCount} gradient={overall.gradient} chosenOffset=({overall.dcr:E2},{overall.dci:E2}) gridSpan={gridSpan:E2} fracOfSpan={(gridSpan > 0 ? Math.Sqrt(overall.dcr * overall.dcr + overall.dci * overall.dci) / gridSpan : 0):P1}");
            }
            if (overall.escape < 0) break;

            // Add the chosen offset to the center in arbitrary precision: offset = dc * 2^scaleExp.
            var precision = Math.Max(40, (int)(zooms * 0.30103) + 30);
            var context = new MathContextCompat(precision);
            var power = BigDecimalCompat.Pow2(scaleExp);
            centerReal = centerReal.Add(BigDecimalCompat.FromDouble(overall.dcr).Multiply(power, context), context);
            centerImag = centerImag.Add(BigDecimalCompat.FromDouble(overall.dci).Multiply(power, context), context);
            zooms += stepZoom;
            onStep?.Invoke(step, zooms, centerReal, centerImag, overall.escape);
        }
        return (centerReal, centerImag);
    }
}
