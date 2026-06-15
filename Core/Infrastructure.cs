using System.Diagnostics;
using System.Drawing.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FractalAnimator.Core;

public interface IExportable
{
    JsonObject ExportJson();
    void ImportJson(JsonObject obj);
}

public static class JsonExtensions
{
    public static JsonObject AsObject(this JsonNode? node) =>
        node as JsonObject ?? throw new InvalidOperationException("Expected JSON object.");
    public static JsonArray AsArray(this JsonNode? node) =>
        node as JsonArray ?? throw new InvalidOperationException("Expected JSON array.");
    public static string GetString(this JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    public static int GetInt(this JsonObject obj, string key) => obj[key]?.GetValue<int>() ?? 0;
    public static double GetDouble(this JsonObject obj, string key) => obj[key]?.GetValue<double>() ?? 0;
    public static bool GetBool(this JsonObject obj, string key) => obj[key]?.GetValue<bool>() ?? false;
}

public static class PropertiesFile
{
    public static Dictionary<string, string> Load(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] is '#' or '!') continue;
            var idx = line.IndexOfAny(['=', ':']);
            if (idx < 0) { dict[Decode(line)] = ""; continue; }
            dict[Decode(line[..idx].Trim())] = Decode(line[(idx + 1)..].Trim());
        }
        return dict;
    }

    private static string Decode(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c != '\\' || i == s.Length - 1) { sb.Append(c); continue; }
            var n = s[++i];
            sb.Append(n switch
            {
                't' => '\t', 'r' => '\r', 'n' => '\n', 'f' => '\f', '\\' => '\\',
                'u' when i + 4 < s.Length => (char)Convert.ToInt32(s.Substring(i + 1, 4), 16),
                _ => n
            });
            if (n == 'u') i += 4;
        }
        return sb.ToString();
    }
}

public static class AssetPaths
{
    public static string Base => Path.Combine(AppContext.BaseDirectory, "Assets");
    public static string Get(params string[] parts) =>
        Path.Combine([Base, .. parts]);
}

public static class Localization
{
    private static Dictionary<string, string> _locale = new(StringComparer.Ordinal);
    private static Dictionary<string, string> _default = new(StringComparer.Ordinal);

    public static void Initialize()
    {
        var langDir = AssetPaths.Get("lang");
        if (!Directory.Exists(langDir)) return; // embedded defaults are enough on their own
        var ci = CultureInfo.CurrentCulture;
        var name = $"{ci.TwoLetterISOLanguageName}_{ci.Name.Split('-').Last()}";
        var enPath = AssetPaths.Get("lang", "en_US.lang");
        if (File.Exists(enPath)) _default = PropertiesFile.Load(enPath);
        var localePath = AssetPaths.Get("lang", name + ".lang");
        _locale = name.Equals("en_US", StringComparison.OrdinalIgnoreCase)
            ? _default
            : File.Exists(localePath)
                ? PropertiesFile.Load(localePath)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        Debug.WriteLine(name);
    }

    // Optional .lang files override these, but the app is fully readable with no asset files at all.
    public static string Get(string key) =>
        _locale.TryGetValue(key, out var v) ? v :
        _default.TryGetValue(key, out v) ? v :
        Defaults.TryGetValue(key, out v) ? v : key;

    private static readonly Dictionary<string, string> Defaults = new(StringComparer.Ordinal)
    {
        ["title"] = "FractalAnimator — infinite-zoom fractal studio",

        ["panel.image"] = "Fractal source",
        ["panel.interpolator"] = "Zoom timing",
        ["panel.indicator"] = "On-screen scale labels",
        ["gen_options.title"] = "Video output",

        ["gen_options.width"] = "Width (px)",
        ["gen_options.height"] = "Height (px)",
        ["gen_options.fps"] = "Frame rate",
        ["gen_options.image_blending"] = "Motion blur frames",
        ["gen_options.start_time"] = "Hold start (s)",
        ["gen_options.end_time"] = "Hold end (s)",
        ["gen_options.ffmpeg"] = "ffmpeg command",
        ["gen_options.encoder"] = "Encoder",

        ["label.empty"] = "— no source loaded —",
        ["label.browse"] = "Preview frames…",
        ["label.generate"] = "Render video",
        ["label.abort"] = "Stop",
        ["label.set"] = "Apply",
        ["label.compile"] = "Compile",
        ["label.preset"] = "Preset",
        ["label.select"] = "Choose folder / file…",
        ["label.font"] = "Choose font…",
        ["label.internal"] = "Use bundled font",
        ["label.close"] = "Close",

        ["message.missing_image"] = "Load or compile a fractal source first.",
        ["message.missing_indicator"] = "No on-screen scale label is selected. Render anyway?",
        ["message.warning"] = "Warning",
        ["message.completed"] = "Render complete.",
        ["message.info"] = "FractalAnimator",
        ["message.error"] = "Error",
        ["message.invalidnum"] = "One of the values could not be parsed as a number.",

        ["file.flv"] = "Video file",
        ["file.fap"] = "FractalAnimator project",

        ["image.amount"] = "Loaded {0} keyframe(s)",
        ["image.dir"] = "Folder:",

        ["image.mandelbrot.description"] =
            "Deep-zoom Mandelbrot via SIMD perturbation. Paste a high-precision center and a magnification.",
        ["image.mandelbrot.real"] = "Center — real part",
        ["image.mandelbrot.imag"] = "Center — imaginary part",
        ["image.mandelbrot.magn"] = "Magnification",
        ["image.mandelbrot.iter"] = "Max iterations",

        ["image.fractalscript.description"] =
            "Write a fractal formula in C# below and press Compile. It is JIT-compiled with SIMD and run on every core.",

        ["image.kf.description"] = "A folder of Kalles Fraktaler keyframe images named NNNNN_<magnification>.png.",
        ["image.kf.tooltip"] = "Pick the folder that holds the keyframe images.",
        ["image.kfb.description"] = "A folder of Kalles Fraktaler .kfb iteration files plus a .kfr palette.",
        ["image.fz.description"] = "A folder of FractalZoomer images, each with a matching .info.json.",
        ["image.fz.tooltip"] = "Pick the FractalZoomer export folder.",
        ["image.maple.description"] = "A MapleMandelMaker configuration file describing a keyframe sequence.",
        ["image.maple.config"] = "MapleMandelMaker config file",

        ["menu.files"] = "Project",
        ["menu.files.load"] = "Open project…",
        ["menu.files.save"] = "Save project…",

        ["op.insert"] = "Add",
        ["op.delete"] = "Remove",
        ["op.update"] = "Update",

        ["indicator.key.time"] = "Time (s)",
        ["indicator.key.image"] = "Keyframe",
        ["indicator.key.accT"] = "Accelerate (s)",
        ["indicator.key.decT"] = "Decelerate (s)",
        ["indicator.key.maxTrans"] = "Max transition (s)",

        ["progress_bar.format"] = "FractalAnimator — {0:F1}%  ({1}/{2} frames, {3}/{4} keyframes, ETA {5})",

        ["image_browser.title"] = "Keyframe preview",
        ["image_browser.image"] = "Keyframe",
        ["image_browser.format"] = "Zoom level {0:F2}   (magnification {1})",

        ["font_chooser.title"] = "Choose a font",

        ["class.hywt.fractal.animator.ui.FractalScriptLoaderConfigure"] = "Fractal formula (live compile)",
        ["class.hywt.fractal.animator.ui.ShaderFractalLoaderConfigure"] = "FractalParadise (shader art)",
        ["class.hywt.fractal.animator.ui.SimpleMandelbrotLoaderConfigure"] = "Mandelbrot (quick)",
        ["class.hywt.fractal.animator.ui.RayMarchFractalLoaderConfigure"] = "Mandelbulb 3D (ray-march)",
        ["class.hywt.fractal.animator.ui.FzImageLoaderConfigure"] = "FractalZoomer images",
        ["class.hywt.fractal.animator.ui.KfImageLoaderConfigure"] = "Kalles Fraktaler PNG",
        ["class.hywt.fractal.animator.ui.KfbLoaderConfigure"] = "Kalles Fraktaler .kfb",
        ["class.hywt.fractal.animator.ui.MapleMandelMakerLoaderConfigure"] = "MapleMandelMaker",
        ["class.hywt.fractal.animator.ui.LinearInterpolatorConfigure"] = "Linear",
        ["class.hywt.fractal.animator.ui.SlopeAccelInterpolatorConfigure"] = "Smooth (eased slopes)",
        ["class.hywt.fractal.animator.ui.AccelInterpolatorConfigure"] = "Accelerate / decelerate",

        ["indicator.class.hywt.fractal.animator.indicator.MagnificationPowerIndicator"] = "Magnification 10ⁿ (top-left)",
        ["indicator.class.hywt.fractal.animator.indicator.KfScaleIndicator"] = "Magnification (KF style)",
        ["indicator.class.hywt.fractal.animator.indicator.FxScaleIndicator"] = "Zoom count (FX style)",
        ["indicator.class.hywt.fractal.animator.indicator.OdometerIndicator"] = "Odometer",
        ["indicator.class.hywt.fractal.animator.indicator.ExponentialScaleIndicator"] = "Exponent (10ⁿ)",
        ["indicator.class.hywt.fractal.animator.indicator.GoogologyIndicator"] = "Googology name",
    };
}

public static class Utils
{
    public static string RemoveExtension(string path) => Path.GetFileNameWithoutExtension(path);
    public static string FormatSeconds(double s) => double.IsInfinity(s) ? "\u221e" :
        string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", (int)(s / 3600), (int)(s % 3600 / 60), (int)(s % 60));
}

public enum EncodingParam { X264, X265, NVENC, QSV, AMF }

public static class EncodingParamExtensions
{
    public static string[] GetParam(this EncodingParam p) => p switch
    {
        EncodingParam.X264 => ["-c:v", "libx264", "-crf", "21", "-preset", "slow", "-x264-params", "bframes=10", "-pix_fmt", "yuv420p10le"],
        EncodingParam.X265 => ["-c:v", "libx265", "-crf", "21", "-preset", "slow", "-x265-params", "no-sao=1:no-rect=1:no-amp=1", "-pix_fmt", "yuv420p10le"],
        EncodingParam.NVENC => ["-c:v", "h264_nvenc", "-qp", "21", "-preset", "p7", "-pix_fmt", "nv12"],
        EncodingParam.QSV => ["-c:v", "h264_qsv", "-qp", "21", "-pix_fmt", "nv12"],
        EncodingParam.AMF => ["-c:v", "h264_amf", "-qp", "21", "-pix_fmt", "nv12"],
        _ => []
    };
}

public sealed class FontRepository : IDisposable
{
    private readonly PrivateFontCollection _col = new();
    private readonly Dictionary<string, FontFamily> _families = new(StringComparer.OrdinalIgnoreCase);
    public static FontRepository Instance { get; } = new();
    private FontRepository() { LoadFont("Roboto-Regular.ttf"); LoadFont("LargeFontMono.ttf"); }

    public Font Create(string file, float size)
    {
        if (_families.TryGetValue(file, out var f)) return new Font(f, size, FontStyle.Regular, GraphicsUnit.Pixel);
        return new Font(SystemFonts.DefaultFont.FontFamily, size, FontStyle.Regular, GraphicsUnit.Pixel);
    }
    public Font CreateRoboto(float size) => Create("Roboto-Regular.ttf", size);
    private void LoadFont(string fn) { var p = AssetPaths.Get("fonts", fn); if (!File.Exists(p)) return; _col.AddFontFile(p); _families[fn] = _col.Families.Last(); }
    public void Dispose() => _col.Dispose();
}

public static class Regexes
{
    public static readonly Regex KfPng = new(@"\d\d\d\d\d_([0-9Ee.\-+]+)\.(png|jpg)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public static readonly Regex Kfb = new(@"\d\d\d\d\d_([0-9Ee.\-+]+)\.(kfb)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public static readonly Regex FzImage = new(@".+ \((\d+)\)\.(png|jpg|bmp)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
}
