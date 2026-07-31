using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using FractalAnimator.Core;
using FractalAnimator.Interp;
using FractalAnimator.Keyframes;

namespace FractalAnimator.Rendering;

public interface IScaleIndicator
{
    void Draw(Graphics g, FractalScale scale, int width, int height);
    void SetScale(double s);
    void SetFont(Font f);
}

public sealed class KfScaleIndicator : IScaleIndicator
{
    Font _font = FontRepository.Instance.CreateRoboto(48); double _scale = 1;
    public void Draw(Graphics g, FractalScale s, int w, int h)
    {
        using var f = new Font(_font.FontFamily, (float)(48 * _scale), FontStyle.Regular, GraphicsUnit.Pixel);
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        var t = "Zoom: " + s.GetMagnification(); var off = (int)(48 * _scale / 12); var x = (int)(8 * _scale);
        g.DrawString(t, f, Brushes.Black, x + off, (float)(48 * _scale + off));
        g.DrawString(t, f, Brushes.White, x, (float)(48 * _scale));
    }
    public void SetScale(double s) => _scale = s;
    public void SetFont(Font f) => _font = f;
}

public sealed class FxScaleIndicator : IScaleIndicator
{
    Font _font = FontRepository.Instance.CreateRoboto(24); double _scale = 1;
    public void Draw(Graphics g, FractalScale s, int w, int h)
    {
        using var f = new Font(_font.FontFamily, (float)(24 * _scale), FontStyle.Regular, GraphicsUnit.Pixel);
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        var t = string.Format(CultureInfo.InvariantCulture, "{0:F2} zooms", s.GetZooms());
        var off = (int)(24 * _scale / 12); var x = (int)(w - 8 * _scale);
        var sz = g.MeasureString(t, f); var dx = x - sz.Width;
        g.DrawString(t, f, Brushes.Black, dx + off, (float)(h - 8 * _scale + off));
        g.DrawString(t, f, Brushes.White, dx, (float)(h - 8 * _scale));
    }
    public void SetScale(double s) => _scale = s;
    public void SetFont(Font f) => _font = f;
}

public sealed class ExponentialScaleIndicator : IScaleIndicator
{
    double _scale = 1;
    Font _font = FontRepository.Instance.CreateRoboto(64);
    public void Draw(Graphics g, FractalScale s, int w, int h)
    {
        const int bs = 64; var es = bs / 2; var m = (int)(_scale * 8); var off = (int)(_scale * 4);
        var dy = (int)(h - _scale) - m;
        using var bf = new Font(_font.FontFamily, (float)(bs * _scale), FontStyle.Regular, GraphicsUnit.Pixel);
        using var ef = new Font(_font.FontFamily, (float)(es * _scale), FontStyle.Regular, GraphicsUnit.Pixel);
        var bw = g.MeasureString("10", bf).Width; var so = s.GetLog10Scale();
        g.DrawString("10", bf, Brushes.Black, m + off, dy + off);
        g.DrawString(so.ToString("F1", CultureInfo.InvariantCulture), ef, Brushes.Black, m + bw + off / 2f, (float)(dy - _scale * es + off / 2f));
        g.DrawString("10", bf, Brushes.White, m, dy);
        g.DrawString(so.ToString("F1", CultureInfo.InvariantCulture), ef, Brushes.White, m + bw, (float)(dy - _scale * es));
    }
    public void SetScale(double s) => _scale = s;
    public void SetFont(Font f) => _font = f;
}

public sealed class GoogologyIndicator : IScaleIndicator
{
    Font _font = FontRepository.Instance.CreateRoboto(24); double _scale = 1;
    public void Draw(Graphics g, FractalScale s, int w, int h)
    {
        using var f = new Font(_font.FontFamily, (float)(24 * _scale), FontStyle.Regular, GraphicsUnit.Pixel);
        var t = Googology.FormatLog2(s.GetZooms()); var off = (int)(24 * _scale / 12);
        var cx = w / 2; var cy = (int)(24 * _scale);
        var sz = g.MeasureString(t, f); var dx = cx - sz.Width / 2;
        g.DrawString(t, f, Brushes.Black, dx + off, cy + off);
        g.DrawString(t, f, Brushes.White, dx, cy);
    }
    public void SetScale(double s) => _scale = s;
    public void SetFont(Font f) => _font = f;
}

/// <summary>
/// Top-left magnification readout drawn as a power of ten — "10ⁿ" with a raised exponent — instead of
/// scientific notation, so the depth reads at a glance. The exponent is log10 of the magnification.
/// </summary>
public sealed class MagnificationPowerIndicator : IScaleIndicator
{
    Font _font = FontRepository.Instance.CreateRoboto(48);
    double _scale = 1;

    /// <summary>Zoom level treated as "1×". Set to the start zoom so the readout climbs from 10⁰.</summary>
    public double ReferenceZooms { get; set; }

    public void Draw(Graphics g, FractalScale s, int w, int h)
    {
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        var exponent = (s.GetZooms() - ReferenceZooms) * 0.3010299956639812; // log10 of magnification vs the start
        var baseSize = (float)(50 * _scale);
        var expSize = baseSize * 0.6f;
        using var baseFont = new Font(_font.FontFamily, baseSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var expFont = new Font(_font.FontFamily, expSize, FontStyle.Regular, GraphicsUnit.Pixel);
        var margin = (float)(22 * _scale);
        var off = baseSize / 14f;
        const string basePart = "×10";
        var expPart = exponent.ToString("F1", CultureInfo.InvariantCulture);
        var baseWidth = g.MeasureString(basePart, baseFont).Width;
        var expY = margin - expSize * 0.25f;
        g.DrawString(basePart, baseFont, Brushes.Black, margin + off, margin + off);
        g.DrawString(expPart, expFont, Brushes.Black, margin + baseWidth + off, expY + off);
        g.DrawString(basePart, baseFont, Brushes.White, margin, margin);
        g.DrawString(expPart, expFont, Brushes.White, margin + baseWidth, expY);
    }

    public void SetScale(double s) => _scale = s;
    public void SetFont(Font f) => _font = f;
}

public sealed class OdometerIndicator : IScaleIndicator
{
    Bitmap _bm = new(1, 1, PixelFormat.Format24bppRgb);
    Graphics _canvas = Graphics.FromImage(new Bitmap(1, 1, PixelFormat.Format24bppRgb));
    Color _bg = Color.FromArgb(20, 20, 20);
    Font _font = FontRepository.Instance.CreateRoboto(48);
    double _scale = 1;
    const int Margin = 6, ScaleBase = 48, Digits = 6;

    public void Draw(Graphics g, FractalScale s, int w, int h)
    {
        _canvas.Clear(_bg);
        using var f = new Font(_font.FontFamily, (float)(48 * _scale), FontStyle.Regular, GraphicsUnit.Pixel);
        var sv = s.GetLog10Zooms(); var si = (int)sv; var pct = sv % 1;
        var d = new int[Digits];
        for (var i = 0; i < Digits; i++)
        {
            d[i] = si % 10; si /= 10; var nine = true;
            for (var j = i - 1; j >= 0; j--) { if (d[j] != 9) { nine = false; break; } }
            var dx = (int)(_bm.Width - ScaleBase * _scale / 2 * (i + 1) - Margin * _scale * (i + 1));
            using var white = new SolidBrush(Color.White);
            if (i == 0 || nine)
            {
                var yp = (int)((ScaleBase - 8 - pct * ScaleBase) * _scale);
                _canvas.DrawString(d[i].ToString(), f, white, dx, yp);
                var nxt = d[i] + 1; if (nxt > 9) nxt = 0;
                _canvas.DrawString(nxt.ToString(), f, white, dx, (float)(yp + ScaleBase * _scale));
            }
            else _canvas.DrawString(d[i].ToString(), f, white, dx, (float)((ScaleBase - 8) * _scale));
        }
        g.DrawImage(_bm, w / 2 - _bm.Width / 2, h - _bm.Height);
    }
    public void SetScale(double s) { _scale = s; InitCanvas(); }
    public void SetFont(Font f) => _font = f;
    void InitCanvas() { _canvas.Dispose(); _bm.Dispose(); _bm = new((int)(ScaleBase * _scale / 2 * Digits + Margin * _scale * (Digits + 1)), (int)(ScaleBase * _scale)); _canvas = Graphics.FromImage(_bm); _canvas.SmoothingMode = SmoothingMode.AntiAlias; _canvas.TextRenderingHint = TextRenderingHint.AntiAlias; }
}

public sealed class FFmpegProcess
{
    readonly ProcessStartInfo _psi; readonly BlockingCollection<byte[]> _q;
    Process? _proc; Stream? _pipe; Thread? _consumer; volatile bool _alive;
    public FFmpegProcess(int w, int h, double fps, string ffmpeg, string path, string[] extra)
    {
        var args = new List<string> { "-r", fps.ToString("F0", CultureInfo.InvariantCulture), "-colorspace", "bt709", "-pix_fmt", "bgr24", "-f", "rawvideo", "-s", $"{w}x{h}", "-i", "-", "-y", path };
        args.InsertRange(args.Count - 2, extra);
        _psi = new ProcessStartInfo { FileName = ffmpeg, Arguments = string.Join(" ", args.Select(Quote)), RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        _q = new(6);
    }
    public void Start()
    {
        if (_proc != null) throw new InvalidOperationException("Already started.");
        _proc = Process.Start(_psi) ?? throw new InvalidOperationException("Failed to start ffmpeg."); _pipe = _proc.StandardInput.BaseStream;
        StartReader(_proc.StandardOutput); StartReader(_proc.StandardError);
        _alive = true; _consumer = new(() => { try { foreach (var f in _q.GetConsumingEnumerable()) _pipe!.Write(f, 0, f.Length); } catch { } finally { _alive = false; } }) { IsBackground = true }; _consumer.Start();
    }
    public void SubmitFrame(Bitmap frame) { if (_consumer == null || !_alive) throw new InvalidOperationException("Consumer dead."); _q.Add(CopyBgr(frame)); }
    public void Finish() { _q.CompleteAdding(); _consumer?.Join(); _pipe?.Close(); _proc?.WaitForExit(); }
    public void Abort() { try { _q.CompleteAdding(); } catch { } try { _proc?.Kill(true); } catch { } }
    static void StartReader(StreamReader r) { new Thread(() => { while (true) { var l = r.ReadLine(); if (l == null) break; Debug.WriteLine(l); } }) { IsBackground = true }.Start(); }
    static string Quote(string a) => a.Contains(' ') ? '"' + a.Replace("\"", "\\\"") + '"' : a;
    static byte[] CopyBgr(Bitmap b)
    {
        var r = new Rectangle(0, 0, b.Width, b.Height);
        var d = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var wb = b.Width * 3; var packed = new byte[b.Width * b.Height * 3];
        var row = new byte[d.Stride];
        for (var y = 0; y < b.Height; y++) { Marshal.Copy(IntPtr.Add(d.Scan0, y * d.Stride), row, 0, d.Stride); Buffer.BlockCopy(row, 0, packed, y * wb, wb); }
        b.UnlockBits(d); return packed;
    }
}

public sealed class VideoRenderer
{
    int _w, _h; double _fps; Interpolator? _interp; readonly List<IScaleIndicator> _inds = [];
    BlockingCollection<Bitmap>? _pool; volatile int _rendered, _renderedKf, _totalKf; bool _finished;
    double _startTime, _endTime;
    FFmpegProcess? _proc; CancellationTokenSource? _cts;

    public void SetInterpolator(Interpolator v) => _interp = v;

    public void FfmpegRender(ImageLoader mgr, RenderParams p, string file)
    {
        _w = p.Width; _h = p.Height; _fps = p.Fps; _renderedKf = 0; _totalKf = mgr.Size();
        var procs = Environment.ProcessorCount; _pool = new(procs * 2);
        for (var i = 0; i < procs * 2; i++) _pool.Add(new Bitmap(_w, _h, PixelFormat.Format24bppRgb));
        if (_interp == null) throw new InvalidOperationException("Interpolator not set.");
        _proc = new(_w, _h, _fps, p.Ffmpeg, file, p.Param.GetParam()); _proc.Start();
        _startTime = p.StartTime; _endTime = p.EndTime;
        var iScale = _w / 1920.0; foreach (var ind in _inds) ind.SetScale(iScale);
        _cts = new();

        // Every output frame is rendered at exactly its own zoom, natively at the output resolution.
        // There is deliberately no keyframe-scaling path: producing an in-between frame by magnifying a
        // neighbouring keyframe (and cross-fading two of them) makes that frame a blurred upscale of a
        // shallower depth rather than the fractal at the depth it claims to show. For an offline render
        // that trade buys nothing worth having, so it does not exist here.
        if (mgr is not IExactZoomRenderer exact)
            throw new InvalidOperationException(
                $"{mgr.GetType().Name} cannot render at arbitrary zoom. Offline video output requires an " +
                "IExactZoomRenderer source so every frame is computed at its own zoom; scaling keyframes " +
                "to fake in-between frames is not supported.");

        using var tl = new FileStream("timeline.bin", FileMode.Create, FileAccess.Write);
        var first = _interp.Get(0.0);
        for (var i = 0; i < (int)(_fps * _startTime); i++) RenderExactFrame(exact, first, _proc);
        for (var fn = 0; ; fn++)
        {
            _cts.Token.ThrowIfCancellationRequested();
            var t = fn * 1.0 / _fps;
            if (_interp.IsOutside(t)) break;
            var v = _interp.Get(t);
            WriteDoubleBE(tl, v);
            RenderExactFrame(exact, v, _proc);
            _renderedKf = (int)v + 1;
        }
        if (p.EndTime > 0 && !mgr.IsUnbounded)
        {
            var last = (double)(mgr.Size() - 1);
            for (var i = 0; i < (int)(_fps * _endTime); i++) RenderExactFrame(exact, last, _proc);
        }
        _proc.Finish(); _finished = true;
    }

    // Compositing is serial on purpose: System.Drawing Bitmaps and the indicator objects are not
    // thread-safe, and the expensive part (the fractal render itself) is already parallel inside the
    // engine, so this is not the bottleneck.
    void RenderExactFrame(IExactZoomRenderer exact, double index, FFmpegProcess p)
    {
        var zooms = exact.ZoomsForIndex(index);
        using var frame = exact.RenderAtZoom(zooms, _w, _h, _cts!.Token);
        var buf = _pool!.Take(_cts.Token);
        using (var g = Graphics.FromImage(buf))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(frame, 0, 0);
            foreach (var ind in _inds) ind.Draw(g, new FractalScale(zooms), _w, _h);
        }
        Interlocked.Increment(ref _rendered);
        p.SubmitFrame(buf);
        _pool.Add(buf);
    }

    public void AddScaleIndicator(IScaleIndicator i) => _inds.Add(i);
    public int GetRenderedFrames() => _rendered;
    public int GetTotalFrames() => _interp == null ? 0 : (int)((_startTime + _endTime + _interp.GetDuration()) * _fps);
    public int GetRenderedKeyframes() => _renderedKf;
    public int GetTotalKeyframes() => _totalKf;
    public bool IsFinished() => _finished;
    public void Abort() { _cts?.Cancel(); _proc?.Abort(); }
    static void WriteDoubleBE(Stream s, double v) { var b = BitConverter.GetBytes(v); if (BitConverter.IsLittleEndian) Array.Reverse(b); s.Write(b); }
}
