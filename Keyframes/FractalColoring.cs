using System.Drawing;
using System.Drawing.Imaging;
using FractalAnimator.Core;

namespace FractalAnimator.Keyframes;

/// <summary>
/// Turns a smooth-iteration buffer (the engine output, -1 for interior) into a colored 24bpp bitmap.
/// Shared by the keyframe images and the live preview so they look identical.
/// </summary>
public static class FractalColoring
{
    public static Bitmap Colorize(float[] buffer, int width, int height, double density)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var palette = new Palette();
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                var row = (byte*)data.Scan0 + y * data.Stride;
                var rowBase = y * width;
                for (var x = 0; x < width; x++)
                {
                    var rgb = palette.GetColorContinuous(buffer[rowBase + x], density);
                    var pixel = row + x * 3;
                    pixel[0] = (byte)(rgb & 0xFF);
                    pixel[1] = (byte)((rgb >> 8) & 0xFF);
                    pixel[2] = (byte)((rgb >> 16) & 0xFF);
                }
            }
        }
        bitmap.UnlockBits(data);
        return bitmap;
    }
}
