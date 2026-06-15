using System.Drawing;
using System.Globalization;
using System.Text.Json.Nodes;
using FractalAnimator.Core;

namespace FractalAnimator.Keyframes;

public sealed class KfPngImageLoader : ImageLoader
{
    readonly List<FractalImage> _list;
    public KfPngImageLoader(string dir)
    {
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir).Where(p => Regexes.KfPng.IsMatch(Path.GetFileName(p))).ToArray() : [];
        if (files.Length == 0) throw new FileNotFoundException("Directory invalid.");
        _list = []; foreach (var f in files) { var m = Regexes.KfPng.Match(Path.GetFileName(f)); if (m.Success) _list.Add(new ImageFileFractalImage(f, FractalScale.FromMagnification(m.Groups[1].Value))); }
        _list.Sort();
    }
    public override FractalImage Get(int i) => _list[i];
    public override int Size() => _list.Count;
}

public sealed class FzImageLoader : ImageLoader
{
    readonly List<FractalImage> _list;
    public FzImageLoader(string dir)
    {
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir).Where(p => Regexes.FzImage.IsMatch(Path.GetFileName(p))).ToArray() : [];
        if (files.Length == 0) throw new FileNotFoundException("Directory invalid.");
        _list = []; foreach (var f in files)
        {
            var info = Path.Combine(Path.GetDirectoryName(f) ?? "", Utils.RemoveExtension(f) + ".info.json");
            var obj = JsonNode.Parse(File.ReadAllText(info))!.AsObject();
            _list.Add(new ImageFileFractalImage(f, FractalScale.FromSize(obj["size"]!.GetValue<string>())));
        }
        _list.Sort();
    }
    public override FractalImage Get(int i) => _list[i];
    public override int Size() => _list.Count;
}

public sealed class KfbLoader : ImageLoader
{
    readonly List<FractalImage> _list;
    public KfbLoader(string dir)
    {
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir).Where(p => Regexes.Kfb.IsMatch(Path.GetFileName(p))).ToArray() : [];
        if (files.Length == 0) throw new FileNotFoundException("Directory invalid.");
        var kfr = Directory.GetFiles(dir, "*.kfr").FirstOrDefault() ?? throw new FileNotFoundException("Missing .kfr");
        var pal = new Palette();
        foreach (var line in File.ReadLines(kfr))
        {
            if (!line.Trim().StartsWith("Colors", StringComparison.Ordinal)) continue;
            var bga = line.Trim().Split(':')[1].Trim().Split(',');
            var c = new int[bga.Length / 3][];
            for (var i = 0; i < bga.Length; i += 3)
                c[i / 3] = [int.Parse(bga[i + 2]), int.Parse(bga[i + 1]), int.Parse(bga[i])];
            pal = new Palette(c);
        }
        var palCopy = pal;
        _list = [];
        foreach (var f in files)
        {
            var m = Regexes.Kfb.Match(Path.GetFileName(f));
            if (m.Success) _list.Add(new KfbImage(f, FractalScale.FromMagnification(m.Groups[1].Value), palCopy));
        }
        _list.Sort();
    }
    public override FractalImage Get(int i) => _list[i];
    public override int Size() => _list.Count;

    sealed class KfbImage : FractalImage
    {
        readonly string _path; readonly Palette _pal; int[][]? _iters; int _maxIter; Bitmap? _image;
        public KfbImage(string f, FractalScale s, Palette p) { _path = f; scale = s; _pal = p; }
        public override Bitmap GetImage() { if (_iters == null) Load(); return _image!; }
        public override Bitmap GetImage(double r) => GetImage();
        public override void Dispose() { _image?.Dispose(); _image = null; _iters = null; }
        void Load()
        {
            using var s = File.OpenRead(_path); using var r = new BinaryReader(s);
            r.ReadBytes(3); _maxIter = 0;
            var w = r.ReadInt32(); var h = r.ReadInt32();
            _iters = new int[w][]; for (var x = 0; x < w; x++) { _iters[x] = new int[h]; for (var y = 0; y < h; y++) { var it = r.ReadInt32(); _maxIter = Math.Max(_maxIter, it); _iters[x][y] = Math.Max(it, 0); } }
            _image = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            for (var x = 0; x < w; x++) for (var y = 0; y < h; y++) { var it = _iters[x][y]; SetPixel(_image, x, y, it == _maxIter ? 0 : _pal.GetColor(it)); }
        }
        static void SetPixel(Bitmap b, int x, int y, int rgb)
        {
            var rect = new Rectangle(0, 0, b.Width, b.Height);
            var d = b.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            unsafe { var p = (byte*)d.Scan0 + y * d.Stride + x * 3; p[0] = (byte)(rgb & 0xFF); p[1] = (byte)((rgb >> 8) & 0xFF); p[2] = (byte)((rgb >> 16) & 0xFF); }
            b.UnlockBits(d);
        }
    }
}

public sealed class MapleMandelMakerLoader : ImageLoader
{
    readonly List<FractalImage> _list;
    public MapleMandelMakerLoader(string cfg)
    {
        _list = []; var m = PropertiesFile.Load(cfg); var init = FractalScale.FromMagnification(m["zoom"]);
        var imgDir = Path.Combine(Path.GetDirectoryName(cfg) ?? "", m["path"]);
        if (Directory.Exists(imgDir)) { var i = 0; while (true) { var f = Path.Combine(imgDir, i.ToString("D8") + ".png"); if (!File.Exists(f)) break; _list.Add(new ImageFileFractalImage(f, new FractalScale(init.GetZooms() - i))); i++; } }
        _list.Reverse();
    }
    public override FractalImage Get(int i) => _list[i];
    public override int Size() => _list.Count;
}

/// <summary>
/// One deep-zoom keyframe rendered on demand by the SIMD perturbation engine and colored with smooth
/// (continuous) iteration values. <c>zooms</c> is the log2 magnification, so depth is unbounded.
/// </summary>
public sealed class PerturbationFractalImage : FractalImage
{
    readonly IFractal _fractal;
    readonly BigDecimalCompat _centerReal, _centerImag;
    readonly double _zooms, _colorDensity;
    readonly int _width, _height, _maxIterations;
    Bitmap? _bitmap;

    public PerturbationFractalImage(IFractal fractal, BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        double zooms, int width, int height, int maxIterations, double colorDensity)
    {
        _fractal = fractal;
        _centerReal = centerReal;
        _centerImag = centerImag;
        _zooms = zooms;
        _width = width;
        _height = height;
        _maxIterations = maxIterations;
        _colorDensity = colorDensity;
        scale = new FractalScale(zooms);
    }

    public override Bitmap GetImage()
    {
        lock (this)
        {
            if (_bitmap != null) return _bitmap;
            var buffer = new float[_width * _height];
            PerturbationEngine.Render(_fractal, _centerReal, _centerImag, _zooms,
                _width, _height, _maxIterations, buffer);
            _bitmap = FractalColoring.Colorize(buffer, _width, _height, _colorDensity);
            return _bitmap;
        }
    }

    public override void Dispose()
    {
        lock (this) { _bitmap?.Dispose(); _bitmap = null; }
    }
}

/// <summary>
/// A bounded sequence of deep-zoom keyframes (one per power-of-two zoom level) produced by a
/// runtime-compiled formula. Frames are rendered lazily and cached.
/// </summary>
public class FractalScriptLoader : ImageLoader
{
    readonly IFractal _fractal;
    readonly BigDecimalCompat _centerReal, _centerImag;
    readonly int _width, _height, _baseIterations, _count;
    readonly double _colorDensity, _iterationsPerZoom;
    readonly FractalImage?[] _cache;
    readonly object _lock = new();

    public const double DefaultColorDensity = 1.0 / 6.0;

    public FractalScriptLoader(IFractal fractal, BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        double targetZooms, int maxIterations, int width = 1000, int height = 1000,
        double colorDensity = DefaultColorDensity, double iterationsPerZoom = 0)
    {
        _fractal = fractal;
        _centerReal = centerReal;
        _centerImag = centerImag;
        _baseIterations = maxIterations;
        _width = width;
        _height = height;
        _colorDensity = colorDensity;
        _iterationsPerZoom = iterationsPerZoom;
        _count = Math.Max(1, (int)Math.Floor(targetZooms) + 2);
        _cache = new FractalImage?[_count];
    }

    public override int Size() => _count;

    public override FractalImage Get(int index)
    {
        lock (_lock)
        {
            if (_cache[index] != null) return _cache[index]!;
            var zooms = index - 1.0;
            var iterations = _baseIterations + (int)(Math.Max(0, zooms) * _iterationsPerZoom);
            return _cache[index] = new PerturbationFractalImage(_fractal, _centerReal, _centerImag,
                zooms, _width, _height, iterations, _colorDensity);
        }
    }
}

/// <summary>
/// The built-in Mandelbrot quick-start, sharing the same engine and addressing as the script loader.
/// </summary>
public sealed class SimpleMandelbrotLoader : FractalScriptLoader
{
    public SimpleMandelbrotLoader(BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        double targetZooms, int maxIterations, int width = 1000, int height = 1000)
        : base(FractalCompiler.CreateBuiltIn(0), centerReal, centerImag, targetZooms, maxIterations, width, height)
    {
    }
}

/// <summary>
/// An effectively endless zoom: keyframes are generated on demand to any depth, with iteration counts
/// that grow as the zoom deepens. A sliding cache keeps memory bounded; rendering continues until the
/// user aborts (or the very large keyframe budget is reached).
/// </summary>
public sealed class InfiniteFractalLoader : ImageLoader
{
    readonly IFractal _fractal;
    readonly BigDecimalCompat _centerReal, _centerImag;
    readonly int _width, _height, _baseIterations, _count;
    readonly double _colorDensity, _iterationsPerZoom;
    readonly Dictionary<int, FractalImage> _cache = new();
    readonly object _lock = new();

    const int CacheWindow = 24;

    public InfiniteFractalLoader(IFractal fractal, BigDecimalCompat centerReal, BigDecimalCompat centerImag,
        int maxKeyframes, int baseIterations, double iterationsPerZoom,
        int width = 1000, int height = 1000, double colorDensity = FractalScriptLoader.DefaultColorDensity)
    {
        _fractal = fractal;
        _centerReal = centerReal;
        _centerImag = centerImag;
        _count = Math.Max(1, maxKeyframes);
        _baseIterations = baseIterations;
        _iterationsPerZoom = iterationsPerZoom;
        _width = width;
        _height = height;
        _colorDensity = colorDensity;
    }

    public override int Size() => _count;
    public override bool IsUnbounded => true;

    public override FractalImage Get(int index)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(index, out var cached)) return cached;
            var zooms = index - 1.0;
            var iterations = _baseIterations + (int)(Math.Max(0, zooms) * _iterationsPerZoom);
            var image = new PerturbationFractalImage(_fractal, _centerReal, _centerImag,
                zooms, _width, _height, iterations, _colorDensity);
            _cache[index] = image;
            EvictBelow(index - CacheWindow);
            return image;
        }
    }

    void EvictBelow(int threshold)
    {
        if (_cache.Count <= CacheWindow * 2) return;
        var stale = _cache.Keys.Where(k => k < threshold).ToList();
        foreach (var key in stale)
        {
            _cache[key].Dispose();
            _cache.Remove(key);
        }
    }
}
