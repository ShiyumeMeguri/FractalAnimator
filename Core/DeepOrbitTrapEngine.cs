namespace FractalAnimator.Core;

/// <summary>
/// Unlimited-depth zoom of a power-2 Julia set with FractalParadise's orbit-trap "B-style" coloring.
///
/// This is the deep-zoom version of <see cref="ShaderFractal"/> — it uses perturbation so the zoom can
/// descend forever instead of dissolving at double precision (~10^13). The high-precision reference
/// orbit is the "reset/rebase" trick: the center is held in arbitrary precision (<see cref="BigDecimalCompat"/>,
/// which needs only multiply/add for power 2 — no transcendentals), each pixel iterates a rescaled
/// delta in double, and the true orbit point is reconstructed as <c>Z_n + scale·delta_n</c> to feed the
/// orbit traps. Every frame is computed fresh at its exact zoom (no keyframe LOD scaling).
/// </summary>
public static class DeepOrbitTrapEngine
{
    const double RenormThreshold = 1.0e15;
    const double BoundSquared = 1024.0;          // |z| > 32, matching the shaders
    const double ReferenceEscapeSquared = 1.0e6;

    public static void BuildReference(BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        BigDecimalCompat juliaReal, BigDecimalCompat juliaImag, double zooms, int maxIterations,
        out double[] referenceReal, out double[] referenceImag, out int length, CancellationToken token)
    {
        referenceReal = new double[maxIterations + 1];
        referenceImag = new double[maxIterations + 1];
        var precision = Math.Max(30, (int)(zooms * 0.30103) + 24);
        var context = new MathContextCompat(precision);
        var two = new BigDecimalCompat(2, 0);
        var zr = centerReal;
        var zi = centerImag;
        referenceReal[0] = zr.ToDouble();
        referenceImag[0] = zi.ToDouble();
        var last = 0;
        for (var n = 0; n < maxIterations; n++)
        {
            token.ThrowIfCancellationRequested();
            // z -> z^2 + cJulia
            var newReal = zr.Pow(2, context).Subtract(zi.Pow(2, context), context).Add(juliaReal, context);
            var newImag = zr.Multiply(zi, context).Multiply(two, context).Add(juliaImag, context);
            zr = newReal;
            zi = newImag;
            var rd = zr.ToDouble();
            var id = zi.ToDouble();
            referenceReal[n + 1] = rd;
            referenceImag[n + 1] = id;
            last = n + 1;
            if (rd * rd + id * id > ReferenceEscapeSquared) break;
        }
        length = last + 1;
    }

    /// <summary>
    /// Perturbs one pixel and returns its escape iteration (or maxIterations for interior points),
    /// the minimum orbit-trap value, and the final orbit magnitude — everything the coloring needs.
    /// </summary>
    public static int IteratePixel(double[] referenceReal, double[] referenceImag, int referenceLength,
        double deltaCReal, double deltaCImag, int scaleExp, int maxIterations, double leafOffset,
        out double trap, out double finalMagnitudeSquared)
    {
        var deltaReal = deltaCReal;
        var deltaImag = deltaCImag;
        trap = 1e5;
        finalMagnitudeSquared = 1.0;
        var limit = Math.Min(maxIterations, referenceLength - 1);
        for (var n = 0; n < limit; n++)
        {
            var zr = referenceReal[n];
            var zi = referenceImag[n];
            var scale = Math.ScaleB(1.0, scaleExp);
            // Julia (no per-step c): delta' -> 2 Z delta' + scale * delta'^2
            var qr = deltaReal * deltaReal - deltaImag * deltaImag;
            var qi = 2.0 * deltaReal * deltaImag;
            var newReal = 2.0 * (zr * deltaReal - zi * deltaImag) + scale * qr;
            var newImag = 2.0 * (zr * deltaImag + zi * deltaReal) + scale * qi;
            deltaReal = newReal;
            deltaImag = newImag;

            var zr1 = referenceReal[n + 1];
            var zi1 = referenceImag[n + 1];
            var vr = zr1 + scale * deltaReal;
            var vi = zi1 + scale * deltaImag;
            var magnitude = vr * vr + vi * vi;
            finalMagnitudeSquared = magnitude;
            trap = Math.Min(trap, BStyleColoring.LeafTrapValue(vr, vi, leafOffset));
            if (magnitude > BoundSquared) return n + 1;

            var maxAbs = Math.Max(Math.Abs(deltaReal), Math.Abs(deltaImag));
            if (maxAbs > RenormThreshold)
            {
                var k = Math.ILogB(maxAbs);
                var factor = Math.ScaleB(1.0, -k);
                deltaReal *= factor;
                deltaImag *= factor;
                scaleExp += k;
            }
        }
        return maxIterations;
    }

    public static void Render(BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        BigDecimalCompat juliaReal, BigDecimalCompat juliaImag, double zooms, int width, int height,
        int maxIterations, double leafOffset, double colorR, double colorG, double colorB,
        float[] outputRgb, CancellationToken token = default, bool orbitTrap = true)
    {
        var spacingLog2 = 2.0 - zooms;
        var scaleExp = (int)Math.Floor(spacingLog2);
        var mantissa = Math.Pow(2.0, spacingLog2 - scaleExp);
        var offsetToDelta = mantissa / width;
        var centerX = width / 2.0;
        var centerY = height / 2.0;

        BuildReference(centerReal, centerImag, juliaReal, juliaImag, zooms, maxIterations,
            out var referenceReal, out var referenceImag, out var referenceLength, token);

        Parallel.For(0, height, new ParallelOptions { CancellationToken = token }, y =>
        {
            var deltaCImag = (centerY - y) * offsetToDelta;
            var rowBase = y * width * 3;
            for (var x = 0; x < width; x++)
            {
                var deltaCReal = (x - centerX) * offsetToDelta;
                var iters = IteratePixel(referenceReal, referenceImag, referenceLength, deltaCReal, deltaCImag,
                    scaleExp, maxIterations, leafOffset, out var trap, out var finalMag);
                var outer = orbitTrap ? trap : 0.0; // escape-time only when the orbit trap is disabled
                var (r, g, b) = BStyleColoring.ColorFromTrap(outer, iters, finalMag, colorR, colorG, colorB);
                var index = rowBase + x * 3;
                outputRgb[index + 0] = (float)r;
                outputRgb[index + 1] = (float)g;
                outputRgb[index + 2] = (float)b;
            }
        });
    }

    /// <summary>
    /// Follows the Julia boundary inward in arbitrary precision: at each step the highest-escape pixel
    /// in the current view becomes the new center (its offset added to the center in BigDecimal), then
    /// zooms in. Yields a deep, high-precision center that stays on the fractal at the target depth.
    /// </summary>
    public static (BigDecimalCompat real, BigDecimalCompat imag) FindDeepCenter(
        BigDecimalCompat juliaReal, BigDecimalCompat juliaImag,
        BigDecimalCompat startReal, BigDecimalCompat startImag, double startZooms,
        int steps, double stepZoom, int gridResolution, int maxIterations)
    {
        var centerReal = startReal;
        var centerImag = startImag;
        var zooms = startZooms;
        for (var step = 0; step < steps; step++)
        {
            var spacingLog2 = 2.0 - zooms;
            var scaleExp = (int)Math.Floor(spacingLog2);
            var mantissa = Math.Pow(2.0, spacingLog2 - scaleExp);
            var offsetToDelta = mantissa / gridResolution;
            var half = gridResolution / 2.0;

            BuildReference(centerReal, centerImag, juliaReal, juliaImag, zooms, maxIterations,
                out var referenceReal, out var referenceImag, out var referenceLength, CancellationToken.None);

            var rowBest = new (int escape, double dcr, double dci)[gridResolution];
            Parallel.For(0, gridResolution, gy =>
            {
                var dci = (half - gy) * offsetToDelta;
                (int escape, double dcr, double dci) best = (-1, 0, dci);
                for (var gx = 0; gx < gridResolution; gx++)
                {
                    var dcr = (gx - half) * offsetToDelta;
                    var iters = IteratePixel(referenceReal, referenceImag, referenceLength, dcr, dci,
                        scaleExp, maxIterations, 1.0, out _, out _);
                    if (iters < maxIterations && iters > best.escape) best = (iters, dcr, dci);
                }
                rowBest[gy] = best;
            });
            (int escape, double dcr, double dci) overall = (-1, 0, 0);
            foreach (var r in rowBest) if (r.escape > overall.escape) overall = r;
            if (overall.escape < 0) break;

            // Add the chosen offset to the center in arbitrary precision: offset = dc * 2^scaleExp.
            var precision = Math.Max(40, (int)(zooms * 0.30103) + 30);
            var context = new MathContextCompat(precision);
            var power = BigDecimalCompat.Pow2(scaleExp);
            centerReal = centerReal.Add(BigDecimalCompat.FromDouble(overall.dcr).Multiply(power, context), context);
            centerImag = centerImag.Add(BigDecimalCompat.FromDouble(overall.dci).Multiply(power, context), context);
            zooms += stepZoom;
        }
        return (centerReal, centerImag);
    }
}
