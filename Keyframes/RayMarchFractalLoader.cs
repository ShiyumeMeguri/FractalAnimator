using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FractalAnimator.Core;

namespace FractalAnimator.Keyframes;

/// <summary>
/// A procedural 3D ray-marched fractal (Mandelbulb + box) generator.
/// Each frame steps deeper into the fractal over time.
/// Implements the GLSL-style algorithm from the provided shader.
/// </summary>
public sealed class RayMarchFractalLoader : ImageLoader
{
    readonly List<RayMarchImage> _frames;

    public RayMarchFractalLoader(int width, int height, double magn, double fps, int maxSteps = 100, int glowPasses = 0)
    {
        _frames = [];
        var totalFrames = (int)(Math.Max(1, Math.Log(magn) / Math.Log(2)) * fps);
        for (var i = 0; i < totalFrames; i++)
        {
            var t = i / fps;
            var zoom = Math.Pow(2, t * 0.5);
            _frames.Add(new RayMarchImage(width, height, t, maxSteps, glowPasses, zoom));
        }
    }

    public override FractalImage Get(int index) => _frames[index];
    public override int Size() => _frames.Count;

    sealed class RayMarchImage : FractalImage
    {
        readonly int _w, _h, _maxSteps, _glowPasses;
        readonly double _time;
        Bitmap? _bitmap;

        public RayMarchImage(int w, int h, double t, int maxSteps, int glowPasses, double zoom)
        {
            _w = w; _h = h; _time = t; _maxSteps = maxSteps; _glowPasses = glowPasses;
            scale = new FractalScale(Math.Log(zoom) / Math.Log(2));
        }

        public override Bitmap GetImage()
        {
            if (_bitmap != null) return _bitmap;
            _bitmap = new Bitmap(_w, _h, PixelFormat.Format24bppRgb);
            Render();
            return _bitmap;
        }

        public override void Dispose() { _bitmap?.Dispose(); _bitmap = null; }

        void Render()
        {
            var data = _bitmap!.LockBits(new Rectangle(0, 0, _w, _h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            var stride = data.Stride;
            var scan0 = data.Scan0;

            Parallel.For(0, _h, y =>
            {
                var rowData = new byte[_w * 3];
                for (var x = 0; x < _w; x++)
                {
                    var u = (2.0 * x - _w) / _h;
                    var v = (2.0 * y - _h) / _h;

                    var ro = new Vec3(0, 0, -3);
                    var rd = Vec3.Normalize(new Vec3((float)u, (float)v, 1));

                    var ang = (float)(_time * 0.2);
                    var sa = (float)Math.Sin(ang);
                    var ca = (float)Math.Cos(ang);
                    ro = RotateXZ(ro, sa, ca);
                    rd = RotateXZ(rd, sa, ca);

                    float d = 0;
                    var col = new Vec3(0, 0, 0);
                    var glow = new Vec3(0, 0, 0);
                    for (var i = 0; i < _maxSteps; i++)
                    {
                        var p = ro + rd * d;
                        var (dist, tmpCol) = Map(p);
                        if (dist < 0.001f) { col += tmpCol; break; }
                        if (d > 20f) break;
                        glow += tmpCol / (1 + dist * dist);
                        d += dist * 0.7f;
                    }

                    col += glow * 0.03f;
                    col = new Vec3(
                        (float)Math.Tanh(col.X / 1e7f),
                        (float)Math.Tanh(col.Y / 1e7f),
                        (float)Math.Tanh(col.Z / 1e7f));

                    rowData[x * 3 + 0] = (byte)Math.Clamp(col.Z * 255, 0, 255);
                    rowData[x * 3 + 1] = (byte)Math.Clamp(col.Y * 255, 0, 255);
                    rowData[x * 3 + 2] = (byte)Math.Clamp(col.X * 255, 0, 255);
                }
                lock (this) { Marshal.Copy(rowData, 0, IntPtr.Add(scan0, y * stride), _w * 3); }
            });
            _bitmap.UnlockBits(data);
        }

        static (float dist, Vec3 col) Map(Vec3 p)
        {
            // Mandelbulb power-8 distance estimator
            var z = p;
            float dr = 1;
            float r;
            var n = 0;
            for (; n < 15; n++)
            {
                r = z.Length();
                if (r > 2) break;
                var theta = (float)Math.Acos(z.Z / r);
                var phi = (float)Math.Atan2(z.Y, z.X);
                dr = (float)Math.Pow(r, 7) * 7 * dr + 1;
                var zr = (float)Math.Pow(r, 7);
                theta *= 7;
                phi *= 7;
                z = new Vec3(
                    zr * (float)(Math.Sin(theta) * Math.Cos(phi)),
                    zr * (float)(Math.Sin(theta) * Math.Sin(phi)),
                    zr * (float)Math.Cos(theta));
                z += p;
            }
            r = z.Length();
            var col = new Vec3(
                0.5f + 0.5f * (float)Math.Sin(r * 2 + 0),
                0.5f + 0.5f * (float)Math.Sin(r * 2 + 2),
                0.5f + 0.5f * (float)Math.Sin(r * 2 + 4));
            return (0.5f * (float)Math.Log(r) * r / dr, col);
        }

        static Vec3 RotateXZ(Vec3 v, float sa, float ca) =>
            new(v.X * ca - v.Z * sa, v.Y, v.X * sa + v.Z * ca);
    }

    readonly record struct Vec3(float X, float Y, float Z)
    {
        public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator /(Vec3 a, float s) => new(a.X / s, a.Y / s, a.Z / s);
        public float Length() => MathF.Sqrt(X * X + Y * Y + Z * Z);
        public static Vec3 Normalize(Vec3 v) { var l = v.Length(); return l > 0 ? v / l : v; }
    }
}
