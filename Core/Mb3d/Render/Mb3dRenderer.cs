namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>
/// Drives the ray marcher over every pixel and shades the result. This first stage uses simple
/// geometry shading (Lambert from a couple of fixed lights + ambient + iteration-based darkening) to
/// validate the camera, march, distance estimate, and formula stack against MB3D's reference
/// thumbnails. Full MB3D lighting/coloring (from TLightingParas9) is layered on afterwards.
/// </summary>
public sealed class Mb3dRenderer
{
    private readonly Mb3dSceneParameters _scene;

    public Mb3dRenderer(Mb3dSceneParameters scene) => _scene = scene;

    /// <summary>Renders to a row-major ARGB (0xAARRGGBB) buffer of size Width*Height.</summary>
    public uint[] Render()
    {
        int w = _scene.Width, h = _scene.Height;
        var pixels = new uint[w * h];

        // Two key lights + fill, in world space (placeholder until real lights are wired).
        Vec3d key = new Vec3d(0.4, 0.7, -0.6).Normalized();
        Vec3d fill = new Vec3d(-0.6, 0.2, -0.5).Normalized();

        Parallel.For(0, h, iy =>
        {
            var marcher = new Mb3dRaymarcher(_scene);
            int row = iy * w;
            for (var ix = 0; ix < w; ix++)
            {
                var hit = marcher.March(ix, iy);
                pixels[row + ix] = hit.IsHit ? Shade(hit, key, fill) : Background(iy);
            }
        });

        return pixels;
    }

    private uint Shade(Mb3dHit hit, Vec3d key, Vec3d fill)
    {
        double diffuse = Math.Max(0, Vec3d.Dot(hit.Normal, key)) * 0.75
                         + Math.Max(0, Vec3d.Dot(hit.Normal, fill)) * 0.35;
        double ambient = 0.18;
        // Slight darkening for deep (high-iteration) detail to read structure.
        double itShade = 1.0 - 0.35 * Math.Min(1.0, hit.IterationCount / (double)Math.Max(1, _scene.MaxIterations));
        double v = Math.Clamp((ambient + diffuse) * itShade, 0, 1);
        byte c = (byte)(v * 255);
        return Pack(c, c, c);
    }

    private static uint Background(int iy) => Pack(8, 10, 16);

    private static uint Pack(byte r, byte g, byte b) =>
        0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
}
