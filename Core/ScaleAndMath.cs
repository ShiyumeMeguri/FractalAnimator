using System.Globalization;

namespace FractalAnimator.Core;

public sealed class FractalScale
{
    private readonly double _zooms;
    public FractalScale(double zooms) => _zooms = zooms;
    public double GetZooms() => _zooms;
    public double GetLog10Zooms() => Math.Log10(2) * _zooms;
    public double GetLog10Scale() => Math.Log10(4) - GetLog10Zooms();
    public string GetMagnification(int p)
    {
        var l10 = GetLog10Zooms();
        if (l10 <= 7) return Math.Pow(10, l10).ToString($"F{p}", CultureInfo.InvariantCulture);
        return string.Format(CultureInfo.InvariantCulture, $"{{0:F{p}}}E{{1:+0;-0;+0}}", Math.Pow(10, l10 - (int)l10), (int)l10);
    }
    public string GetMagnification() => GetMagnification(2);
    public static FractalScale FromMagnification(string s)
    {
        var parts = s.ToLowerInvariant().Split('e');
        var b = double.Parse(parts[0], CultureInfo.InvariantCulture);
        if (b == 0) return new(-99999999);
        var e = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        return new(Math.Log(b) / Math.Log(2) + Math.Log(10) / Math.Log(2) * e);
    }
    public static FractalScale FromSize(string s)
    {
        var parts = s.ToLowerInvariant().Split('e');
        var b = double.Parse(parts[0], CultureInfo.InvariantCulture);
        if (b == 0) return new(-99999999);
        var e = -(parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0);
        return new(-(Math.Log(b) / Math.Log(2) - 2) + Math.Log(10) / Math.Log(2) * e);
    }
    public override string ToString() => GetMagnification();
}

public sealed class Palette
{
    private static readonly int[][] Def =
    [
        [1, 1, 1], [205, 92, 92], [240, 128, 128], [255, 0, 0], [178, 34, 34], [139, 0, 0],
        [188, 143, 143], [165, 42, 42], [128, 0, 0], [250, 128, 114], [255, 99, 71], [233, 150, 122], [255, 127, 80],
        [255, 69, 0], [255, 160, 122], [160, 82, 45], [210, 105, 30], [139, 69, 19], [244, 164, 96], [255, 218, 185],
        [205, 133, 63], [255, 228, 196], [255, 140, 0], [222, 184, 135], [210, 180, 140], [255, 222, 173],
        [255, 228, 181], [255, 165, 0], [245, 222, 179], [184, 134, 11], [218, 165, 32], [255, 215, 0],
        [240, 230, 140], [238, 232, 170], [189, 183, 107], [255, 255, 0], [128, 128, 0], [107, 142, 35],
        [154, 205, 50], [85, 107, 47], [173, 255, 47], [127, 255, 0], [124, 252, 0], [0, 255, 0], [50, 205, 50],
        [152, 251, 152], [144, 238, 144], [34, 139, 34], [0, 128, 0], [0, 100, 0], [143, 188, 143], [46, 139, 87],
        [60, 179, 113], [0, 255, 127], [0, 250, 154], [102, 205, 170], [127, 255, 212], [64, 224, 208],
        [32, 178, 170], [72, 209, 204], [0, 139, 139], [0, 128, 128], [0, 255, 255], [175, 238, 238],
        [0, 206, 209], [95, 158, 160], [176, 224, 230], [173, 216, 230], [0, 191, 255], [135, 206, 235],
        [135, 206, 250], [70, 130, 180], [30, 144, 255], [176, 196, 222], [100, 149, 237], [65, 105, 225],
        [0, 0, 255], [0, 0, 205], [0, 0, 139], [0, 0, 128], [25, 25, 112], [106, 90, 205], [72, 61, 139],
        [123, 104, 238], [147, 112, 219], [138, 43, 226], [75, 0, 130], [153, 50, 204], [148, 0, 211],
        [186, 85, 211], [216, 191, 216], [221, 160, 221], [238, 130, 238], [255, 0, 255], [139, 0, 139],
        [128, 0, 128], [218, 112, 214], [199, 21, 133], [255, 20, 147], [255, 105, 180], [219, 112, 147],
        [220, 20, 60], [255, 192, 203], [255, 182, 193], [220, 220, 220], [211, 211, 211], [192, 192, 192],
        [169, 169, 169], [128, 128, 128], [105, 105, 105], [119, 136, 153], [112, 128, 144], [47, 79, 79]
    ];

    private readonly int[][] _palette;
    public Palette() : this(Def) { }
    public Palette(params int[][] p) => _palette = p;
    private float[] Ctrans(float it)
    {
        var p = it - (float)Math.Floor(it);
        var c1 = (int)Math.Floor(it) % _palette.Length;
        var c2 = (c1 + 1) % _palette.Length;
        return [(1 - p) * _palette[c1][0] + p * _palette[c2][0], (1 - p) * _palette[c1][1] + p * _palette[c2][1], (1 - p) * _palette[c1][2] + p * _palette[c2][2]];
    }
    public int GetColor(int it)
    {
        if (it == -1) return 0;
        var c = Ctrans(it / 6.0f);
        return ((int)MathF.Round(c[0]) << 16) | ((int)MathF.Round(c[1]) << 8) | (int)MathF.Round(c[2]);
    }

    /// <summary>
    /// Smooth coloring for a fractional escape value. Interior points (negative value) are black.
    /// <paramref name="density"/> controls how many iterations span one full palette cycle.
    /// </summary>
    public int GetColorContinuous(double value, double density)
    {
        if (value < 0) return 0;
        var c = Ctrans((float)(value * density));
        return ((int)MathF.Round(c[0]) << 16) | ((int)MathF.Round(c[1]) << 8) | (int)MathF.Round(c[2]);
    }
}

public static class Googology
{
    static readonly string[] B = ["n", "m", "b", "tr", "quadr", "quint", "sext", "sept", "oct", "non"];
    static readonly string[] U = ["", "un", "duo", "tre", "quattuor", "quin", "se", "septe", "octo", "nove"];
    static readonly string[] T = ["", "deci", "viginti", "triginta", "quadraginta", "quinquaginta", "sexaginta", "septuaginta", "octoginta", "nonaginta"];
    static readonly string[] TM = ["", "n", "ms", "ns", "ns", "ns", "n", "n", "mx", ""];
    static readonly string[] H = ["", "centi", "ducenti", "trecenti", "quadringenti", "quingenti", "sescenti", "septingenti", "octingenti", "nongenti"];
    static readonly string[] HM = ["", "nx", "n", "ns", "ns", "ns", "n", "n", "mx", ""];
    const string Vowels = "aeiou";
    static readonly string[] Suffixes = ["", "thousand"];

    static string RmLastVowel(string s) => s.Length > 0 && Vowels.Contains(s[^1]) ? s[..^1] : s;
    public static string Suffix(int n)
    {
        if (n < 0) return "";
        if (n < Suffixes.Length) return Suffixes[n];
        return GoogSuffix(n - 1) + "on";
    }
    static string GoogSuffix(int n)
    {
        string r;
        if (n % 1000 < 10) r = B[n % 1000];
        else
        {
            var uo = n % 10; var u = U[uo]; var ten = T[(n / 10) % 10]; var hun = H[(n / 100) % 10];
            var m = string.IsNullOrEmpty(ten) ? HM[(n / 100) % 10] : TM[(n / 10) % 10];
            if (uo == 3) { if (m.Contains('s')) u += "s"; }
            else if (uo == 6) { if (m.Contains('s')) u += "s"; if (m.Contains('x')) u += "x"; }
            else if (uo is 7 or 9) { if (m.Contains('n')) u += "n"; if (m.Contains('m')) u += "m"; }
            r = u + ten + hun;
        }
        if (n >= 1000) r = GoogSuffix(n / 1000) + r;
        return RmLastVowel(r) + "illi";
    }
    public static string ToEnglishNotation(double mant, int exp)
    {
        var sd = mant * Math.Pow(10, exp % 3);
        return sd.ToString("F3", CultureInfo.InvariantCulture) + " " + Suffix(exp / 3);
    }
    public static string FormatLog2(double l2) { var r = FromLog2(l2); return ToEnglishNotation(r.Mant, r.Exp); }
    public static string FormatLog10(double l10) { var r = FromLog10(l10); return ToEnglishNotation(r.Mant, r.Exp); }
    public static LogResult FromLog2(double v) { var l10 = v * Math.Log10(2); return new(Math.Pow(10, l10 - Math.Floor(l10)), (int)Math.Floor(l10)); }
    public static LogResult FromLog10(double v) => new(Math.Pow(10, v - Math.Floor(v)), (int)Math.Floor(v));
    public record struct LogResult(double Mant, int Exp);
}
