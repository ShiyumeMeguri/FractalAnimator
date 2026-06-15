using System.Globalization;
using FractalAnimator.Core;
using FractalAnimator.Interp;
using FractalAnimator.Keyframes;
using FractalAnimator.Rendering;
using FractalAnimator.UI;

namespace FractalAnimator;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.Run();
        if (args.Length > 0 && args[0].Equals("--rendertest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.RenderTest();
        if (args.Length > 0 && args[0].Equals("--previewtest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.PreviewTest();
        if (args.Length > 0 && args[0].Equals("--uishot", StringComparison.OrdinalIgnoreCase))
            return SelfTest.UiShot();
        if (args.Length > 0 && args[0].Equals("--shadertest", StringComparison.OrdinalIgnoreCase))
            return SelfTest.ShaderTest(args);
        if (args.Length > 0 && args[0].Equals("--corruptionvideo", StringComparison.OrdinalIgnoreCase))
            return SelfTest.CorruptionVideo(args);
        if (args.Length > 0 && args[0].Equals("--shadergallery", StringComparison.OrdinalIgnoreCase))
            return SelfTest.ShaderGallery();
        if (args.Length > 0 && args[0].Equals("--deepprobe", StringComparison.OrdinalIgnoreCase))
            return SelfTest.DeepProbe();
        if (args.Length > 0 && args[0].Equals("--deepjulia", StringComparison.OrdinalIgnoreCase))
            return SelfTest.DeepJulia();

        ApplicationConfiguration.Initialize();
        Localization.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}

/// <summary>
/// Headless verification of the runtime-compiled formulas and the SIMD deep-zoom engine. Run with
/// <c>dotnet run -- --selftest</c>. It compiles every preset, renders sample tiles across shallow and
/// extreme zoom depths, and asserts the SIMD and scalar pixel loops agree.
/// </summary>
internal static class SelfTest
{
    public static int Run()
    {
        var failures = 0;
        Console.WriteLine($"SIMD accelerated: {PerturbationEngine.SimdAccelerated}, lanes: {PerturbationEngine.VectorLanes}");

        for (var i = 0; i < FractalCompiler.BuiltInNames.Count; i++)
        {
            var name = FractalCompiler.BuiltInNames[i];
            IFractal fractal;
            try
            {
                fractal = FractalCompiler.CreateBuiltIn(i);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] compile '{name}': {ex.Message}");
                failures++;
                continue;
            }
            var isSimd = fractal is ISimdFractal;
            Console.WriteLine($"[ok]   compiled '{name}' -> {fractal.Name} (SIMD: {isSimd}, dynamical: {fractal.DynamicalPlane})");

            failures += CheckSimdMatchesScalar(fractal, name);
        }

        // Deep-zoom stress at a known interesting Mandelbrot location (~39 significant digits, so it
        // resolves structure to roughly zoom 2^130; beyond that the coordinate itself is the limit, not
        // the engine). The invariant we assert at every depth is numerical stability: no NaN, no crash.
        var mandel = FractalCompiler.CreateBuiltIn(0);
        var deepReal = BigDecimalCompat.Parse("-1.99999911758766165543764649311537154663");
        var deepImag = BigDecimalCompat.Parse("-4.2402439547240753390707694210131039e-13");
        foreach (var zooms in new[] { 0.0, 30.0, 102.0, 300.0, 800.0 })
        {
            var buffer = new float[96 * 96];
            try
            {
                PerturbationEngine.Render(mandel, deepReal, deepImag, zooms, 96, 96, 1500, buffer);
                var (interior, escaped, nan) = Classify(buffer);
                var ok = nan == 0;
                Console.WriteLine($"{(ok ? "[ok]  " : "[FAIL]")} deep zooms={zooms,6}: interior={interior} escaped={escaped} nan={nan}");
                if (!ok) failures++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] deep zooms={zooms}: {ex.Message}");
                failures++;
            }
        }

        // The location resolves real structure (a mix of interior and exterior) at 1e30-class depth.
        {
            var buffer = new float[96 * 96];
            PerturbationEngine.Render(mandel, deepReal, deepImag, 102.0, 96, 96, 2000, buffer);
            var (interior, escaped, _) = Classify(buffer);
            var ok = interior > 0 && escaped > 0;
            Console.WriteLine($"{(ok ? "[ok]  " : "[FAIL]")} structure at zooms=102: interior={interior} escaped={escaped}");
            if (!ok) failures++;
        }

        // SIMD and scalar must still agree deep, not just at the surface.
        {
            var a = new float[96 * 96];
            var b = new float[96 * 96];
            PerturbationEngine.ForceScalar = false;
            PerturbationEngine.Render(mandel, deepReal, deepImag, 102.0, 96, 96, 2000, a);
            PerturbationEngine.ForceScalar = true;
            PerturbationEngine.Render(mandel, deepReal, deepImag, 102.0, 96, 96, 2000, b);
            PerturbationEngine.ForceScalar = false;
            var mismatches = 0;
            for (var p = 0; p < a.Length; p++)
                if ((a[p] < 0) != (b[p] < 0) || Math.Abs(a[p] - b[p]) > 1e-3) mismatches++;
            var ok = mismatches < a.Length * 0.005;
            Console.WriteLine($"{(ok ? "[ok]  " : "[FAIL]")} SIMD vs scalar deep (zooms=102): mismatches={mismatches}/{a.Length}");
            if (!ok) failures++;
        }

        failures += CheckUiConstructs();
        failures += CheckImagePipeline();

        Console.WriteLine(failures == 0 ? "ALL SELF-TESTS PASSED" : $"{failures} SELF-TEST FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }

    // Render real keyframes through the full image + coloring path and save PNGs for visual inspection.
    static int CheckImagePipeline()
    {
        var failures = 0;
        var outDir = Path.Combine(AppContext.BaseDirectory, "selftest_out");
        Directory.CreateDirectory(outDir);
        var deepReal = BigDecimalCompat.Parse("-1.99999911758766165543764649311537154663");
        var deepImag = BigDecimalCompat.Parse("-4.2402439547240753390707694210131039e-13");
        var cases = new (string name, int preset, BigDecimalCompat re, BigDecimalCompat im, double zooms, int iter)[]
        {
            ("mandelbrot_full", 0, BigDecimalCompat.FromDouble(-0.5), BigDecimalCompat.Zero, 0, 500),
            ("mandelbrot_deep_1e30", 0, deepReal, deepImag, 102, 2500),
            ("burningship", 1, BigDecimalCompat.FromDouble(-0.5), BigDecimalCompat.FromDouble(-0.5), 1, 600),
            ("julia", 4, BigDecimalCompat.Zero, BigDecimalCompat.Zero, 0, 600),
        };
        foreach (var c in cases)
        {
            try
            {
                var fractal = FractalCompiler.CreateBuiltIn(c.preset);
                var image = new PerturbationFractalImage(fractal, c.re, c.im, c.zooms, 512, 512, c.iter,
                    FractalScriptLoader.DefaultColorDensity);
                using var bitmap = image.GetImage();
                var distinct = CountDistinctColors(bitmap);
                var path = Path.Combine(outDir, c.name + ".png");
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                var ok = distinct > 50;
                Console.WriteLine($"{(ok ? "[ok]  " : "[FAIL]")} image '{c.name}': {distinct} distinct colors -> {path}");
                if (!ok) failures++;
                image.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] image '{c.name}': {ex.Message}");
                failures++;
            }
        }
        return failures;
    }

    static int CountDistinctColors(Bitmap bitmap)
    {
        var seen = new HashSet<int>();
        for (var y = 0; y < bitmap.Height; y += 4)
            for (var x = 0; x < bitmap.Width; x += 4)
                seen.Add(bitmap.GetPixel(x, y).ToArgb());
        return seen.Count;
    }

    // Building the main window exercises the theme, the code editor and every panel's Init().
    static int CheckUiConstructs()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Localization.Initialize();
            using var form = new MainForm();
            using var loader = new FractalScriptLoaderConfigure();
            loader.SetOnLoadCallable(() => Task.CompletedTask);
            loader.Init();
            using var shaderLoader = new ShaderFractalLoaderConfigure();
            shaderLoader.SetOnLoadCallable(() => Task.CompletedTask);
            shaderLoader.Init();
            Console.WriteLine("[ok]   UI constructs (MainForm + formula studio + FractalParadise studio)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] UI construction: {ex.Message}");
            return 1;
        }
    }

    static int CheckSimdMatchesScalar(IFractal fractal, string name)
    {
        if (fractal is not ISimdFractal || !PerturbationEngine.SimdAccelerated) return 0;

        // Mandelbrot-family default view; Julia uses the origin-centered dynamical plane.
        var center = fractal.DynamicalPlane
            ? (BigDecimalCompat.Zero, BigDecimalCompat.Zero)
            : (BigDecimalCompat.FromDouble(-0.5), BigDecimalCompat.Zero);
        const int size = 128;
        const double zooms = 2.0;

        var simd = new float[size * size];
        var scalar = new float[size * size];
        PerturbationEngine.ForceScalar = false;
        PerturbationEngine.Render(fractal, center.Item1, center.Item2, zooms, size, size, 600, simd);
        PerturbationEngine.ForceScalar = true;
        PerturbationEngine.Render(fractal, center.Item1, center.Item2, zooms, size, size, 600, scalar);
        PerturbationEngine.ForceScalar = false;

        var mismatches = 0;
        double worst = 0;
        for (var p = 0; p < simd.Length; p++)
        {
            var a = simd[p];
            var b = scalar[p];
            var bothInterior = a < 0 && b < 0;
            if (bothInterior) continue;
            var diff = Math.Abs(a - b);
            if (diff > worst) worst = diff;
            if (diff > 1e-3 || float.IsNaN(a) != float.IsNaN(b)) mismatches++;
        }
        var ratio = mismatches / (double)simd.Length;
        var ok = ratio < 0.005;
        Console.WriteLine($"{(ok ? "[ok]  " : "[FAIL]")} SIMD vs scalar '{name}': mismatches={mismatches}/{simd.Length} worst={worst:0.000}");
        return ok ? 0 : 1;
    }

    // End-to-end render through the real VideoRenderer path that previously raced on shared GDI
    // bitmaps and the stateful odometer indicator. Requires ffmpeg on PATH.
    public static int RenderTest()
    {
        try
        {
            var loader = new SimpleMandelbrotLoader(BigDecimalCompat.FromDouble(-0.743643887037151),
                BigDecimalCompat.FromDouble(0.13182590420533), 8, 400, 400, 400);
            var renderer = new VideoRenderer();
            renderer.SetInterpolator(new LinearInterpolator([0, 2], [0, 6]));
            renderer.SetMergeFrames(3);
            renderer.AddScaleIndicator(new KfScaleIndicator());
            renderer.AddScaleIndicator(new OdometerIndicator());
            var output = Path.Combine(Path.GetTempPath(), "fractalanimator_rendertest.mp4");
            if (File.Exists(output)) File.Delete(output);
            var p = new RenderParams(320, 240, 15, 3, 0, 0, "ffmpeg", EncodingParam.X264);
            renderer.FfmpegRender(loader, p, output);
            var size = File.Exists(output) ? new FileInfo(output).Length : 0;
            var ok = size > 0;
            Console.WriteLine($"{(ok ? "[ok]  " : "[FAIL]")} end-to-end render -> {output} ({size} bytes, {renderer.GetRenderedFrames()} frames)");
            Console.WriteLine(ok ? "RENDER TEST PASSED" : "RENDER TEST FAILED");
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] render: {ex}");
            return 1;
        }
    }

    // Drives the live formula studio under a real message loop: the debounce timer must fire, the
    // background render must complete, and the result must marshal back and paint without throwing.
    public static int PreviewTest()
    {
        Exception? captured = null;
        Application.ThreadException += (_, e) => captured = e.Exception;
        ApplicationConfiguration.Initialize();
        Localization.Initialize();
        var form = new Form { Width = 900, Height = 700, StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-3000, -3000) };
        var config = new FractalScriptLoaderConfigure { Dock = DockStyle.Fill };
        config.SetOnLoadCallable(() => Task.CompletedTask);
        config.Init();
        form.Controls.Add(config);
        Theme.Apply(form);
        form.Show();
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < 3500)
        {
            Application.DoEvents();
            Thread.Sleep(30);
        }
        var loader = config.Get();             // exercises auto-compile + auto-build of the loader
        form.Close();
        form.Dispose();
        if (captured != null)
        {
            Console.WriteLine($"[FAIL] live preview threw: {captured}");
            return 1;
        }
        Console.WriteLine($"[ok]   live preview pumped under message loop; auto-built loader = {(loader != null ? loader.Size() + " keyframes" : "null")}");
        Console.WriteLine("PREVIEW TEST PASSED");
        return loader != null ? 0 : 1;
    }

    // Renders the actual laid-out main window to a PNG (no desktop/computer-use needed) for visual review.
    public static int UiShot()
    {
        ApplicationConfiguration.Initialize();
        Localization.Initialize();
        var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-5000, -5000) };
        form.Show();
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < 4500)
        {
            Application.DoEvents();
            Thread.Sleep(30);
        }
        var dir = Path.Combine(AppContext.BaseDirectory, "selftest_out");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "ui.png");
        using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(System.Drawing.Point.Empty, form.ClientSize));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        form.Close();
        form.Dispose();
        Console.WriteLine("UI shot saved: " + path);

        // Render exactly what the default preview would show, to tell a dark view from a preview bug.
        var fractal = FractalCompiler.CreateBuiltIn(0);
        var buffer = new float[512 * 512];
        PerturbationEngine.Render(fractal, BigDecimalCompat.Parse("-1.99999911758766165543764649311537154663"),
            BigDecimalCompat.Parse("-4.2402439547240753390707694210131039e-13"), 102, 512, 512, 1500, buffer);
        var (interior, escaped, _) = Classify(buffer);
        using (var preview = FractalColoring.Colorize(buffer, 512, 512, 0.167))
            preview.Save(Path.Combine(dir, "ui_preview_direct.png"), System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"default preview view: interior={interior} escaped={escaped}");
        return 0;
    }

    // The 腐化监视 material parameters (Assets/腐化监视.mat), mapped onto the ported shader coloring.
    public static ShaderColorParams CorruptionParams() => new()
    {
        Type = ShaderFractalType.Mandelbrot,
        PowerReal = -2.23,
        PowerImag = 0.0,
        Reverse = false,
        UseJulia = true,
        JuliaCXRaw = -77781,
        JuliaCYRaw = -23656,
        LeafOrbitOffset = 0.59,
        ColorR = 10.0,
        ColorG = 2.5,
        ColorB = 0.75,
        Iterations = 500,
    };

    // Scale 1e6 in the shader == half-width 10 == full width 20 == zooms = 2 - log2(20).
    public const double CorruptionFullZooms = -2.321928094887362;

    // Renders 腐化监视 at the full material view plus several candidate zoom targets, to confirm the
    // port looks right and to find a center that still has structure deep (not "the void").
    public static int ShaderTest(string[] args)
    {
        var p = CorruptionParams();
        var dir = Path.Combine(AppContext.BaseDirectory, "selftest_out");
        Directory.CreateDirectory(dir);

        RenderShader(p, 0, 0, CorruptionFullZooms, 900, Path.Combine(dir, "corruption_full.png"));

        // Grid-scan for a center that still has rich structure when zoomed deep (the Julia boundary),
        // scoring each candidate by colour variety at a probe depth.
        const double probeZooms = 14.0;
        const int probeSize = 110;
        var scored = new List<(double x, double y, int colors)>();
        for (var gx = -9.0; gx <= 9.0; gx += 1.5)
            for (var gy = -9.0; gy <= 9.0; gy += 1.5)
            {
                var image = new ShaderFractalImage(p, gx, gy, probeZooms, probeSize, probeSize);
                using var bitmap = image.GetImage();
                scored.Add((gx, gy, CountDistinctColors(bitmap)));
                image.Dispose();
            }
        scored.Sort((a, b) => b.colors.CompareTo(a.colors));
        Console.WriteLine($"Top centers with structure at zooms={probeZooms}:");
        for (var i = 0; i < 8 && i < scored.Count; i++)
            Console.WriteLine($"  ({scored[i].x,5:0.0}, {scored[i].y,5:0.0})  colors={scored[i].colors}");

        // Zoom ladder into the best candidate so we can eyeball that it stays detailed (not void).
        var best = scored[0];
        foreach (var z in new[] { 2.0, 8.0, 14.0, 20.0, 26.0 })
            RenderShader(p, best.x, best.y, z, 480, Path.Combine(dir, $"corruption_best_z{z:00}.png"));
        return 0;
    }

    // Renders the verified 腐化监视 zoom (center (3,9) on the Julia boundary, levels -2..18, which stays
    // detailed and never reaches the void) as a video. Usage: --corruptionvideo [seconds] [outputPath] [width] [height] [fps] [keyframeRes]
    public static int CorruptionVideo(string[] args)
    {
        var seconds = args.Length > 1 && double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 300.0;
        var output = args.Length > 2 ? args[2] : @"D:\Ruri\Github\Project\FractalParadise\Assets\腐化监视.mp4";
        var width = args.Length > 3 && int.TryParse(args[3], out var w) ? w : 1920;
        var height = args.Length > 4 && int.TryParse(args[4], out var h) ? h : 1080;
        var fps = args.Length > 5 && int.TryParse(args[5], out var f) ? f : 30;
        var keyframeRes = args.Length > 6 && int.TryParse(args[6], out var k) ? k : 1080;

        // Deep boundary center found by --deepprobe; stays detailed to the ~double-precision floor.
        const double centerX = 1.6960609410226892, centerY = 2.0804990249369038, startZooms = -2.0;
        var endZooms = args.Length > 7 && double.TryParse(args[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var ez) ? ez : 40.0;
        var p = CorruptionParams();
        p.Iterations = 2500; // deep zoom needs more iterations than the shallow material default (500)
        var loader = new ShaderFractalLoader(p, centerX, centerY, startZooms, endZooms, keyframeRes, keyframeRes);
        var count = loader.Size();
        var interpolator = new LinearInterpolator([0.0, seconds], [0.0, count - 1]);
        var renderer = new VideoRenderer();
        renderer.SetInterpolator(interpolator);
        renderer.SetMergeFrames(2);
        renderer.AddScaleIndicator(new MagnificationPowerIndicator { ReferenceZooms = startZooms });
        var parameters = new RenderParams(width, height, fps, 2, 0, 0, "ffmpeg", EncodingParam.X264);
        Console.WriteLine($"Rendering {seconds}s zoom · {count} keyframes (zooms {startZooms}..{endZooms}, ×10^{(endZooms - startZooms) * 0.30103:0.0}) · {width}x{height}@{fps} · keyframe {keyframeRes}px -> {output}");
        var start = Environment.TickCount64;
        renderer.FfmpegRender(loader, parameters, output);
        var elapsed = (Environment.TickCount64 - start) / 1000.0;
        var size = File.Exists(output) ? new FileInfo(output).Length : 0;
        Console.WriteLine($"done in {elapsed:0.0}s: {size} bytes, {renderer.GetRenderedFrames()} frames -> {output}");
        return size > 0 ? 0 : 1;
    }

    // Renders several FractalParadise materials at their full view to confirm style switching,
    // inversion (reverse) and complex powers all port correctly.
    public static int ShaderGallery()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "selftest_out");
        Directory.CreateDirectory(dir);
        var gallery = new (string name, ShaderColorParams p)[]
        {
            ("inversion_line", new ShaderColorParams { PowerReal = -1, Reverse = false, UseJulia = false }),
            ("shattered_julia", new ShaderColorParams { PowerReal = 2.02, Reverse = true, UseJulia = true, JuliaCXRaw = -77781, JuliaCYRaw = -23656 }),
            ("mandelbrot_fern", new ShaderColorParams { PowerReal = 3, PowerImag = 1, Reverse = true, UseJulia = false }),
            ("gluttonous_fish", new ShaderColorParams { PowerReal = 2, PowerImag = -0.67, Reverse = true, UseJulia = false, ColorR = 0.785, ColorG = 8.218, ColorB = 9.999 }),
        };
        foreach (var g in gallery)
            RenderShader(g.p, 0, 0, CorruptionFullZooms, 600, Path.Combine(dir, "gallery_" + g.name + ".png"));
        return 0;
    }

    // Follows the Julia boundary inward (recentering on the highest-escape pixel and zooming) to find a
    // center that stays on the fractal edge — the only way a fixed-center direct-iteration zoom keeps
    // detail instead of drifting into the void.
    public static (double centerX, double centerY, double zooms) FindDeepCenter(
        ShaderColorParams p, double startX, double startY, double startZooms, int steps, double stepZoom, int res)
    {
        double cx = startX, cy = startY, z = startZooms;
        var invR = 2.0 / (res - 1);
        for (var step = 0; step < steps; step++)
        {
            var half = Math.Pow(2.0, 1.0 - z);
            var rowBest = new (double escape, double x, double y)[res];
            Parallel.For(0, res, py =>
            {
                var uy = (res - 1 - py) * invR - 1.0;
                var ccy = cy + uy * half;
                (double escape, double x, double y) best = (-1, cx, cy);
                for (var px = 0; px < res; px++)
                {
                    var ux = px * invR - 1.0;
                    var ccx = cx + ux * half;
                    var it = BStyleColoring.EscapeIteration(new Cplx(ccx, ccy), p, p.Iterations);
                    if (it < p.Iterations && it > best.escape) best = (it, ccx, ccy);
                }
                rowBest[py] = best;
            });
            (double escape, double x, double y) overall = (-1, cx, cy);
            foreach (var r in rowBest) if (r.escape > overall.escape) overall = r;
            if (overall.escape < 0) break;
            cx = overall.x;
            cy = overall.y;
            Console.WriteLine($"  step {step,2}: center=({cx:0.########}, {cy:0.########}) zooms={z,5:0.0} maxEscape={overall.escape}");
            z += stepZoom;
        }
        return (cx, cy, z);
    }

    public static int DeepProbe()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "selftest_out");
        Directory.CreateDirectory(dir);
        var p = CorruptionParams();
        p.Iterations = 3000;
        Console.WriteLine("Following the Julia boundary inward to find a deep-zoomable center:");
        var (cx, cy, depth) = FindDeepCenter(p, 0, 0, CorruptionFullZooms, 16, 3.0, 130);
        Console.WriteLine($"Deep center = ({cx:R}, {cy:R}), reachable depth zooms ≈ {depth:0.0}");
        foreach (var z in new[] { CorruptionFullZooms, 8.0, 18.0, 28.0, 38.0, depth })
            RenderShader(p, cx, cy, z, 480, Path.Combine(dir, $"deep_z{z:00}.png"));
        return 0;
    }

    // The 腐化监视 Julia constant (its power-2 sibling is the 碎化Julia material).
    static readonly BigDecimalCompat JuliaReal = BigDecimalCompat.FromDouble(-0.77781);
    static readonly BigDecimalCompat JuliaImag = BigDecimalCompat.FromDouble(-0.23656);
    const double DeepLeafOffset = 0.59;

    public static int DeepJulia()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "selftest_out");
        Directory.CreateDirectory(dir);
        const int iterations = 2000;
        Console.WriteLine("Following the Julia boundary inward (arbitrary precision)…");
        var (cre, cim) = DeepOrbitTrapEngine.FindDeepCenter(JuliaReal, JuliaImag,
            BigDecimalCompat.Zero, BigDecimalCompat.Zero, -2.0, 30, 3.0, 130, iterations);
        Console.WriteLine($"Deep center:\n  re = {cre.ToPlainString()}\n  im = {cim.ToPlainString()}");
        Console.WriteLine("orbit-trap (eye) coloring:");
        foreach (var z in new[] { -2.0, 25.0, 55.0, 85.0 })
            RenderDeep(cre, cim, z, 480, iterations, true, Path.Combine(dir, $"deepjulia_trap_z{z:00}.png"));
        Console.WriteLine("escape-time coloring:");
        foreach (var z in new[] { -2.0, 25.0, 55.0, 85.0 })
            RenderDeep(cre, cim, z, 480, iterations, false, Path.Combine(dir, $"deepjulia_escape_z{z:00}.png"));
        return 0;
    }

    static void RenderDeep(BigDecimalCompat cre, BigDecimalCompat cim, double zooms, int size, int iterations, bool orbitTrap, string path)
    {
        var rgb = new float[size * size * 3];
        DeepOrbitTrapEngine.Render(cre, cim, JuliaReal, JuliaImag, zooms, size, size, iterations,
            DeepLeafOffset, 10.0, 2.5, 0.75, rgb, default, orbitTrap);
        using var bitmap = RgbToBitmap(rgb, size, size);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"[deep] {Path.GetFileName(path),-22} zooms={zooms,6:0.0} (×10^{(zooms + 2) * 0.30103:0.0}) distinctColors={CountDistinctColors(bitmap)}");
    }

    static Bitmap RgbToBitmap(float[] rgb, int width, int height)
    {
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                var row = (byte*)data.Scan0 + y * data.Stride;
                for (var x = 0; x < width; x++)
                {
                    var i = (y * width + x) * 3;
                    row[x * 3 + 0] = ToByte(rgb[i + 2]);
                    row[x * 3 + 1] = ToByte(rgb[i + 1]);
                    row[x * 3 + 2] = ToByte(rgb[i + 0]);
                }
            }
        }
        bitmap.UnlockBits(data);
        return bitmap;
    }

    static byte ToByte(double c) => double.IsNaN(c) ? (byte)0 : (byte)Math.Clamp((int)Math.Round(c * 255.0), 0, 255);

    static void RenderShader(ShaderColorParams p, double cx, double cy, double zooms, int size, string path)
    {
        var image = new ShaderFractalImage(p, cx, cy, zooms, size, size);
        using var bitmap = image.GetImage();
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        var distinct = CountDistinctColors(bitmap);
        Console.WriteLine($"[shader] {Path.GetFileName(path),-28} center=({cx},{cy}) zooms={zooms,7:0.00} distinctColors={distinct}");
        image.Dispose();
    }

    static (int interior, int escaped, int nan) Classify(float[] buffer)
    {
        int interior = 0, escaped = 0, nan = 0;
        foreach (var v in buffer)
        {
            if (float.IsNaN(v)) nan++;
            else if (v < 0) interior++;
            else escaped++;
        }
        return (interior, escaped, nan);
    }
}
