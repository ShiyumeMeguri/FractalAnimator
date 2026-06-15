using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FractalAnimator.Core;

namespace FractalAnimator.Keyframes;

/// <summary>
/// One keyframe of a FractalParadise-style fractal, rendered by direct iteration with the orbit-trap
/// B-style coloring (no perturbation — these formulas are shallow art, so double precision over the
/// view is plenty). The view is a square of full width <c>2^(2 - zooms)</c> centered at
/// (<c>centerX</c>, <c>centerY</c>), matching the zoom convention of the perturbation keyframes so it
/// drops straight into the same video pipeline. Rows are rendered across all cores.
/// </summary>
public sealed class ShaderFractalImage : FractalImage
{
    readonly ShaderColorParams _params;
    readonly double _centerX, _centerY, _zooms;
    readonly int _width, _height;
    Bitmap? _bitmap;

    public ShaderFractalImage(ShaderColorParams parameters, double centerX, double centerY,
        double zooms, int width, int height)
    {
        _params = parameters;
        _centerX = centerX;
        _centerY = centerY;
        _zooms = zooms;
        _width = width;
        _height = height;
        scale = new FractalScale(zooms);
    }

    public override Bitmap GetImage()
    {
        lock (this)
        {
            if (_bitmap != null) return _bitmap;
            _bitmap = Render(CancellationToken.None);
            return _bitmap;
        }
    }

    public Bitmap Render(CancellationToken token)
    {
        var halfWidth = Math.Pow(2.0, 1.0 - _zooms);
        var bitmap = new Bitmap(_width, _height, PixelFormat.Format24bppRgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        var stride = data.Stride;
        var scan0 = data.Scan0;
        var invW = _width > 1 ? 2.0 / (_width - 1) : 0.0;
        var invH = _height > 1 ? 2.0 / (_height - 1) : 0.0;
        try
        {
            Parallel.For(0, _height, new ParallelOptions { CancellationToken = token }, y =>
            {
                var rowData = new byte[_width * 3];
                var verticalUnit = (_height - 1 - y) * invH - 1.0; // flip so +imaginary is up
                var cy = _centerY + verticalUnit * halfWidth;
                for (var x = 0; x < _width; x++)
                {
                    var horizontalUnit = x * invW - 1.0;
                    var cx = _centerX + horizontalUnit * halfWidth;
                    var (r, g, b) = BStyleColoring.Color(new Cplx(cx, cy), _params);
                    var index = x * 3;
                    rowData[index + 0] = ToByte(b);
                    rowData[index + 1] = ToByte(g);
                    rowData[index + 2] = ToByte(r);
                }
                Marshal.Copy(rowData, 0, IntPtr.Add(scan0, y * stride), _width * 3);
            });
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    static byte ToByte(double channel)
    {
        if (double.IsNaN(channel)) return 0;
        return (byte)Math.Clamp((int)Math.Round(channel * 255.0), 0, 255);
    }

    public override void Dispose()
    {
        lock (this) { _bitmap?.Dispose(); _bitmap = null; }
    }
}

/// <summary>
/// A bounded zoom sequence of <see cref="ShaderFractalImage"/> keyframes, one per power-of-two level
/// from <c>startZooms</c> to <c>endZooms</c>, rendered lazily and cached.
/// </summary>
public sealed class ShaderFractalLoader : ImageLoader
{
    readonly ShaderColorParams _params;
    readonly double _centerX, _centerY, _startZooms;
    readonly int _width, _height, _count;
    readonly FractalImage?[] _cache;
    readonly object _lock = new();

    public ShaderFractalLoader(ShaderColorParams parameters, double centerX, double centerY,
        double startZooms, double endZooms, int width = 1000, int height = 1000)
    {
        _params = parameters;
        _centerX = centerX;
        _centerY = centerY;
        _startZooms = startZooms;
        _width = width;
        _height = height;
        _count = Math.Max(1, (int)Math.Ceiling(endZooms - startZooms) + 1);
        _cache = new FractalImage?[_count];
    }

    public override int Size() => _count;

    public override FractalImage Get(int index)
    {
        lock (_lock)
        {
            return _cache[index] ??= new ShaderFractalImage(_params, _centerX, _centerY,
                _startZooms + index, _width, _height);
        }
    }
}
