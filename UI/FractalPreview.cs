using System.Drawing.Drawing2D;
using FractalAnimator.Core;
using FractalAnimator.Keyframes;

namespace FractalAnimator.UI;

/// <summary>
/// An inline, always-on fractal preview. Each <see cref="RenderPreview"/> cancels the previous render
/// and computes a fresh tile on a background thread (so the UI stays responsive), then shows it. The
/// surface is interactive like a viewer: double-click recenters, the mouse wheel zooms.
/// </summary>
public sealed class FractalPreview : Panel
{
    Bitmap? _bitmap;
    CancellationTokenSource? _cts;
    string _status = "Live preview";
    bool _busy;

    /// <summary>Double-click target as a fraction of the view from center: x right-positive, y up-positive, range ±0.5.</summary>
    public event Action<double, double>? PointPicked;

    /// <summary>Mouse-wheel zoom in levels (positive = deeper).</summary>
    public event Action<int>? ZoomChanged;

    public FractalPreview()
    {
        DoubleBuffered = true;
        BackColor = Theme.SurfaceAlt;
        Cursor = Cursors.Cross;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = false;
        MouseEnter += (_, _) => Focus();
        MouseDoubleClick += (_, e) => RaisePoint(e.Location);
        MouseWheel += (_, e) => ZoomChanged?.Invoke(e.Delta > 0 ? 2 : -2);
    }

    /// <summary>
    /// Renders a fresh preview tile in the background. The caller supplies a render function that, given
    /// a square pixel size and a cancellation token, returns the colored bitmap — so any fractal engine
    /// can drive the same preview surface.
    /// </summary>
    public void RenderPreview(Func<int, CancellationToken, Bitmap> render)
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var size = Math.Clamp(Math.Min(ClientSize.Width, ClientSize.Height), 64, 512);
        if (size <= 0) size = 256;
        _busy = true;
        _status = "rendering…";
        Invalidate();
        var token = cts.Token;
        Task.Run(() =>
        {
            try
            {
                var bitmap = render(size, token);
                token.ThrowIfCancellationRequested();
                if (!IsHandleCreated || token.IsCancellationRequested) { bitmap?.Dispose(); return; }
                BeginInvoke(() =>
                {
                    if (cts != _cts) { bitmap?.Dispose(); return; }
                    _bitmap?.Dispose();
                    _bitmap = bitmap;
                    _busy = false;
                    _status = "";
                    Invalidate();
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!IsHandleCreated) return;
                try { BeginInvoke(() => { _busy = false; _status = ex.Message.Split('\n')[0]; Invalidate(); }); }
                catch { }
            }
        }, token);
    }

    void RaisePoint(Point location)
    {
        if (_bitmap == null) return;
        var rect = FitRect();
        if (!rect.Contains(location)) return;
        var fractionX = (location.X - rect.X) / (double)rect.Width - 0.5;
        var fractionY = 0.5 - (location.Y - rect.Y) / (double)rect.Height;
        PointPicked?.Invoke(fractionX, fractionY);
    }

    Rectangle FitRect()
    {
        if (_bitmap == null) return ClientRectangle;
        var scale = Math.Min((double)ClientSize.Width / _bitmap.Width, (double)ClientSize.Height / _bitmap.Height);
        var w = Math.Max(1, (int)(_bitmap.Width * scale));
        var h = Math.Max(1, (int)(_bitmap.Height * scale));
        return new Rectangle((ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(Theme.SurfaceAlt);
        if (_bitmap != null)
        {
            e.Graphics.InterpolationMode = InterpolationMode.Bilinear;
            e.Graphics.DrawImage(_bitmap, FitRect());
        }
        if (!string.IsNullOrEmpty(_status))
        {
            using var brush = new SolidBrush(_busy ? Theme.Accent : Theme.Bad);
            e.Graphics.DrawString(_status, Font, brush, 6, 6);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts?.Cancel();
            _bitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}
