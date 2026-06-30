namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>
/// Drives the ray marcher over every pixel and shades the result with the MB3D paint model
/// (<see cref="Mb3dShader"/>). Rows run in parallel; each gets its own marcher.
/// </summary>
public sealed class Mb3dRenderer
{
    private readonly Mb3dSceneParameters _scene;
    private readonly Mb3dPaintParameters _paint;

    public Mb3dRenderer(Mb3dSceneParameters scene, Mb3dPaintParameters paint)
    {
        _scene = scene;
        _paint = paint;
    }

    /// <summary>Renders to a row-major ARGB (0xAARRGGBB) buffer of size Width*Height.</summary>
    public uint[] Render()
    {
        int w = _scene.Width, h = _scene.Height;
        var pixels = new uint[w * h];

        Parallel.For(0, h, iy =>
        {
            var marcher = new Mb3dRaymarcher(_scene);
            int row = iy * w;
            for (var ix = 0; ix < w; ix++)
            {
                var hit = marcher.March(ix, iy);
                pixels[row + ix] = Mb3dShader.Shade(hit, _paint, _scene, iy);
            }
        });

        return pixels;
    }
}
