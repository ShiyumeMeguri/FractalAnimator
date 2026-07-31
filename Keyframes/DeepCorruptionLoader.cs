using System.Drawing;
using System.Drawing.Imaging;
using FractalAnimator.Core;

namespace FractalAnimator.Keyframes;

/// <summary>
/// One keyframe of the 腐化监视 Julia set (power -2.23) rendered by <see cref="DeepOrbitTrapEngine"/> —
/// arbitrary-precision reference orbit + double-precision 2nd-order perturbation, so unlike
/// <see cref="ShaderFractalImage"/> it is not bounded by double's ~10^15 precision floor. The center is
/// held in <see cref="BigDecimalCompat"/> rather than double for exactly that reason.
/// </summary>
public sealed class DeepCorruptionImage : FractalImage
{
    readonly ShaderColorParams _params;
    readonly BigDecimalCompat _centerX, _centerY, _juliaX, _juliaY;
    readonly double _zooms;
    readonly int _width, _height;
    readonly DeepOrbitTrapEngine.DeepReference? _sharedReference;
    Bitmap? _bitmap;

    public DeepCorruptionImage(ShaderColorParams parameters, BigDecimalCompat centerX, BigDecimalCompat centerY,
        double zooms, int width, int height, DeepOrbitTrapEngine.DeepReference? sharedReference = null)
    {
        _params = parameters;
        _centerX = centerX;
        _centerY = centerY;
        _juliaX = BigDecimalCompat.FromDouble(parameters.JuliaCXRaw * 1e-5);
        _juliaY = BigDecimalCompat.FromDouble(parameters.JuliaCYRaw * 1e-5);
        _zooms = zooms;
        _width = width;
        _height = height;
        _sharedReference = sharedReference;
        scale = new FractalScale(zooms);
    }

    public override Bitmap GetImage()
    {
        lock (this)
        {
            if (_bitmap != null) return _bitmap;
            _bitmap = Render();
            return _bitmap;
        }
    }

    public Bitmap Render(CancellationToken token = default)
    {
        var rgb = new float[_width * _height * 3];
        DeepOrbitTrapEngine.Render(_centerX, _centerY, _juliaX, _juliaY, _params.PowerReal, _params.PowerImag,
            _zooms, _width, _height, _params.Iterations, _params.LeafOrbitOffset, _params.ColorR, _params.ColorG, _params.ColorB,
            rgb, token, true, _sharedReference);
        return RgbToBitmap(rgb, _width, _height);
    }

    static Bitmap RgbToBitmap(float[] rgb, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                var row = (byte*)data.Scan0 + y * data.Stride;
                for (var x = 0; x < width; x++)
                {
                    var i = (y * width + x) * 3;
                    row[x * 3 + 0] = ToByte(rgb[i + 2]);
                    row[x * 3 + 1] = ToByte(rgb[i + 1]);
                    row[x * 3 + 2] = ToByte(rgb[i + 0]);
                }
            }
        }
        bitmap.UnlockBits(data);
        return bitmap;
    }

    static byte ToByte(float v) => (byte)Math.Clamp((int)Math.Round((double.IsNaN(v) ? 0.0 : v) * 255.0), 0, 255);

    public override void Dispose()
    {
        lock (this) { _bitmap?.Dispose(); _bitmap = null; }
    }
}

/// <summary>
/// A bounded zoom sequence of <see cref="DeepCorruptionImage"/> keyframes, one per zoom level from
/// <c>startZooms</c> to <c>endZooms</c>, rendered lazily and cached — the arbitrary-precision sibling of
/// <see cref="ShaderFractalLoader"/>, for zoom ranges that exceed what direct double iteration can reach.
/// </summary>
public sealed class DeepCorruptionLoader : ImageLoader, IExactZoomRenderer
{
    // Perturbation only pays off (and is only numerically reliable) once the view is tight enough that
    // per-pixel deltas are small. At a wide/shallow view nearly every pixel's delta is "large" relative
    // to the reference, so the 2nd-order approximation glitches almost everywhere and needs a massive
    // tiled repair for something direct double iteration already renders instantly and exactly (measured:
    // a shallow keyframe through the perturbation engine took 100x+ longer than a genuinely deep one).
    // Below this verified-safe threshold (see --stablecenter), use the fast direct engine instead.
    public const double ShallowDeepThreshold = 45.0;

    readonly ShaderColorParams _params;
    readonly BigDecimalCompat _centerX, _centerY;
    readonly double _centerXDouble, _centerYDouble;
    readonly double _startZooms;
    readonly int _width, _height, _count;
    readonly FractalImage?[] _cache;
    readonly object _lock = new();
    readonly long _startTicks = Environment.TickCount64;
    readonly double _endZooms;
    DeepOrbitTrapEngine.DeepReference? _sharedReference;
    int _rendered;

    public DeepCorruptionLoader(ShaderColorParams parameters, BigDecimalCompat centerX, BigDecimalCompat centerY,
        double startZooms, double endZooms, int width = 1000, int height = 1000)
    {
        _params = parameters;
        _centerX = centerX;
        _centerY = centerY;
        _centerXDouble = centerX.ToDouble();
        _centerYDouble = centerY.ToDouble();
        _startZooms = startZooms;
        _endZooms = endZooms;
        _width = width;
        _height = height;
        _count = Math.Max(1, (int)Math.Ceiling(endZooms - startZooms) + 1);
        _cache = new FractalImage?[_count];
    }

    public override int Size() => _count;

    public double ZoomsForIndex(double index) => _startZooms + index;

    /// <summary>
    /// Renders one frame at exactly this zoom, at full output resolution — no keyframe, no scaling.
    /// The deep engine's reference orbit is shared across every frame (it depends on the center, not the
    /// zoom), so rendering at an arbitrary zoom costs no more than rendering at an integer one.
    /// </summary>
    public Bitmap RenderAtZoom(double zooms, int width, int height, CancellationToken token)
    {
        if (zooms < ShallowDeepThreshold)
            return new ShaderFractalImage(_params, _centerXDouble, _centerYDouble, zooms, width, height).Render(token);

        _sharedReference ??= DeepOrbitTrapEngine.BuildReference(_centerX, _centerY,
            BigDecimalCompat.FromDouble(_params.JuliaCXRaw * 1e-5), BigDecimalCompat.FromDouble(_params.JuliaCYRaw * 1e-5),
            _params.PowerReal, _params.PowerImag, _endZooms, _params.Iterations, token);
        return new DeepCorruptionImage(_params, _centerX, _centerY, zooms, width, height, _sharedReference).Render(token);
    }

    public override FractalImage Get(int index)
    {
        lock (_lock)
        {
            if (_cache[index] is { } cached) return cached;
            var zooms = _startZooms + index;
            var engine = zooms < ShallowDeepThreshold ? "fast" : "deep";
            var before = Environment.TickCount64;
            FractalImage image;
            if (zooms < ShallowDeepThreshold)
                image = new ShaderFractalImage(_params, _centerXDouble, _centerYDouble, zooms, _width, _height);
            else
            {
                // Every deep keyframe shares one center, so they can all share one reference orbit —
                // built once at the deepest zoom of the sequence (the most digits any frame will need).
                _sharedReference ??= DeepOrbitTrapEngine.BuildReference(_centerX, _centerY,
                    BigDecimalCompat.FromDouble(_params.JuliaCXRaw * 1e-5), BigDecimalCompat.FromDouble(_params.JuliaCYRaw * 1e-5),
                    _params.PowerReal, _params.PowerImag, _endZooms, _params.Iterations, CancellationToken.None);
                image = new DeepCorruptionImage(_params, _centerX, _centerY, zooms, _width, _height, _sharedReference);
            }
            image.GetImage(); // force render now (not on first video-frame access) so timing/progress is accurate
            var thisMs = Environment.TickCount64 - before;
            var done = Interlocked.Increment(ref _rendered);
            Console.WriteLine($"[keyframe {done}/{_count}] zooms={zooms:0.0} engine={engine} took={thisMs / 1000.0:0.0}s total={(Environment.TickCount64 - _startTicks) / 1000.0:0.0}s");
            return _cache[index] = image;
        }
    }
}
