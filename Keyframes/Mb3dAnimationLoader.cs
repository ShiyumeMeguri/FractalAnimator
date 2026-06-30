using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FractalAnimator.Core;
using FractalAnimator.Core.Mb3d;
using FractalAnimator.Core.Mb3d.Render;

namespace FractalAnimator.Keyframes;

/// <summary>
/// Loads a Mandelbulb3D animation (<c>.m3a</c>) and renders its keyframes with the faithful MB3D
/// ray-marcher. Each keyframe becomes one <see cref="FractalImage"/>; frames are rendered lazily and
/// cached. The render resolution is configurable (full 2880x1620 is faithful but slow, so a smaller
/// working size is used for interactive preview).
/// </summary>
public sealed class Mb3dAnimationLoader : ImageLoader
{
    private readonly List<Mb3dKeyframeImage> _frames;

    public Mb3dAnimationLoader(string path, int renderWidth = 0, int renderHeight = 0, int superSample = 2)
    {
        var animation = Mb3dAnimation.Load(path);
        if (animation.Keyframes.Count == 0)
            throw new InvalidDataException("The .m3a file contains no keyframes.");

        // Default to the file's aspect ratio at a modest interactive size.
        if (renderWidth <= 0 || renderHeight <= 0)
        {
            double aspect = animation.Height > 0 ? (double)animation.Width / animation.Height : 16.0 / 9.0;
            renderHeight = 360;
            renderWidth = (int)Math.Round(renderHeight * aspect);
        }

        _frames = [];
        for (var i = 0; i < animation.Keyframes.Count; i++)
            _frames.Add(new Mb3dKeyframeImage(animation.Keyframes[i], renderWidth, renderHeight, i, superSample));
    }

    public override FractalImage Get(int index) => _frames[index];
    public override int Size() => _frames.Count;

    private sealed class Mb3dKeyframeImage : FractalImage
    {
        private readonly Mb3dKeyframe _keyframe;
        private readonly int _width, _height, _superSample;
        private Bitmap? _bitmap;

        public Mb3dKeyframeImage(Mb3dKeyframe keyframe, int width, int height, int index, int superSample)
        {
            _keyframe = keyframe;
            _width = width;
            _height = height;
            _superSample = superSample;
            scale = new FractalScale(index); // sequential index; MB3D animations are time-based, not zoom-based
        }

        public override Bitmap GetImage()
        {
            if (_bitmap != null) return _bitmap;
            // Supersample: render at superSample x the target size, then box-average down. MB3D itself
            // oversamples (the AniScale factor) and downscales, so this anti-aliasing is what matches the
            // smooth reference frames rather than a point-sampled (noisier) image.
            int ss = Math.Max(1, _superSample);
            int hiW = _width * ss, hiH = _height * ss;
            var scene = Mb3dSceneParameters.FromHeader(_keyframe.Header, _keyframe.Addon, hiW, hiH);
            var paint = Mb3dPaintParameters.FromHeader(_keyframe.Header);
            uint[] pixels = new Mb3dRenderer(scene, paint).Render();
            if (ss > 1) pixels = Downsample(pixels, hiW, hiH, ss);
            _bitmap = ToBitmap(pixels, _width, _height);
            return _bitmap;
        }

        // Box-average ss*ss source pixels into each destination pixel (downscale anti-aliasing).
        private static uint[] Downsample(uint[] src, int srcW, int srcH, int ss)
        {
            int dstW = srcW / ss, dstH = srcH / ss;
            var dst = new uint[dstW * dstH];
            int blockArea = ss * ss;
            for (var dy = 0; dy < dstH; dy++)
            {
                for (var dx = 0; dx < dstW; dx++)
                {
                    uint r = 0, g = 0, b = 0;
                    int sx0 = dx * ss, sy0 = dy * ss;
                    for (var by = 0; by < ss; by++)
                    {
                        int row = (sy0 + by) * srcW + sx0;
                        for (var bx = 0; bx < ss; bx++)
                        {
                            uint p = src[row + bx];
                            r += (p >> 16) & 0xFF;
                            g += (p >> 8) & 0xFF;
                            b += p & 0xFF;
                        }
                    }
                    dst[dy * dstW + dx] = 0xFF000000u
                        | ((r / (uint)blockArea) << 16) | ((g / (uint)blockArea) << 8) | (b / (uint)blockArea);
                }
            }
            return dst;
        }

        public override void Dispose()
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }

        private static Bitmap ToBitmap(uint[] argb, int w, int h)
        {
            var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            // uint 0xAARRGGBB matches Format32bppArgb's little-endian BGRA byte order.
            Marshal.Copy(MemoryMarshal.Cast<uint, int>(argb).ToArray(), 0, data.Scan0, argb.Length);
            bitmap.UnlockBits(data);
            return bitmap;
        }
    }
}
