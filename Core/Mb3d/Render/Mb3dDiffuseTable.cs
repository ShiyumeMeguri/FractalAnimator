namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>
/// The diffuse response tables, a faithful port of <c>SetCosTabFunction</c> (LightAdjust.pas:622) +
/// <c>GetCosTabVal</c> (LightAdjust.pas:784). Four base functions (0 clamped-Lambert-ish, 1 squared,
/// 2 half-Lambert, 3 half-Lambert squared) plus their roughened counterparts (a 61-tap box blur of
/// sqrt(base)); a cubic spline samples them and blends base->roughened by the per-pixel roughness.
/// </summary>
public static class Mb3dDiffuseTable
{
    // [0..3] = base functions, [4..7] = roughened versions; 128 entries each.
    private static readonly float[][] Table = Build();

    private static float[][] Build()
    {
        var table = new float[8][];
        for (var k = 0; k < 8; k++) table[k] = new float[128];

        for (var i = 0; i < 128; i++)
        {
            double d = 1 - (i - 2) / 60.0;
            table[0][i] = (float)(d > 0.15 ? (d - 0.08) * 1.0869565 : d <= 0 ? 0 : Math.Pow(d, Math.Max(1, (0.505 - d) * 3.8)));
            double c = Math.Max(0, d);
            table[1][i] = (float)(c * c);
            table[2][i] = (float)(d * 0.5 + 0.5);
            table[3][i] = (float)((d * 0.5 + 0.5) * (d * 0.5 + 0.5));
        }

        // Roughened tables: 61-tap box blur of sqrt(base), mapped through Sqr(e*0.011 + Sqr(e*0.007)).
        var tmp = new double[128];
        for (var k = 0; k < 4; k++)
        {
            for (var j = 0; j < 128; j++) tmp[j] = Math.Sqrt(Math.Max(0, table[k][j]));
            for (var j = 0; j < 128; j++)
            {
                double e = 0;
                for (var i = 0; i <= 60; i++)
                {
                    int l = Math.Abs(j + i - 30);
                    if (l < 128) e += tmp[l];
                }
                double v = e * 0.011 + (e * 0.007) * (e * 0.007);
                table[k + 4][j] = (float)(v * v);
            }
        }
        return table;
    }

    /// <summary>
    /// Diffuse response for function <paramref name="function"/> (0..3) at cosine <paramref name="dot"/>,
    /// with roughness <paramref name="rough"/> (0 sharp .. 1 fully roughened).
    /// </summary>
    public static double Evaluate(int function, double dot, double rough)
    {
        double t = 62 - 60 * dot;
        int ip = (int)t - 1; // Trunc toward zero; t > 0 here
        double frac;
        if (ip < 0) { ip = 0; frac = 0; }
        else if (ip > 124) { ip = 124; frac = 1; }
        else frac = t - (int)t;

        // MakeSplineCoeff (Math3D.pas:700): cardinal cubic weights.
        double w3 = (1.0 / 6.0) * frac * frac * frac;
        double w0 = 1.0 / 6.0 + 0.5 * frac * (frac - 1.0) - w3;
        double w2 = frac + w0 - 2.0 * w3;
        double w1 = 1.0 - w0 - w2 - w3;

        float[] baseTab = Table[function];
        float[] roughTab = Table[function + 4];
        double baseValue = baseTab[ip] * w0 + baseTab[ip + 1] * w1 + baseTab[ip + 2] * w2 + baseTab[ip + 3] * w3;
        double roughValue = roughTab[ip] * w0 + roughTab[ip + 1] * w1 + roughTab[ip + 2] * w2 + roughTab[ip + 3] * w3;
        return baseValue + rough * (roughValue - baseValue);
    }
}
