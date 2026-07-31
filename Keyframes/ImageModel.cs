using System.Collections;
using System.Drawing;
using FractalAnimator.Core;

namespace FractalAnimator.Keyframes;

public abstract class FractalImage : IComparable<FractalImage>, IDisposable
{
    protected FractalScale scale = new(0);
    public FractalScale GetScale() => scale;
    public int CompareTo(FractalImage? o) => o is null ? 1 : scale.GetZooms().CompareTo(o.scale.GetZooms());
    public abstract Bitmap GetImage();
    public virtual Bitmap GetImage(double ratio) => GetImage();
    public abstract void Dispose();
}

/// <summary>
/// A source that can render the fractal at an arbitrary, non-integer zoom and at arbitrary output
/// dimensions — i.e. every video frame is computed at exactly the zoom that frame is supposed to show.
///
/// This exists so the video renderer never has to fake in-between frames by scaling a neighbouring
/// keyframe. Scaling is a quality downgrade (the frame is a magnified lower-resolution image, not the
/// fractal at that depth), and for an offline render there is no reason to accept it.
/// </summary>
public interface IExactZoomRenderer
{
    /// <summary>Renders at exactly <paramref name="zooms"/>, natively at the requested pixel size.</summary>
    Bitmap RenderAtZoom(double zooms, int width, int height, CancellationToken token);

    /// <summary>Maps the interpolator's (fractional) keyframe index onto an absolute zoom level.</summary>
    double ZoomsForIndex(double index);
}

public abstract class ImageLoader : IEnumerable<FractalImage>
{
    public abstract FractalImage Get(int index);
    public abstract int Size();

    /// <summary>
    /// True when the keyframe sequence is effectively endless (generated on demand). Unbounded loaders
    /// must not be probed at <see cref="GetLast"/>, because that would force the deepest possible frame.
    /// </summary>
    public virtual bool IsUnbounded => false;
    public FractalImage? Acquire(int index) { try { return Get(index); } catch { return null; } }
    public FractalImage GetFrameAtZoom(double zoom) { for (var i = 0; i < Size(); i++) { var f = Get(i); if (f.GetScale().GetZooms() > zoom) return Get(i - 1); } return Get(Size() - 1); }
    public IEnumerator<FractalImage> GetEnumerator() { for (var i = 0; i < Size(); i++) yield return Get(i); }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public FractalImage GetFirst() => Get(0);
    public FractalImage GetLast() => Get(Size() - 1);
    public FractalScale GetFirstScale() => GetFirst().GetScale();
    public FractalScale GetLastScale() => GetLast().GetScale();
}

public sealed class ImageFileFractalImage : FractalImage
{
    readonly string _path;
    Bitmap? _image;
    public ImageFileFractalImage(string file, FractalScale s) { _path = file; scale = s; }
    public override Bitmap GetImage()
    {
        if (_image == null) { using var fs = File.OpenRead(_path); var raw = new Bitmap(fs); _image = new Bitmap(raw); raw.Dispose(); }
        return _image;
    }
    public override void Dispose() { _image?.Dispose(); _image = null; }
}
