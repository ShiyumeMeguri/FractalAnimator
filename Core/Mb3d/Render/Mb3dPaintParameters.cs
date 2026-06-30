namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>One resolved light for shading (global/directional or positional).</summary>
public struct Mb3dRenderLight
{
    public bool Active;
    public bool Positional;
    public Vec3d Direction;     // global: unit direction surface->light (camera frame)
    public Vec3d Position;      // positional: world position - mid
    public Vec3d Color;         // RGB on 0..255 scale, pre-multiplied by amplitude
    public int DiffuseFunction; // 0..3
    public int SpecularExponent;
    public double MaxDistanceSquared;
}

/// <summary>
/// Per-frame shading constants, mirroring the paint setup in HeaderTrafos.pas:1240-1450 (the
/// companion to <see cref="Mb3dSceneParameters"/> for the color/paint stage). Colors follow MB3D's
/// convention: light/ambient/depth colors on a 0..255 float scale, surface <c>LCols</c> on 0..1.
/// </summary>
public sealed class Mb3dPaintParameters
{
    private const double Pid180 = Math.PI / 180.0;

    public readonly Mb3dRenderLight[] Lights = new Mb3dRenderLight[6];
    public double DiffuseMultiplier;     // sDiff
    public double SpecularMultiplier;    // sSpec
    public double DepthFog;              // sDepth = TBpos[4] * 0.8e-6
    public Vec3d AmbientColor;           // AmbCol * ambMult (0..255)
    public Vec3d AmbientColor2;          // AmbCol2 * ambMult
    public Vec3d DepthColor;             // DepthCol (0..255), bottom of background gradient
    public Vec3d DepthColor2;            // DepthCol2 (0..255), top of background gradient

    public readonly int[] ColorPosition = new int[10];   // LCols stop positions (0..32767)
    public readonly Vec3d[] ColorDiffuse = new Vec3d[10]; // 0..1
    public readonly Vec3d[] ColorSpecular = new Vec3d[10];// 0..1
    public readonly double[] ColorSlope = new double[10]; // 1/(gap)

    public double ColorStart;            // sCStart
    public double ColorMul;              // sCmul
    public int ColorOnOrbitTrap;         // iColOnOT (bit0: 0=iteration, 1=orbit trap)
    public double SmoothIterationScale;  // mctsM = 32767/(maxIt+1)
    public bool ColorCycling;
    public bool NoColorInterp;

    public static Mb3dPaintParameters FromHeader(Mb3dMandHeader header)
    {
        var light = header.Light;
        var p = new Mb3dPaintParameters();
        ReadOnlySpan<int> tb = light.TrackbarPos;   // TBpos[3..11] -> index 0..8

        // Scalars (HeaderTrafos.pas:1292-1423).
        p.DiffuseMultiplier = Tb(tb, 5) * 0.02;
        p.DepthFog = Tb(tb, 4) * 0.8e-6;
        p.SpecularMultiplier = Math.Max(0.004, (Tb(tb, 7) & 0xFFF) * 0.02);
        double ambMult = (Tb(tb, 8) & 0xFFF) / 90.0;
        p.ColorOnOrbitTrap = (int)((light.TrackbarOptions >> 17) & 1); // diffmap off -> just this bit
        p.ColorCycling = (light.TrackbarOptions & 0x4000) != 0;
        p.SmoothIterationScale = 32767.0 / Math.Max(1, header.Iterations + 1);

        // sCStart / sCmul (CalcSCstartAndSCmul, HeaderTrafos.pas:1186-1195).
        double scStart = Sqr((Tb(tb, 9) + 30) / 90.0) * 32767 - 10900;
        double scMul = Sqr((Tb(tb, 10) + 30) / 90.0) * 32767 - 10900 - scStart;
        if (Math.Abs(scMul) > 0.001) scMul = 2.0 / scMul; else scMul = scMul < 0 ? -2000 : 2000;
        p.ColorStart = scStart;
        p.ColorMul = scMul;

        // Lights (HeaderTrafos.pas:1322-1377). Global lights only for now (the common case).
        for (var i = 0; i < 6; i++)
        {
            var l = light.Lights[i];
            int option = l.Option & 3;            // 0 on, 1/3 off, 2 lightmap
            ref var rl = ref p.Lights[i];
            rl.Active = option == 0;
            if (!rl.Active) continue;
            rl.DiffuseFunction = (l.Function >> 4) & 3;
            rl.SpecularExponent = 2 << (l.Function & 7);
            bool positional = (((l.Option >> 2) & 7) | ((l.Function & 128) >> 4) & 1) != 0
                              && ((l.Option >> 2) & 1) != 0;
            rl.Positional = positional;
            double amplitude = l.LampValue;
            var color = new Vec3d(l.Color.B0, l.Color.B1, l.Color.B2); // RGB on 0..255

            if (positional)
            {
                rl.Color = color * (amplitude * 1.3);
                rl.Position = new Vec3d(l.PositionX, l.PositionY, l.PositionZ);
                rl.MaxDistanceSquared = 800 * ((rl.Color.X + rl.Color.Y + rl.Color.Z) + 128 * amplitude);
            }
            else
            {
                // BuildViewVectorFOV(xa = LYpos, ya = -LXpos) = (-sin ya, sin xa, cos xa cos ya), then negate.
                double xa = l.PositionY, ya = -l.PositionX;
                double v0 = -Math.Sin(ya), v1 = Math.Sin(xa), v2 = Math.Cos(xa) * Math.Cos(ya);
                rl.Direction = new Vec3d(-v0, -v1, -v2).Normalized();
                rl.Color = color * amplitude;
            }
        }

        // Ambient / depth colors.
        p.AmbientColor = Rgb(light.AmbientColor) * ambMult;
        p.AmbientColor2 = Rgb(light.AmbientColor2) * ambMult;
        p.DepthColor = Rgb(light.DepthColor);
        p.DepthColor2 = Rgb(light.DepthColor2);

        // Surface color stops (LCols).
        for (var i = 0; i < 10; i++)
        {
            p.ColorDiffuse[i] = ColCardinal(light.SurfaceColors[i].ColorDiffuse);
            p.ColorSpecular[i] = ColCardinal(light.SurfaceColors[i].ColorSpecular);
            p.ColorPosition[i] = light.SurfaceColors[i].Position;
        }
        for (var i = 0; i < 10; i++)
        {
            int gap = i < 9 ? p.ColorPosition[i + 1] - p.ColorPosition[i] : 32767 - p.ColorPosition[9];
            p.ColorSlope[i] = gap > 1 ? 1.0 / gap : 1.0;
        }

        return p;
    }

    private static int Tb(ReadOnlySpan<int> tb, int index1Based) => tb[index1Based - 3]; // TBpos is 3..11
    private static double Sqr(double v) => v * v;
    private static Vec3d Rgb(Mb3dRgb c) => new(c.B0, c.B1, c.B2);
    private static Vec3d ColCardinal(uint c) => new((c & 0xFF) / 255.0, ((c >> 8) & 0xFF) / 255.0, ((c >> 16) & 0xFF) / 255.0);
}
