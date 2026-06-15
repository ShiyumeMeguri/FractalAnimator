using System.Numerics;

namespace FractalAnimator.Core;

/// <summary>
/// Outcome of iterating a single pixel against a reference orbit.
/// </summary>
public enum PixelStatus
{
    Escaped,
    Interior,
    Glitch,
}

/// <summary>
/// Deep-zoom fractal renderer built on perturbation theory with a rescaled delta, so the reachable
/// zoom depth is not bounded by <see cref="double"/>'s exponent range. The pipeline is:
///
///   1. Build one high-precision reference orbit at the image center (arbitrary precision only when
///      the zoom actually needs it; plain double otherwise).
///   2. For every pixel iterate the <i>rescaled</i> perturbation delta. The true delta is
///      <c>delta * 2^scaleExp</c> where <c>scaleExp</c> is tracked as an integer and the delta is
///      renormalized back into a safe magnitude whenever it grows too large. While the zoom is so
///      deep that <c>2^scaleExp</c> underflows a double, the higher-order terms simply vanish — which
///      is exactly the correct linear approximation at that depth.
///   3. Detect glitched pixels (Pauldelbrot criterion) and re-iterate them against fresh references.
///
/// The inner pixel loop runs in two flavors: a fully vectorized <see cref="System.Numerics.Vector{T}"/>
/// path when the formula implements <see cref="ISimdFractal"/> and the hardware is accelerated, and a
/// scalar fallback otherwise. Rows are distributed across all cores.
/// </summary>
public static class PerturbationEngine
{
    // Pixel bailout radius. Large so smooth coloring stays continuous.
    const double Bailout = 256.0;
    const double BailoutSquared = Bailout * Bailout;

    // The reference orbit is kept until it grows clearly past every pixel's bailout.
    const double ReferenceEscapeSquared = 1.0e6;

    // Pauldelbrot glitch criterion: |z| collapsed far below the reference magnitude.
    const double GlitchFactor = 1.0e-6;

    // Renormalize the rescaled delta once it grows past this magnitude.
    const double RenormThreshold = 1.0e15;

    const int MaxRebasePasses = 24;

    public static int VectorLanes => Vector<double>.Count;
    public static bool SimdAccelerated => Vector.IsHardwareAccelerated && Vector<double>.Count > 1;

    /// <summary>Test hook: forces the scalar pixel loop, used to validate the SIMD path against it.</summary>
    public static bool ForceScalar;

    /// <summary>
    /// Renders one keyframe into <paramref name="output"/> (length <c>width * height</c>). Each entry
    /// holds the smooth (fractional) escape iteration, or -1 for interior points.
    /// </summary>
    public static void Render(IFractal fractal, BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        double zooms, int width, int height, int maxIterations, float[] output, CancellationToken token = default)
    {
        // size = 4 / magnification = 2^(2 - zooms); pixels are square at spacing = size / width.
        var spacingLog2 = 2.0 - zooms;
        var scaleExp = (int)Math.Floor(spacingLog2);
        var mantissa = Math.Pow(2.0, spacingLog2 - scaleExp); // 2^frac in [1, 2)
        var offsetToDelta = mantissa / width;                 // rescaled-delta units per pixel
        var centerX = width / 2.0;
        var centerY = height / 2.0;

        var dynamical = fractal.DynamicalPlane;
        BuildReference(fractal, centerReal, centerImag, zooms, maxIterations, dynamical,
            out var referenceReal, out var referenceImag, out var referenceLength, token);

        var useSimd = !ForceScalar && SimdAccelerated && fractal is ISimdFractal;
        var simd = fractal as ISimdFractal;

        Parallel.For(0, height, new ParallelOptions { CancellationToken = token }, y =>
        {
            var deltaCImag = (centerY - y) * offsetToDelta;
            var rowBase = y * width;
            var x = 0;
            if (useSimd)
            {
                var lanes = Vector<double>.Count;
                Span<double> deltaCRealLane = stackalloc double[lanes];
                for (; x + lanes <= width; x += lanes)
                {
                    for (var l = 0; l < lanes; l++)
                        deltaCRealLane[l] = (x + l - centerX) * offsetToDelta;
                    IterateGroupSimd(simd!, referenceReal, referenceImag, referenceLength,
                        new Vector<double>(deltaCRealLane), new Vector<double>(deltaCImag),
                        scaleExp, maxIterations, dynamical, output, rowBase + x);
                }
            }
            for (; x < width; x++)
            {
                var deltaCReal = (x - centerX) * offsetToDelta;
                var status = IteratePixel(fractal, referenceReal, referenceImag, referenceLength,
                    deltaCReal, deltaCImag, scaleExp, maxIterations, dynamical, out var smooth);
                output[rowBase + x] = status == PixelStatus.Glitch ? float.NaN : (float)smooth;
            }
        });

        RepairGlitches(fractal, centerReal, centerImag, zooms, width, height, maxIterations,
            scaleExp, offsetToDelta, centerX, centerY, dynamical, output, token);
    }

    static void BuildReference(IFractal fractal, BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        double zooms, int maxIterations, bool dynamical, out double[] referenceReal, out double[] referenceImag,
        out int length, CancellationToken token)
    {
        referenceReal = new double[maxIterations + 1];
        referenceImag = new double[maxIterations + 1];
        var last = 0;
        if (zooms <= 50.0)
        {
            var creal = centerReal.ToDouble();
            var cimag = centerImag.ToDouble();
            double zr = dynamical ? creal : 0;
            double zi = dynamical ? cimag : 0;
            referenceReal[0] = zr;
            referenceImag[0] = zi;
            for (var n = 0; n < maxIterations; n++)
            {
                fractal.ReferenceIterate(ref zr, ref zi, creal, cimag);
                referenceReal[n + 1] = zr;
                referenceImag[n + 1] = zi;
                last = n + 1;
                if (zr * zr + zi * zi > ReferenceEscapeSquared) break;
            }
        }
        else
        {
            var precision = (int)(zooms * 0.30103) + 24;
            var context = new MathContextCompat(precision);
            var zr = dynamical ? centerReal : BigDecimalCompat.Zero;
            var zi = dynamical ? centerImag : BigDecimalCompat.Zero;
            referenceReal[0] = zr.ToDouble();
            referenceImag[0] = zi.ToDouble();
            for (var n = 0; n < maxIterations; n++)
            {
                token.ThrowIfCancellationRequested();
                fractal.ReferenceIteratePrecise(ref zr, ref zi, centerReal, centerImag, context);
                var rd = zr.ToDouble();
                var id = zi.ToDouble();
                referenceReal[n + 1] = rd;
                referenceImag[n + 1] = id;
                last = n + 1;
                if (rd * rd + id * id > ReferenceEscapeSquared) break;
            }
        }
        length = last + 1;
    }

    /// <summary>
    /// Scalar perturbation of a single pixel against the given reference orbit.
    /// </summary>
    static PixelStatus IteratePixel(IFractal fractal, double[] referenceReal, double[] referenceImag,
        int referenceLength, double deltaCReal, double deltaCImag, int scaleExp, int maxIterations,
        bool seedWithOffset, out double smooth)
    {
        var deltaReal = seedWithOffset ? deltaCReal : 0.0;
        var deltaImag = seedWithOffset ? deltaCImag : 0.0;
        var deltaCR = deltaCReal;
        var deltaCI = deltaCImag;
        var limit = Math.Min(maxIterations, referenceLength - 1);
        for (var n = 0; n < limit; n++)
        {
            var zr = referenceReal[n];
            var zi = referenceImag[n];
            var scale = Math.ScaleB(1.0, scaleExp);
            fractal.PerturbIterate(ref deltaReal, ref deltaImag, zr, zi, deltaCR, deltaCI, scale);

            var zr1 = referenceReal[n + 1];
            var zi1 = referenceImag[n + 1];
            var vr = zr1 + scale * deltaReal;
            var vi = zi1 + scale * deltaImag;
            var magnitude = vr * vr + vi * vi;
            if (magnitude > BailoutSquared)
            {
                smooth = SmoothValue(n + 1, magnitude);
                return PixelStatus.Escaped;
            }
            var referenceMagnitude = zr1 * zr1 + zi1 * zi1;
            if (magnitude < GlitchFactor * referenceMagnitude)
            {
                smooth = 0;
                return PixelStatus.Glitch;
            }
            var maxAbs = Math.Max(Math.Abs(deltaReal), Math.Abs(deltaImag));
            if (maxAbs > RenormThreshold)
            {
                var k = Math.ILogB(maxAbs);
                var factor = Math.ScaleB(1.0, -k);
                deltaReal *= factor;
                deltaImag *= factor;
                deltaCR *= factor;
                deltaCI *= factor;
                scaleExp += k;
            }
        }
        smooth = -1;
        return limit >= maxIterations ? PixelStatus.Interior : PixelStatus.Glitch;
    }

    /// <summary>
    /// Vectorized perturbation of <see cref="Vector{T}.Count"/> adjacent pixels at once.
    /// </summary>
    static void IterateGroupSimd(ISimdFractal fractal, double[] referenceReal, double[] referenceImag,
        int referenceLength, Vector<double> deltaCRealInit, Vector<double> deltaCImagInit,
        int scaleExp, int maxIterations, bool seedWithOffset, float[] output, int outputBase)
    {
        var lanes = Vector<double>.Count;
        var deltaReal = seedWithOffset ? deltaCRealInit : Vector<double>.Zero;
        var deltaImag = seedWithOffset ? deltaCImagInit : Vector<double>.Zero;
        var deltaCReal = deltaCRealInit;
        var deltaCImag = deltaCImagInit;
        var zero = Vector<double>.Zero;
        var renormVector = new Vector<double>(RenormThreshold);
        var bailoutVector = new Vector<double>(BailoutSquared);
        var glitchVector = new Vector<double>(GlitchFactor);
        var allDone = new Vector<long>(-1);

        var doneMask = Vector<long>.Zero;
        var escapeIteration = Vector<double>.Zero;
        var escapeMagnitude = Vector<double>.Zero;

        var limit = Math.Min(maxIterations, referenceLength - 1);
        for (var n = 0; n < limit; n++)
        {
            var zr = new Vector<double>(referenceReal[n]);
            var zi = new Vector<double>(referenceImag[n]);
            var scale = new Vector<double>(Math.ScaleB(1.0, scaleExp));
            fractal.PerturbIterateSimd(ref deltaReal, ref deltaImag, zr, zi, deltaCReal, deltaCImag, scale);

            var zr1 = new Vector<double>(referenceReal[n + 1]);
            var zi1 = new Vector<double>(referenceImag[n + 1]);
            var vr = zr1 + scale * deltaReal;
            var vi = zi1 + scale * deltaImag;
            var magnitude = vr * vr + vi * vi;

            var newlyEscaped = Vector.AndNot(Vector.GreaterThan(magnitude, bailoutVector), doneMask);
            if (!Vector.EqualsAll(newlyEscaped, Vector<long>.Zero))
            {
                escapeIteration = Vector.ConditionalSelect(newlyEscaped, new Vector<double>(n + 1), escapeIteration);
                escapeMagnitude = Vector.ConditionalSelect(newlyEscaped, magnitude, escapeMagnitude);
                doneMask = Vector.BitwiseOr(doneMask, newlyEscaped);
                deltaReal = Vector.ConditionalSelect(doneMask, zero, deltaReal);
                deltaImag = Vector.ConditionalSelect(doneMask, zero, deltaImag);
                deltaCReal = Vector.ConditionalSelect(doneMask, zero, deltaCReal);
                deltaCImag = Vector.ConditionalSelect(doneMask, zero, deltaCImag);
                if (Vector.EqualsAll(doneMask, allDone)) break;
            }

            var maxAbs = Vector.Max(Vector.Abs(deltaReal), Vector.Abs(deltaImag));
            if (Vector.GreaterThanAny(maxAbs, renormVector))
            {
                double maxValue = 0;
                for (var l = 0; l < lanes; l++)
                    maxValue = Math.Max(maxValue, maxAbs[l]);
                var k = Math.ILogB(maxValue);
                var factor = new Vector<double>(Math.ScaleB(1.0, -k));
                deltaReal *= factor;
                deltaImag *= factor;
                deltaCReal *= factor;
                deltaCImag *= factor;
                scaleExp += k;
            }
        }

        var interiorByReference = limit >= maxIterations;
        for (var l = 0; l < lanes; l++)
        {
            var index = outputBase + l;
            if (doneMask[l] != 0)
                output[index] = (float)SmoothValue((int)escapeIteration[l], escapeMagnitude[l]);
            else
                output[index] = interiorByReference ? -1f : float.NaN;
        }
    }

    /// <summary>
    /// Smooth (fractional) iteration count for continuous coloring of an escaped point.
    /// </summary>
    static double SmoothValue(int iteration, double magnitude)
    {
        var logZ = 0.5 * Math.Log(magnitude);
        var nu = Math.Log(logZ / Math.Log(Bailout)) / Math.Log(2.0);
        return iteration - nu;
    }

    /// <summary>
    /// Re-iterates glitched pixels (marked NaN) against fresh reference orbits, repeatedly, until they
    /// resolve or the rebase budget is exhausted. Glitches that never resolve are colored as interior.
    /// </summary>
    static void RepairGlitches(IFractal fractal, BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        double zooms, int width, int height, int maxIterations, int scaleExp,
        double offsetToDelta, double centerX, double centerY, bool dynamical, float[] output, CancellationToken token)
    {
        for (var pass = 0; pass < MaxRebasePasses; pass++)
        {
            token.ThrowIfCancellationRequested();
            var glitchPixel = -1;
            var glitchCount = 0;
            for (var i = 0; i < output.Length; i++)
            {
                if (!float.IsNaN(output[i])) continue;
                glitchCount++;
                if (glitchPixel < 0) glitchPixel = i;
            }
            if (glitchCount == 0) return;

            // Pick a reference among the glitched pixels (a deterministic interior sample).
            glitchPixel = NthGlitch(output, glitchCount / 8);
            var gx = glitchPixel % width;
            var gy = glitchPixel / width;

            // True coordinate of the new reference center, computed in arbitrary precision.
            var scaleBig = BigDecimalCompat.Pow2(scaleExp);
            var roundContext = new MathContextCompat(Math.Max(40, (int)(zooms * 0.30103) + 24));
            var newCenterReal = centerReal.Add(
                BigDecimalCompat.FromDouble((gx - centerX) * offsetToDelta).Multiply(scaleBig, roundContext), roundContext);
            var newCenterImag = centerImag.Add(
                BigDecimalCompat.FromDouble((centerY - gy) * offsetToDelta).Multiply(scaleBig, roundContext), roundContext);

            BuildReference(fractal, newCenterReal, newCenterImag, zooms, maxIterations, dynamical,
                out var referenceReal, out var referenceImag, out var referenceLength, token);

            Parallel.For(0, height, new ParallelOptions { CancellationToken = token }, y =>
            {
                var rowBase = y * width;
                for (var x = 0; x < width; x++)
                {
                    var index = rowBase + x;
                    if (!float.IsNaN(output[index])) continue;
                    var deltaCReal = (x - gx) * offsetToDelta;
                    var deltaCImag = (gy - y) * offsetToDelta;
                    var status = IteratePixel(fractal, referenceReal, referenceImag, referenceLength,
                        deltaCReal, deltaCImag, scaleExp, maxIterations, dynamical, out var smooth);
                    if (status != PixelStatus.Glitch)
                        output[index] = (float)smooth;
                }
            });
        }

        // Anything still glitched after the budget is treated as interior.
        for (var i = 0; i < output.Length; i++)
            if (float.IsNaN(output[i]))
                output[i] = -1f;
    }

    static int NthGlitch(float[] output, int n)
    {
        var seen = 0;
        for (var i = 0; i < output.Length; i++)
        {
            if (!float.IsNaN(output[i])) continue;
            if (seen == n) return i;
            seen++;
        }
        for (var i = 0; i < output.Length; i++)
            if (float.IsNaN(output[i]))
                return i;
        return 0;
    }
}
