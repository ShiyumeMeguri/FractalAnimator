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
        if (args.Length > 0 && args[0].Equals("--precisionlimit", StringComparison.OrdinalIgnoreCase))
            return SelfTest.PrecisionLimit(args);
        if (args.Length > 0 && args[0].Equals("--stablecenter", StringComparison.OrdinalIgnoreCase))
            return SelfTest.StableCenter(args);
        if (args.Length > 0 && args[0].Equals("--bigdecimalcheck", StringComparison.OrdinalIgnoreCase))
            return SelfTest.BigDecimalCheck();
        if (args.Length > 0 && args[0].Equals("--deepcorruptioncompare", StringComparison.OrdinalIgnoreCase))
            return SelfTest.DeepCorruptionCompare(args);
        if (args.Length > 0 && args[0].Equals("--deeppixeltrace", StringComparison.OrdinalIgnoreCase))
            return SelfTest.DeepPixelTrace(args);
        if (args.Length > 0 && args[0].Equals("--glitchthresholdsweep", StringComparison.OrdinalIgnoreCase))
            return SelfTest.GlitchThresholdSweep(args);
        if (args.Length > 0 && args[0].Equals("--ddcheck", StringComparison.OrdinalIgnoreCase))
            return SelfTest.DoubleDoubleCheck();
        if (args.Length > 0 && args[0].Equals("--kernelbench", StringComparison.OrdinalIgnoreCase))
            return SelfTest.KernelBench(args);
        if (args.Length > 0 && args[0].Equals("--kerneltruth", StringComparison.OrdinalIgnoreCase))
            return SelfTest.KernelTruth(args);
        if (args.Length > 0 && args[0].Equals("--trapbound", StringComparison.OrdinalIgnoreCase))
            return FractalAnimator.Diagnostics.TrapBoundCheck.Run();
        if (args.Length > 0 && args[0].Equals("--kernelframe", StringComparison.OrdinalIgnoreCase))
            return SelfTest.KernelFrame(args);
        if (args.Length > 0 && args[0].Equals("--sequence", StringComparison.OrdinalIgnoreCase))
            return SelfTest.SequenceRender(args);
        if (args.Length > 0 && args[0].Equals("--sequencemux", StringComparison.OrdinalIgnoreCase))
            return SelfTest.SequenceMux(args);
        if (args.Length > 0 && args[0].Equals("--trajectorydiff", StringComparison.OrdinalIgnoreCase))
            return FractalAnimator.Diagnostics.TrajectoryDiff.Run(args);
        if (args.Length > 0 && args[0].Equals("--pixeltruth", StringComparison.OrdinalIgnoreCase))
            return SelfTest.PixelTruth(args);
        if (args.Length > 0 && args[0].Equals("--deepglitchrate", StringComparison.OrdinalIgnoreCase))
            return SelfTest.DeepGlitchRate(args);
        if (args.Length > 0 && args[0].Equals("--deepcorruptioncenter", StringComparison.OrdinalIgnoreCase))
            return SelfTest.DeepCorruptionCenter(args);
        if (args.Length > 0 && args[0].Equals("--corruptionvideodeep", StringComparison.OrdinalIgnoreCase))
            return SelfTest.CorruptionVideoDeep(args);
        if (args.Length > 0 && args[0].Equals("--deepcorruptioncentervariety", StringComparison.OrdinalIgnoreCase))
            return SelfTest.DeepCorruptionCenterVariety(args);

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

    // Deep boundary center found by --deepprobe; shared by --corruptionvideo and --precisionlimit so
    // the two never drift apart.
    public const double CorruptionDeepCenterX = 1.6960609410226892, CorruptionDeepCenterY = 2.0804990249369038, CorruptionDeepStartZooms = -2.0;

    public const string CorruptionSearchSeedReal = "1.6960609410233599087725501935007533031282139713494645128";
    public const string CorruptionSearchSeedImag = "2.0804990249382296516390607216160366206005654070041117876";

    public const string CorruptionCertifiedCenterReal = "1.6960609410233599087725501935007533044116921108898232113873453112368849267105262609353151";
    public const string CorruptionCertifiedCenterImag = "2.0804990249382296516390607216160366205488825339169652857397690815935914452900579435211160";

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
        const double centerX = CorruptionDeepCenterX, centerY = CorruptionDeepCenterY, startZooms = CorruptionDeepStartZooms;
        var endZooms = args.Length > 7 && double.TryParse(args[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var ez) ? ez : 40.0;
        var p = CorruptionParams();
        p.Iterations = 2500; // deep zoom needs more iterations than the shallow material default (500)
        var loader = new ShaderFractalLoader(p, centerX, centerY, startZooms, endZooms, keyframeRes, keyframeRes);
        var count = loader.Size();
        var interpolator = new LinearInterpolator([0.0, seconds], [0.0, count - 1]);
        var renderer = new VideoRenderer();
        renderer.SetInterpolator(interpolator);

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

    // Finds where --corruptionvideo's direct double iteration actually corrupts: the zoom level at
    // which neighbouring keyframe pixels alias to the identical double coordinate (centerX/centerY +
    // unit*halfWidth rounds to the same value), so the image stops gaining detail and goes blocky
    // instead. Checked two ways — analytically (counting distinct doubles across a probe row/column,
    // which is exact and instant) and empirically (rendering real keyframes and watching distinctColors
    // collapse), since chaotic sensitivity in the iteration could in principle move the visible floor
    // earlier than pure coordinate aliasing. Usage: --precisionlimit [keyframeRes]
    public static int PrecisionLimit(string[] args)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "selftest_out");
        Directory.CreateDirectory(dir);
        var res = args.Length > 1 && int.TryParse(args[1], out var r) ? r : 1080;
        const double centerX = CorruptionDeepCenterX, centerY = CorruptionDeepCenterY;

        Console.WriteLine($"Analytic aliasing scan (center=({centerX}, {centerY}), res={res}):");
        var invW = res > 1 ? 2.0 / (res - 1) : 0.0;
        double onset = -1, collapse = -1;
        for (var z = 20.0; z <= 60.0; z += 0.25)
        {
            var halfWidth = Math.Pow(2.0, 1.0 - z);
            var seenX = new HashSet<double>();
            var seenY = new HashSet<double>();
            for (var i = 0; i < res; i++)
            {
                var u = i * invW - 1.0;
                seenX.Add(centerX + u * halfWidth);
                seenY.Add(centerY + u * halfWidth);
            }
            var unique = Math.Min(seenX.Count, seenY.Count);
            if (onset < 0 && unique < res)
            {
                onset = z;
                Console.WriteLine($"  z={z,5:0.00}: first aliasing, unique={unique}/{res}");
            }
            if (unique <= 2)
            {
                collapse = z;
                Console.WriteLine($"  z={z,5:0.00}: total collapse, unique={unique}/{res}");
                break;
            }
        }
        Console.WriteLine($"Analytic prediction: aliasing onset ≈ {onset:0.00}, total collapse ≈ {collapse:0.00}");

        Console.WriteLine("Empirical probe renders (distinctColors over a 240px tile at the same center):");
        var p = CorruptionParams();
        p.Iterations = 2500;
        int? last = null;
        double visualOnset = -1;
        var lo = Math.Max(2.0, Math.Floor(onset) - 6);
        var hi = Math.Min(60.0, Math.Ceiling(collapse > 0 ? collapse : onset) + 3);
        for (var z = lo; z <= hi; z += 1.0)
        {
            var image = new ShaderFractalImage(p, centerX, centerY, z, 240, 240);
            using var bitmap = image.GetImage();
            var distinct = CountDistinctColors(bitmap);
            bitmap.Save(Path.Combine(dir, $"precisionlimit_z{z:00}.png"), System.Drawing.Imaging.ImageFormat.Png);
            var flag = last is int l && distinct < l * 0.5 ? "  <- collapsing" : "";
            Console.WriteLine($"  z={z,5:0.0}: distinctColors={distinct}{flag}");
            if (visualOnset < 0 && last is int l2 && distinct < l2 * 0.5) visualOnset = z;
            last = distinct;
            image.Dispose();
        }

        var limit = collapse > 0 ? collapse : onset;
        Console.WriteLine($"CURRENT PRECISION LIMIT (double, res={res}): analytic={limit:0.0} visualOnset={visualOnset:0.0} (×10^{(limit + 2) * 0.30103:0.0})");
        return 0;
    }

    // A fixed center used across a huge zoom range (58 doublings, here) is only "safe" if it sits
    // precisely enough on the Julia boundary that it never drifts into the void (flat interior/exterior)
    // *before* double precision itself runs out. FindDeepCenter's greedy search (highest-escape pixel on
    // a finite grid, recentred every few zooms) gives no such guarantee on its own — it was only ever
    // checked at its own checkpoint zooms. This walks the FULL range at fine (1 zoom) granularity and
    // reports distinctColors at every step, so a transient or early void collapse (as opposed to the
    // expected precision-floor collapse near the end) shows up as a dip anywhere but the tail.
    // Usage: --stablecenter [cx] [cy] [startZooms] [endZooms]
    public static int StableCenter(string[] args)
    {
        var cx = args.Length > 1 && double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : CorruptionDeepCenterX;
        var cy = args.Length > 2 && double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ? y : CorruptionDeepCenterY;
        var startZooms = args.Length > 3 && double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var sz) ? sz : CorruptionDeepStartZooms;
        var endZooms = args.Length > 4 && double.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var ez) ? ez : 56.0;
        var p = CorruptionParams();
        p.Iterations = 2500;

        Console.WriteLine($"Full-range stability sweep for ({cx}, {cy}), zooms {startZooms}..{endZooms}:");
        var counts = new List<(double z, int distinct)>();
        for (var z = startZooms; z <= endZooms; z += 1.0)
        {
            var image = new ShaderFractalImage(p, cx, cy, z, 200, 200);
            using var bitmap = image.GetImage();
            var distinct = CountDistinctColors(bitmap);
            counts.Add((z, distinct));
            image.Dispose();
        }

        // Flag a "void dip": a near-flat reading (<=8 distinct colours) that later RECOVERS above half
        // the run's peak. The expected precision-floor collapse is monotonic and permanent once it
        // starts (more zoom only worsens aliasing, never un-aliases it), so it never recovers — only a
        // genuine drift through/near a void pocket does. That's what tells the two apart, not distance
        // from the end of the range (a fixed "last N levels" cutoff false-positives on a long tail).
        var peak = counts.Max(c => c.distinct);
        var dips = new List<(double z, int distinct)>();
        for (var i = 0; i < counts.Count; i++)
        {
            if (counts[i].distinct > 8) continue;
            if (counts.Skip(i + 1).Any(c => c.distinct > peak / 2)) dips.Add(counts[i]);
        }
        var dipZooms = dips.Select(d => d.z).ToHashSet();
        foreach (var c in counts)
            Console.WriteLine($"  z={c.z,5:0.0}: distinctColors={c.distinct,4}{(dipZooms.Contains(c.z) ? "  <- VOID DIP" : "")}");

        Console.WriteLine(dips.Count == 0
            ? $"STABLE: no void dip anywhere in {startZooms}..{endZooms} (peak distinctColors={peak}); only the expected precision-floor collapse near the tail."
            : $"UNSTABLE: void dip(s) at zooms {string.Join(", ", dips.Select(d => d.z.ToString("0.0")))} — this center drifts off the boundary before the precision floor. Needs re-searching.");
        return dips.Count == 0 ? 0 : 1;
    }

    // Sanity-checks the new arbitrary-precision transcendentals (Divide/Sqrt/Exp/Ln/SinCos/Atan2/Pi) in
    // BigDecimalCompat against System.Math at a precision (40 digits) where double is still fully
    // trustworthy, so a wrong Taylor/Newton implementation is caught before it's relied on for a deep
    // render. Usage: --bigdecimalcheck
    public static int BigDecimalCheck()
    {
        var ctx = new MathContextCompat(40);
        var failures = 0;
        void Check(string name, double actual, double expected, double tol = 1e-12)
        {
            var diff = Math.Abs(actual - expected);
            var ok = diff <= tol * Math.Max(1.0, Math.Abs(expected));
            Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {name,-28} got={actual:G17} want={expected:G17} diff={diff:E3}");
            if (!ok) failures++;
        }

        Console.WriteLine("BigDecimalCompat vs System.Math (40-digit context):");
        double[] xs = [0.2, 1.0, 2.71828, 0.001, 17.5, 0.0001, 1024.0];
        foreach (var xd in xs)
        {
            var x = BigDecimalCompat.FromDouble(xd);
            Check($"Sqrt({xd})", x.Sqrt(ctx).ToDouble(), Math.Sqrt(xd));
            Check($"Ln({xd})", x.Ln(ctx).ToDouble(), Math.Log(xd));
        }
        // Exp is only ever called here on ln|z| (the reference orbit stops once |z| passes ~1000, so the
        // real argument range is roughly [-40, 7]) — skip inputs that overflow double on both sides of
        // the comparison itself (e.g. 1024, where Math.Exp returns +Infinity and Infinity-Infinity=NaN).
        double[] expXs = [0.2, 1.0, 2.71828, 0.001, 17.5, 0.0001, -30.0, 6.9];
        foreach (var xd in expXs)
            Check($"Exp({xd})", BigDecimalCompat.FromDouble(xd).Exp(ctx).ToDouble(), Math.Exp(xd));
        double[] angles = [0.1, 0.7, 1.5, 2.9, -0.3, -2.0, 3.14159];
        foreach (var ad in angles)
        {
            var a = BigDecimalCompat.FromDouble(ad);
            var (sin, cos) = BigDecimalCompat.SinCos(a, ctx);
            Check($"Sin({ad})", sin.ToDouble(), Math.Sin(ad));
            Check($"Cos({ad})", cos.ToDouble(), Math.Cos(ad));
            Check($"Atan({ad})", BigDecimalCompat.Atan(a, ctx).ToDouble(), Math.Atan(ad));
        }
        (double y, double x)[] quads = [(1, 1), (1, -1), (-1, -1), (-1, 1), (0.77781, -0.23656), (-0.001, 5.0), (5.0, 0.0), (-5.0, 0.0)];
        foreach (var (yd, xd) in quads)
        {
            var atan2 = BigDecimalCompat.Atan2(BigDecimalCompat.FromDouble(yd), BigDecimalCompat.FromDouble(xd), ctx);
            Check($"Atan2({yd},{xd})", atan2.ToDouble(), Math.Atan2(yd, xd));
        }
        Check("Pi", BigDecimalCompat.Pi(ctx).ToDouble(), Math.PI);
        Check("Divide(1/3)", BigDecimalCompat.FromDouble(1).Divide(BigDecimalCompat.FromDouble(3), ctx).ToDouble(), 1.0 / 3.0);

        // Complex z^p at moderate precision must match ShaderFractal.Pow (the double-precision port).
        (double zr, double zi, double pr, double pi)[] powCases =
            [(1.6960609410226892, 2.0804990249369038, -2.23, 0), (0.3, -0.7, -2.23, 0), (-1.1, 0.2, 3.0, 0)];
        foreach (var (zr, zi, pr, pim) in powCases)
        {
            var (re, im) = DeepOrbitTrapEngine.ComplexPow(BigDecimalCompat.FromDouble(zr), BigDecimalCompat.FromDouble(zi), pr, pim, ctx);
            var expected = ShaderFractal.Pow(new Cplx(zr, zi), new Cplx(pr, pim));
            Check($"ComplexPow.Re({zr},{zi})^{pr}", re.ToDouble(), expected.Re);
            Check($"ComplexPow.Im({zr},{zi})^{pr}", im.ToDouble(), expected.Im);
        }

        // Verify D1=f'(Z), D2H=0.5*f''(Z) for f(z)=z^p against central finite differences of ShaderFractal.Pow.
        (double zr, double zi)[] derivPts = [(1.6960609410226892, 2.0804990249369038), (0.104, 0.02), (0.3, -0.7), (-1.1, 0.2)];
        var pw = new Cplx(-2.23, 0);
        var pw1 = new Cplx(-3.23, 0);
        var pw2 = new Cplx(-4.23, 0);
        var pTimesPm1 = ShaderFractal.Mul(pw, pw1);
        foreach (var (zr, zi) in derivPts)
        {
            var z = new Cplx(zr, zi);
            var d1 = ShaderFractal.Mul(pw, ShaderFractal.Pow(z, pw1));
            var d2h = ShaderFractal.Mul(pTimesPm1, ShaderFractal.Pow(z, pw2)) * 0.5;
            const double h = 1e-6;
            var fPlus = ShaderFractal.Pow(new Cplx(zr + h, zi), pw);
            var fMinus = ShaderFractal.Pow(new Cplx(zr - h, zi), pw);
            var f0 = ShaderFractal.Pow(z, pw);
            var numD1 = (fPlus - fMinus) * (1.0 / (2 * h));
            var numD2 = (fPlus + fMinus - f0 * 2.0) * (1.0 / (h * h));
            Check($"D1.Re z=({zr},{zi})", d1.Re, numD1.Re, 1e-4);
            Check($"D1.Im z=({zr},{zi})", d1.Im, numD1.Im, 1e-4);
            Check($"D2H.Re z=({zr},{zi})", d2h.Re, numD2.Re / 2.0, 1e-3);
            Check($"D2H.Im z=({zr},{zi})", d2h.Im, numD2.Im / 2.0, 1e-3);
        }

        Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }

    // Renders 腐化监视 past the double-precision floor using DeepCorruptionLoader (arbitrary-precision
    // reference + 2nd-order perturbation, tiled glitch repair). The center must come from
    // --deepcorruptioncenter (or an equally verified deep search) — a plain double center's digits
    // beyond ~16 are meaningless past that point, so it's taken as exact decimal strings, not doubles.
    // Usage: --corruptionvideodeep <centerReal> <centerImag> [seconds] [out] [w] [h] [fps] [keyRes] [startZooms] [endZooms] [iterations]
    public static int CorruptionVideoDeep(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: --corruptionvideodeep <centerReal> <centerImag> [seconds] [out] [w] [h] [fps] [keyRes] [startZooms] [endZooms] [iterations]");
            return 1;
        }
        var centerReal = BigDecimalCompat.Parse(args[1]);
        var centerImag = BigDecimalCompat.Parse(args[2]);
        var seconds = args.Length > 3 && double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 60.0;
        var output = args.Length > 4 ? args[4] : @"D:\Ruri\Github\Project\FractalParadise\Assets\腐化监视.mp4";
        var width = args.Length > 5 && int.TryParse(args[5], out var w) ? w : 1920;
        var height = args.Length > 6 && int.TryParse(args[6], out var h) ? h : 1080;
        var fps = args.Length > 7 && int.TryParse(args[7], out var f) ? f : 30;
        var keyframeRes = args.Length > 8 && int.TryParse(args[8], out var k) ? k : 1080;
        var startZooms = args.Length > 9 && double.TryParse(args[9], NumberStyles.Float, CultureInfo.InvariantCulture, out var sz) ? sz : CorruptionDeepStartZooms;
        var endZooms = args.Length > 10 && double.TryParse(args[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var ez) ? ez : 340.0;
        var iterations = args.Length > 11 && int.TryParse(args[11], out var it) ? it : 400;

        var p = CorruptionParams();
        p.Iterations = iterations;
        var loader = new DeepCorruptionLoader(p, centerReal, centerImag, startZooms, endZooms, keyframeRes, keyframeRes);
        var count = loader.Size();
        var interpolator = new LinearInterpolator([0.0, seconds], [0.0, count - 1]);
        var renderer = new VideoRenderer();
        renderer.SetInterpolator(interpolator);

        renderer.AddScaleIndicator(new MagnificationPowerIndicator { ReferenceZooms = startZooms });
        var parameters = new RenderParams(width, height, fps, 2, 0, 0, "ffmpeg", EncodingParam.X264);
        Console.WriteLine($"Rendering {seconds}s zoom · {count} keyframes (zooms {startZooms}..{endZooms}, ×10^{(endZooms - startZooms) * 0.30103:0.0}) · {width}x{height}@{fps} · keyframe {keyframeRes}px · iterations={iterations} -> {output}");
        var start = Environment.TickCount64;
        renderer.FfmpegRender(loader, parameters, output);
        var elapsed = (Environment.TickCount64 - start) / 1000.0;
        var size = File.Exists(output) ? new FileInfo(output).Length : 0;
        Console.WriteLine($"done in {elapsed:0.0}s: {size} bytes, {renderer.GetRenderedFrames()} frames -> {output}");
        return size > 0 ? 0 : 1;
    }

    // Follows the 腐化监视 Julia boundary (power -2.23, arbitrary precision) from the already-verified
    // shallow center down to a target depth, logging progress so a long run can be monitored. Starts
    // from zooms=40 (not from scratch) deliberately: the perturbation-based grid search only works once
    // the grid's own physical span is already small — at shallow zoom the search grid covers a wide
    // enough area that every off-center candidate's delta violates the 2nd-order Taylor validity from
    // iteration 1 and glitches immediately, leaving only the trivially-valid center (delta=0) standing,
    // so the "search" just returns the seed every step (confirmed: escape count was bit-identical across
    // 13 widely-spaced shallow steps — a dead giveaway nothing was actually varying). The 0..45 range is
    // already fully verified void-free by --stablecenter using plain double, which has no such problem.
    // Usage: --deepcorruptioncenter [endZooms=340] [stepZoom=4] [gridRes=100] [iterations=300] [startZooms=40]
    public static int DeepCorruptionCenter(string[] args)
    {
        var endZooms = args.Length > 1 && double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var e) ? e : 340.0;
        var stepZoom = args.Length > 2 && double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var sz) ? sz : 4.0;
        var gridRes = args.Length > 3 && int.TryParse(args[3], out var g) ? g : 100;
        var iterations = args.Length > 4 && int.TryParse(args[4], out var it) ? it : 300;
        DeepOrbitTrapEngine.DebugLogging = true;
        var p = CorruptionParams();
        var startZooms = args.Length > 5 && double.TryParse(args[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var sz2) ? sz2 : 40.0;
        var radiusFraction = args.Length > 6 && double.TryParse(args[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var rf) ? rf : 1.0;
        var steps = (int)Math.Ceiling((endZooms - startZooms) / stepZoom);

        Console.WriteLine($"Following 腐化监视 boundary (power={p.PowerReal}) from zooms={startZooms} to ~{endZooms} in {steps} steps of {stepZoom}, grid={gridRes}, iterations={iterations}, radiusFraction={radiusFraction}");
        var start = Environment.TickCount64;
        var (cre, cim) = DeepOrbitTrapEngine.FindDeepCenter(
            BigDecimalCompat.FromDouble(p.JuliaCXRaw * 1e-5), BigDecimalCompat.FromDouble(p.JuliaCYRaw * 1e-5),
            p.PowerReal, p.PowerImag,
            BigDecimalCompat.Parse(CorruptionSearchSeedReal),
            BigDecimalCompat.Parse(CorruptionSearchSeedImag),
            startZooms, steps, stepZoom, gridRes, iterations, p.LeafOrbitOffset,
            (step, z, r, i, escape) => Console.WriteLine($"  step {step,3}/{steps}: zooms={z,6:0.0} escape={escape,5} elapsed={(Environment.TickCount64 - start) / 1000.0,6:0.0}s"),
            radiusFraction);
        var elapsed = (Environment.TickCount64 - start) / 1000.0;
        Console.WriteLine($"done in {elapsed:0.0}s");
        Console.WriteLine($"DEEP CENTER re = {cre.ToPlainString()}");
        Console.WriteLine($"DEEP CENTER im = {cim.ToPlainString()}");
        return 0;
    }

    // Same destination as --deepcorruptioncenter, different compass: scores small probe RENDERS (full
    // glitch repair included) by colour variety instead of a single raw pixel's escape count. Usage:
    // --deepcorruptioncentervariety [endZooms=340] [stepZoom=4] [candidateGrid=4] [probeSize=32] [iterations=200] [startZooms=40] [radiusFraction=0.3]
    public static int DeepCorruptionCenterVariety(string[] args)
    {
        var endZooms = args.Length > 1 && double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var e) ? e : 340.0;
        var stepZoom = args.Length > 2 && double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var sz) ? sz : 4.0;
        var candidateGrid = args.Length > 3 && int.TryParse(args[3], out var g) ? g : 4;
        var probeSize = args.Length > 4 && int.TryParse(args[4], out var ps) ? ps : 32;
        var iterations = args.Length > 5 && int.TryParse(args[5], out var it) ? it : 200;
        var startZooms = args.Length > 6 && double.TryParse(args[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var sz2) ? sz2 : 40.0;
        var radiusFraction = args.Length > 7 && double.TryParse(args[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var rf) ? rf : 0.3;
        var p = CorruptionParams();
        var steps = (int)Math.Ceiling((endZooms - startZooms) / stepZoom);

        Console.WriteLine($"Variety-search 腐化监视 boundary from zooms={startZooms} to ~{endZooms} in {steps} steps of {stepZoom}, candidates={candidateGrid * candidateGrid}, probeSize={probeSize}, iterations={iterations}, radiusFraction={radiusFraction}");
        var start = Environment.TickCount64;
        var (cre, cim) = DeepOrbitTrapEngine.FindDeepCenterByVariety(
            BigDecimalCompat.FromDouble(p.JuliaCXRaw * 1e-5), BigDecimalCompat.FromDouble(p.JuliaCYRaw * 1e-5),
            p.PowerReal, p.PowerImag,
            BigDecimalCompat.FromDouble(CorruptionDeepCenterX), BigDecimalCompat.FromDouble(CorruptionDeepCenterY),
            startZooms, steps, stepZoom, candidateGrid, probeSize, iterations, p.LeafOrbitOffset, p.ColorR, p.ColorG, p.ColorB,
            (step, z, r, i, score) => Console.WriteLine($"  step {step,3}/{steps}: zooms={z,6:0.0} bestDistinctColors={score,5} elapsed={(Environment.TickCount64 - start) / 1000.0,6:0.0}s"),
            radiusFraction);
        var elapsed = (Environment.TickCount64 - start) / 1000.0;
        Console.WriteLine($"done in {elapsed:0.0}s");
        Console.WriteLine($"DEEP CENTER re = {cre.ToPlainString()}");
        Console.WriteLine($"DEEP CENTER im = {cim.ToPlainString()}");
        return 0;
    }

    // Calibrates the chaos-safe iteration budget at a given depth: renders (with the tiled-repair debug
    // log on) and reports how many pixels glitch initially and how many remain unresolved after repair —
    // no "direct" comparison, because double-precision direct iteration is itself meaningless past its
    // own ~10^16 wall, so it can't serve as ground truth at the depths this engine actually targets.
    // Usage: --deepglitchrate [zooms] [size=200] [iterations=300]
    public static int DeepGlitchRate(string[] args)
    {
        var zooms = args.Length > 1 && double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ? z : 100.0;
        var size = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 200;
        var iterations = args.Length > 3 && int.TryParse(args[3], out var it) ? it : 300;
        var centerReal = args.Length > 5 ? BigDecimalCompat.Parse(args[4]) : BigDecimalCompat.FromDouble(CorruptionDeepCenterX);
        var centerImag = args.Length > 5 ? BigDecimalCompat.Parse(args[5]) : BigDecimalCompat.FromDouble(CorruptionDeepCenterY);
        var p = CorruptionParams();
        p.Iterations = iterations;
        DeepOrbitTrapEngine.DebugLogging = true;
        var rgb = new float[size * size * 3];
        var start = Environment.TickCount64;
        DeepOrbitTrapEngine.Render(centerReal, centerImag,
            BigDecimalCompat.FromDouble(p.JuliaCXRaw * 1e-5), BigDecimalCompat.FromDouble(p.JuliaCYRaw * 1e-5),
            p.PowerReal, p.PowerImag, zooms, size, size, iterations, p.LeafOrbitOffset, p.ColorR, p.ColorG, p.ColorB, rgb);
        var elapsed = (Environment.TickCount64 - start) / 1000.0;
        using var bitmap = RgbToBitmap(rgb, size, size);
        var dir = Path.Combine(AppContext.BaseDirectory, "selftest_out");
        Directory.CreateDirectory(dir);
        bitmap.Save(Path.Combine(dir, $"glitchrate_z{zooms:0}_it{iterations}.png"), System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"zooms={zooms} size={size} iterations={iterations}: {elapsed:0.0}s, distinctColors={CountDistinctColors(bitmap)}");
        return 0;
    }

    // Traces one pixel's trajectory two ways, iteration by iteration: (a) direct double iteration of its
    // own exact coordinate (ground truth) and (b) the reference orbit + reconstructed perturbation delta
    // (Z_n + scale*delta_n). Prints both and their difference every iteration, to find exactly where (if
    // anywhere) they part ways, rather than inferring it from aggregate image statistics.
    // Usage: --deeppixeltrace [zooms=20] [pixelOffsetX=0.3] [iterations=45]
    public static int DeepPixelTrace(string[] args)
    {
        var zooms = args.Length > 1 && double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ? z : 20.0;
        var offsetFrac = args.Length > 2 && double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var o) ? o : 0.3;
        var iterations = args.Length > 3 && int.TryParse(args[3], out var it) ? it : 45;

        var p = CorruptionParams();
        var juliaConst = new Cplx(p.JuliaCXRaw * 1e-5, p.JuliaCYRaw * 1e-5);

        var reference = DeepOrbitTrapEngine.BuildReference(
            BigDecimalCompat.FromDouble(CorruptionDeepCenterX), BigDecimalCompat.FromDouble(CorruptionDeepCenterY),
            BigDecimalCompat.FromDouble(juliaConst.Re), BigDecimalCompat.FromDouble(juliaConst.Im),
            p.PowerReal, p.PowerImag, zooms, iterations, default);

        var spacingLog2 = 2.0 - zooms;
        var scaleExp = (int)Math.Floor(spacingLog2);
        var mantissa = Math.Pow(2.0, spacingLog2 - scaleExp);
        var seedScale = Math.ScaleB(1.0, scaleExp);
        var deltaCReal = offsetFrac * mantissa; // rescaled-units seed, matching Render's offsetToDelta*pixelIndex convention
        const double deltaCImag = 0.0;

        var trueZ = new Cplx(CorruptionDeepCenterX + seedScale * deltaCReal, CorruptionDeepCenterY + seedScale * deltaCImag);
        var delta = new DdComplex(deltaCReal, deltaCImag);
        Console.WriteLine($"zooms={zooms} scaleExp={scaleExp} pixelOffset(rescaled)=({deltaCReal:E3},{deltaCImag:E3}) true physical offset={seedScale * deltaCReal:E3}");
        Console.WriteLine($"{"n",3}  {"direct.Re",14} {"direct.Im",14}  {"perturb.Re",14} {"perturb.Im",14}  {"|diff|",10}  {"|delta_true|",12}  {"|Z_ref|",10}");
        for (var n = 0; n < Math.Min(iterations, reference.Length - 1); n++)
        {
            DeepOrbitTrapEngine.PerturbStep(reference, n, ref delta, ref scaleExp);

            trueZ = ShaderFractal.Apply(ShaderFractalType.Mandelbrot, trueZ, juliaConst, p.PowerReal, p.PowerImag);
            var dTrue = DdComplex.ScaleB(delta, scaleExp);
            var perturbDd = reference.Z[n + 1] + dTrue;
            var perturbZ = new Cplx(perturbDd.Re.ToDouble(), perturbDd.Im.ToDouble());
            var diff = trueZ - perturbZ;
            var diffMag = Math.Sqrt(diff.Dot);
            var trueDeltaMag = Math.Sqrt(dTrue.MagnitudeSquared);
            var refMag = Math.Sqrt(reference.MagnitudeSquared[n + 1]);
            Console.WriteLine($"{n + 1,3}  {trueZ.Re,14:F8} {trueZ.Im,14:F8}  {perturbZ.Re,14:F8} {perturbZ.Im,14:F8}  {diffMag,10:E3}  {trueDeltaMag,12:E3}  {refMag,10:E3}");
        }
        return 0;
    }

    /// <summary>
    /// Ground truth for a single pixel: no perturbation, no reference orbit, no double anywhere in the
    /// iteration — just z ← z^p + K carried entirely in arbitrary precision. Far too slow to render an
    /// image with, but it is the only impartial way to settle which of two fast paths is right about a
    /// disputed pixel. Mirrors BStyleColoring.BStyle's loop bounds and bailout exactly.
    /// </summary>
    static (int iterations, double trap, double finalMagSq) TruePixel(
        BigDecimalCompat cRe, BigDecimalCompat cIm, BigDecimalCompat kRe, BigDecimalCompat kIm,
        double powerReal, double powerImag, int maxIterations, double leafOffset, int precision)
    {
        var ctx = new MathContextCompat(precision);
        var zr = cRe;
        var zi = cIm;
        var trap = 1e5;
        var finalMagSq = 1.0;
        var iters = 1;
        for (; iters < maxIterations; iters++)
        {
            var (pr, pi) = DeepOrbitTrapEngine.ComplexPow(zr, zi, powerReal, powerImag, ctx);
            zr = pr.Add(kRe, ctx);
            zi = pi.Add(kIm, ctx);
            var r = zr.ToDouble();
            var i = zi.ToDouble();
            finalMagSq = r * r + i * i;
            if (finalMagSq > 1024.0) break;
            trap = Math.Min(trap, BStyleColoring.LeafTrapValue(r, i, leafOffset));
        }
        return (iters, trap, finalMagSq);
    }

    // Renders the same view with the repair-forcing threshold and with the double-double-calibrated one,
    // finds every pixel where they disagree, and adjudicates each with full arbitrary-precision direct
    // iteration. This is the test that decides whether dropping the repair is correct or is quietly
    // accepting bad pixels. Usage: --pixeltruth [zooms] [size] [iterations]
    public static int PixelTruth(string[] args)
    {
        var zooms = args.Length > 1 && double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ? z : 120.0;
        var size = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 240;
        var iterations = args.Length > 3 && int.TryParse(args[3], out var it) ? it : 300;
        var cx = BigDecimalCompat.Parse(CorruptionCertifiedCenterReal);
        var cy = BigDecimalCompat.Parse(CorruptionCertifiedCenterImag);
        var p = CorruptionParams();
        p.Iterations = iterations;
        var kRe = BigDecimalCompat.FromDouble(p.JuliaCXRaw * 1e-5);
        var kIm = BigDecimalCompat.FromDouble(p.JuliaCYRaw * 1e-5);

        float[] Render(double factor)
        {
            DeepOrbitTrapEngine.GlitchFactor = factor;
            var rgb = new float[size * size * 3];
            DeepOrbitTrapEngine.Render(cx, cy, kRe, kIm, p.PowerReal, p.PowerImag, zooms, size, size,
                iterations, p.LeafOrbitOffset, p.ColorR, p.ColorG, p.ColorB, rgb);
            return rgb;
        }

        var repaired = Render(1.0e-6);
        var plain = Render(1.0e-32);
        DeepOrbitTrapEngine.GlitchFactor = 1.0e-32;

        var differing = new List<int>();
        for (var i = 0; i < size * size; i++)
        {
            var d = Math.Abs(repaired[i * 3] - plain[i * 3]) + Math.Abs(repaired[i * 3 + 1] - plain[i * 3 + 1]) + Math.Abs(repaired[i * 3 + 2] - plain[i * 3 + 2]);
            if (d * 255.0 > 3.0) differing.Add(i);
        }
        Console.WriteLine($"zooms={zooms} size={size}: {differing.Count} of {size * size} pixels differ between repaired and double-double-threshold renders");
        if (differing.Count == 0) { Console.WriteLine("IDENTICAL — repair changes nothing"); return 0; }

        var spacingLog2 = 2.0 - zooms;
        var scaleExp = (int)Math.Floor(spacingLog2);
        var mantissa = Math.Pow(2.0, spacingLog2 - scaleExp);
        var offsetToDelta = mantissa / size;
        var half = size / 2.0;
        var precision = Math.Max(60, (int)(zooms * 0.30103) + (int)(iterations * 0.25) + 40);
        var bigCtx = new MathContextCompat(precision);
        var scaleBig = BigDecimalCompat.Pow2(scaleExp);

        int repairedWins = 0, plainWins = 0, neither = 0;
        var sample = differing.Count <= 8 ? differing : differing.Where((_, i) => i % (differing.Count / 8) == 0).Take(8).ToList();
        foreach (var idx in sample)
        {
            var px = idx % size;
            var py = idx / size;
            var cRe = cx.Add(BigDecimalCompat.FromDouble((px - half) * offsetToDelta).Multiply(scaleBig, bigCtx), bigCtx);
            var cIm = cy.Add(BigDecimalCompat.FromDouble((half - py) * offsetToDelta).Multiply(scaleBig, bigCtx), bigCtx);
            var (ti, ttrap, tmag) = TruePixel(cRe, cIm, kRe, kIm, p.PowerReal, p.PowerImag, iterations, p.LeafOrbitOffset, precision);
            var (tr, tg, tb) = BStyleColoring.ColorFromTrap(ttrap, ti, tmag, p.ColorR, p.ColorG, p.ColorB);
            double DiffTo(float[] img) =>
                (Math.Abs(img[idx * 3] - tr) + Math.Abs(img[idx * 3 + 1] - tg) + Math.Abs(img[idx * 3 + 2] - tb)) * 255.0 / 3.0;
            var dRep = DiffTo(repaired);
            var dPlain = DiffTo(plain);
            var verdict = Math.Abs(dRep - dPlain) < 1.0 ? "tie" : dRep < dPlain ? "REPAIRED" : "DOUBLE-DOUBLE";
            if (verdict == "REPAIRED") repairedWins++; else if (verdict == "DOUBLE-DOUBLE") plainWins++; else neither++;
            Console.WriteLine($"  pixel({px,4},{py,4}) trueIters={ti,4}  |repaired-truth|={dRep,7:0.0}  |dd-truth|={dPlain,7:0.0}  -> {verdict}");
        }
        Console.WriteLine($"VERDICT: repaired closer on {repairedWins}, double-double closer on {plainWins}, tie on {neither}");
        return 0;
    }

    // Head-to-head: the shipping engine versus the certified microkernel, same centre, same zoom ladder,
    // same iteration budget. Reports the ladder's pixel retirement distribution, because that — not the
    // wall clock alone — is what explains the new engine's cost and whether the design's central claim
    // (most pixels retire on a cheap rung) actually holds on this fractal.
    // Usage: --kernelbench [resolution=480] [iterations=300]
    static FractalAnimator.Rendering.SequenceSpec BuildSequenceSpec(string[] args)
    {
        var outDir = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "sequence_out");
        var startZooms = args.Length > 2 && double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var sz) ? sz : 0.0;
        var endZooms = args.Length > 3 && double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var ez) ? ez : 170.0;
        var frames = args.Length > 4 && int.TryParse(args[4], out var fr) ? fr : 300;
        var width = args.Length > 5 && int.TryParse(args[5], out var w) ? w : 1920;
        var height = args.Length > 6 && int.TryParse(args[6], out var h) ? h : 1080;
        var iterations = args.Length > 7 && int.TryParse(args[7], out var it) ? it : 300;

        var p = CorruptionParams();
        return new FractalAnimator.Rendering.SequenceSpec
        {
            OutputDirectory = outDir,
            CenterReal = BigDecimalCompat.Parse(CorruptionCertifiedCenterReal),
            CenterImag = BigDecimalCompat.Parse(CorruptionCertifiedCenterImag),
            JuliaRawReal = p.JuliaCXRaw * 1e-5,
            JuliaRawImag = p.JuliaCYRaw * 1e-5,
            PowerReal = p.PowerReal,
            PowerImag = p.PowerImag,
            LeafOffset = p.LeafOrbitOffset,
            ColorR = p.ColorR,
            ColorG = p.ColorG,
            ColorB = p.ColorB,
            StartZooms = startZooms,
            EndZooms = endZooms,
            Frames = frames,
            Width = width,
            Height = height,
            Iterations = iterations,
        };
    }

    public static int SequenceRender(string[] args)
    {
        var spec = BuildSequenceSpec(args);
        Console.WriteLine($"sequence -> {spec.OutputDirectory}");
        Console.WriteLine($"  zoom {spec.StartZooms}..{spec.EndZooms} frames={spec.Frames} {spec.Width}x{spec.Height} iterations={spec.Iterations}");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("interrupt received; finishing the frame in flight then stopping");
            cts.Cancel();
        };

        var start = Environment.TickCount64;
        var rendered = 0;
        var skipped = 0;
        try
        {
            FractalAnimator.Rendering.ResumableSequenceRenderer.Render(spec, progress =>
            {
                if (progress.Skipped) { skipped++; return; }
                rendered++;
                var elapsed = (Environment.TickCount64 - start) / 1000.0;
                var remaining = rendered > 0 ? elapsed / rendered * (progress.Total - progress.Completed) : 0;
                Console.WriteLine($"  frame {progress.FrameIndex + 1}/{progress.Total} zoom={progress.Zooms,7:0.00} uncert={progress.Uncertified} {progress.RenderMilliseconds:0}ms eta={remaining / 60.0:0.0}min");
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"stopped: {rendered} rendered, {skipped} already present. rerun the same command to resume.");
            return 2;
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine(ex.Message);
            return 1;
        }

        Console.WriteLine($"done: {rendered} rendered, {skipped} resumed from disk, {(Environment.TickCount64 - start) / 1000.0:0.0}s");
        return 0;
    }

    public static int SequenceMux(string[] args)
    {
        var spec = BuildSequenceSpec(args);
        var output = args.Length > 8 ? args[8] : Path.Combine(spec.OutputDirectory, "sequence.mp4");
        var fps = args.Length > 9 && int.TryParse(args[9], out var f) ? f : 30;

        var missing = new List<int>();
        for (var i = 0; i < spec.Frames; i++)
            if (!File.Exists(FractalAnimator.Rendering.ResumableSequenceRenderer.FramePath(spec, i))) missing.Add(i);
        if (missing.Count > 0)
        {
            Console.WriteLine($"cannot mux: {missing.Count} frames missing (first={missing[0]}). run --sequence to finish them.");
            return 1;
        }

        var pattern = Path.Combine(spec.OutputDirectory, "frame_%06d.png");
        var arguments = $"-y -framerate {fps} -i \"{pattern}\" -c:v libx264 -crf 18 -preset slow -pix_fmt yuv420p \"{output}\"";
        Console.WriteLine($"ffmpeg {arguments}");
        var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg", arguments) { UseShellExecute = false };
        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null) { Console.WriteLine("failed to start ffmpeg"); return 1; }
        process.WaitForExit();
        if (process.ExitCode != 0) { Console.WriteLine($"ffmpeg exited {process.ExitCode}"); return process.ExitCode; }

        var size = File.Exists(output) ? new FileInfo(output).Length : 0;
        Console.WriteLine($"muxed {spec.Frames} frames -> {output} ({size / 1024.0 / 1024.0:0.0} MB)");
        return 0;
    }

    public static int KernelFrame(string[] args)
    {
        var zooms = args.Length > 1 && double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ? z : 120.0;
        var size = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 600;
        var iterations = args.Length > 3 && int.TryParse(args[3], out var it) ? it : 300;

        var cx = BigDecimalCompat.Parse(CorruptionCertifiedCenterReal);
        var cy = BigDecimalCompat.Parse(CorruptionCertifiedCenterImag);
        var p = CorruptionParams();
        p.Iterations = iterations;
        var baked = FractalAnimator.Core.Atoms.PowerJuliaBaker.Bake(
            cx, cy, p.JuliaCXRaw * 1e-5, p.JuliaCYRaw * 1e-5, p.PowerReal, p.PowerImag, iterations);

        var rgb = new float[size * size * 3];
        var stats = FractalAnimator.Core.Kernel.CertifiedRenderer.Render(baked, zooms, size, size, iterations, rgb);

        var dir = Path.Combine(AppContext.BaseDirectory, "selftest_out");
        Directory.CreateDirectory(dir);
        using var bitmap = RgbToBitmap(rgb, size, size);
        var path = Path.Combine(dir, $"kernelframe_z{zooms:000}.png");
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"zoom={zooms} size={size} uncert={stats.Uncertified} rungs={string.Join("/", stats.RetiredPerRung)} distinctColors={CountDistinctColors(bitmap)} {stats.TotalMilliseconds:0}ms");
        {
            var r32 = FractalAnimator.Core.Kernel.CertifiedIterator<FractalAnimator.Core.Kernel.Fp32,
                FractalAnimator.Core.Atoms.PowerJuliaAtom<FractalAnimator.Core.Kernel.Fp32>,
                FractalAnimator.Core.Atoms.BStyleTrapAtom>.EscalationReasons;
            var rdd = FractalAnimator.Core.Kernel.CertifiedIterator<DoubleDouble,
                FractalAnimator.Core.Atoms.PowerJuliaAtom<DoubleDouble>,
                FractalAnimator.Core.Atoms.BStyleTrapAtom>.EscalationReasons;
            var rqd = FractalAnimator.Core.Kernel.CertifiedIterator<FractalAnimator.Core.Kernel.QuadDouble,
                FractalAnimator.Core.Atoms.PowerJuliaAtom<FractalAnimator.Core.Kernel.QuadDouble>,
                FractalAnimator.Core.Atoms.BStyleTrapAtom>.EscalationReasons;
            var rff = FractalAnimator.Core.Kernel.CertifiedIterator<FractalAnimator.Core.Kernel.FloatFloat,
                FractalAnimator.Core.Atoms.PowerJuliaAtom<FractalAnimator.Core.Kernel.FloatFloat>,
                FractalAnimator.Core.Atoms.BStyleTrapAtom>.EscalationReasons;
            var r64 = FractalAnimator.Core.Kernel.CertifiedIterator<FractalAnimator.Core.Kernel.Fp64,
                FractalAnimator.Core.Atoms.PowerJuliaAtom<FractalAnimator.Core.Kernel.Fp64>,
                FractalAnimator.Core.Atoms.BStyleTrapAtom>.EscalationReasons;
            Console.WriteLine($"  esc FP32 ball={r32[0]} supInf={r32[1]} denom={r32[2]} cap={r32[3]} escMargin={r32[4]} colour={r32[5]} orbitEnd={r32[6]} rebaseCap={r32[7]}");
            Console.WriteLine($"  esc FF32 ball={rff[0]} supInf={rff[1]} denom={rff[2]} cap={rff[3]} escMargin={rff[4]} colour={rff[5]} orbitEnd={rff[6]} rebaseCap={rff[7]}");
            Console.WriteLine($"  esc FP64 ball={r64[0]} supInf={r64[1]} denom={r64[2]} cap={r64[3]} escMargin={r64[4]} colour={r64[5]} orbitEnd={r64[6]} rebaseCap={r64[7]}");
            Console.WriteLine($"  esc DD   ball={rdd[0]} supInf={rdd[1]} denom={rdd[2]} cap={rdd[3]} escMargin={rdd[4]} colour={rdd[5]} orbitEnd={rdd[6]} rebaseCap={rdd[7]}");
            Console.WriteLine($"  esc QD   ball={rqd[0]} supInf={rqd[1]} denom={rqd[2]} cap={rqd[3]} escMargin={rqd[4]} colour={rqd[5]} orbitEnd={rqd[6]} rebaseCap={rqd[7]}");
            Console.WriteLine($"  batchMode={FractalAnimator.Core.Kernel.CertifiedIteratorBatch.Mode} batchEsc={FractalAnimator.Core.Kernel.CertifiedIteratorBatch.EscalationReport()}");
            var cb = FractalAnimator.Core.Kernel.CertifiedIterator<DoubleDouble,
                FractalAnimator.Core.Atoms.PowerJuliaAtom<DoubleDouble>,
                FractalAnimator.Core.Atoms.BStyleTrapAtom>.CapBuckets;
            var cd = FractalAnimator.Core.Kernel.CertifiedIterator<DoubleDouble,
                FractalAnimator.Core.Atoms.PowerJuliaAtom<DoubleDouble>,
                FractalAnimator.Core.Atoms.BStyleTrapAtom>.CapDecoupledSum;
            var capTotal = cb[0] + cb[1] + cb[2] + cb[3] + cb[4];
            Console.WriteLine($"  DD capBuckets <1={cb[0]} <1e3={cb[1]} <1e9={cb[2]} huge={cb[3]} nonfinite={cb[4]} avgDecoupled={(capTotal > 0 ? cd / (double)capTotal : 0):0.0}");
        }
        {
            var probeOrbit = FractalAnimator.Core.Atoms.PowerJuliaBaker.Narrow<FractalAnimator.Core.Kernel.Fp32>(baked);
            var sp = 2.0 - zooms;
            var se = (int)Math.Floor(sp);
            var man = Math.Pow(2.0, sp - se);
            var otd = man / size;
            var halfPix = size / 2.0;
            for (var probe = 0; probe < 3; probe++)
            {
                var px = size / 4 + probe * size / 4;
                var seedProbe = FractalAnimator.Core.Kernel.Cx<FractalAnimator.Core.Kernel.Fp32>.FromDouble(
                    (px - halfPix) * otd, (halfPix - px) * otd);
                var rr = FractalAnimator.Core.Kernel.CertifiedIterator<FractalAnimator.Core.Kernel.Fp32,
                    FractalAnimator.Core.Atoms.PowerJuliaAtom<FractalAnimator.Core.Kernel.Fp32>,
                    FractalAnimator.Core.Atoms.BStyleTrapAtom>.Iterate(probeOrbit, seedProbe, se, iterations);
                var orbD = FractalAnimator.Core.Atoms.PowerJuliaBaker.Narrow<DoubleDouble>(baked);
                var seedD = FractalAnimator.Core.Kernel.Cx<DoubleDouble>.FromDouble(
                    (px - halfPix) * otd, (halfPix - px) * otd);
                var rd = FractalAnimator.Core.Kernel.CertifiedIterator<DoubleDouble,
                    FractalAnimator.Core.Atoms.PowerJuliaAtom<DoubleDouble>,
                    FractalAnimator.Core.Atoms.BStyleTrapAtom>.Iterate(orbD, seedD, se, iterations);
                Console.WriteLine($"  probe px={px} FP32({rr.Verdict},{rr.Iterations},dec={rr.DecoupledSteps},trap={rr.Color.TrapMin:G8}) DD({rd.Verdict},{rd.Iterations},dec={rd.DecoupledSteps},trap={rd.Color.TrapMin:G8})");
            }
        }
        return stats.Uncertified == 0 ? 0 : 1;
    }

    public static int KernelTruth(string[] args)
    {
        var zooms = args.Length > 1 && double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ? z : 120.0;
        var size = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 96;
        var iterations = args.Length > 3 && int.TryParse(args[3], out var it) ? it : 300;
        var samples = args.Length > 4 && int.TryParse(args[4], out var sm) ? sm : 24;

        var cx = BigDecimalCompat.Parse(CorruptionCertifiedCenterReal);
        var cy = BigDecimalCompat.Parse(CorruptionCertifiedCenterImag);
        var p = CorruptionParams();
        p.Iterations = iterations;
        var kRe = p.JuliaCXRaw * 1e-5;
        var kIm = p.JuliaCYRaw * 1e-5;

        FractalAnimator.Core.Kernel.KernelOptions.RebaseEnabled =
            !(args.Length > 5 && args[5].Equals("norebase", StringComparison.OrdinalIgnoreCase));
        var baked = FractalAnimator.Core.Atoms.PowerJuliaBaker.Bake(cx, cy, kRe, kIm, p.PowerReal, p.PowerImag, iterations);
        var rgb = new float[size * size * 3];
        var stats = FractalAnimator.Core.Kernel.CertifiedRenderer.Render(baked, zooms, size, size, iterations, rgb);
        Console.WriteLine($"kernel: zooms={zooms} size={size} rebase={FractalAnimator.Core.Kernel.KernelOptions.RebaseEnabled} uncert={stats.Uncertified} rungs={string.Join("/", stats.RetiredPerRung)}");

        var spacingLog2 = 2.0 - zooms;
        var scaleExp = (int)Math.Floor(spacingLog2);
        var mantissa = Math.Pow(2.0, spacingLog2 - scaleExp);
        var offsetToDelta = mantissa / size;
        var half = size / 2.0;

        var stride = Math.Max(1, size * size / samples);
        int checkedPixels = 0, exact = 0, mismatched = 0;
        var worst = 0.0;
        for (var index = 0; index < size * size; index += stride)
        {
            var px = index % size;
            var py = index / size;
            var ok = FractalAnimator.Core.Atoms.ArbitraryPrecisionFallback.TryResolve(
                cx, cy, baked.JuliaReal, baked.JuliaImag, p.PowerReal, p.PowerImag,
                (px - half) * offsetToDelta, (half - py) * offsetToDelta, scaleExp,
                iterations, p.LeafOrbitOffset, out var tr, out var tg, out var tb, out var prec);
            if (!ok) continue;
            checkedPixels++;
            var o = index * 3;
            var d = (Math.Abs(rgb[o] - tr) + Math.Abs(rgb[o + 1] - tg) + Math.Abs(rgb[o + 2] - tb)) * 255.0 / 3.0;
            if (d > worst) worst = d;
            if (d < 0.5) exact++;
            else
            {
                mismatched++;
                if (mismatched <= 6)
                {
                    var okHi = FractalAnimator.Core.Atoms.ArbitraryPrecisionFallback.TryResolveAt(
                        cx, cy, baked.JuliaReal, baked.JuliaImag, p.PowerReal, p.PowerImag,
                        (px - half) * offsetToDelta, (half - py) * offsetToDelta, scaleExp,
                        iterations, p.LeafOrbitOffset, prec + 240, out var hr, out var hg, out var hb);
                    var dHi = okHi ? (Math.Abs(rgb[o] - hr) + Math.Abs(rgb[o + 1] - hg) + Math.Abs(rgb[o + 2] - hb)) * 255.0 / 3.0 : double.NaN;
                    var dApHi = okHi ? (Math.Abs(tr - hr) + Math.Abs(tg - hg) + Math.Abs(tb - hb)) * 255.0 / 3.0 : double.NaN;
                    Console.WriteLine($"  MISMATCH ({px},{py}) kernel-vs-ap={d:0.00} apPrec={prec} | kernel-vs-ap{prec + 240}={dHi:0.00} ap-vs-ap{prec + 240}={dApHi:0.00}");
                    var seed = FractalAnimator.Core.Kernel.Cx<FractalAnimator.Core.Kernel.Fp32>.FromDouble((px - half) * offsetToDelta, (half - py) * offsetToDelta);
                    var orb32 = FractalAnimator.Core.Atoms.PowerJuliaBaker.Narrow<FractalAnimator.Core.Kernel.Fp32>(baked);
                    var res32 = FractalAnimator.Core.Kernel.CertifiedIterator<FractalAnimator.Core.Kernel.Fp32,
                        FractalAnimator.Core.Atoms.PowerJuliaAtom<FractalAnimator.Core.Kernel.Fp32>,
                        FractalAnimator.Core.Atoms.BStyleTrapAtom>.Iterate(orb32, seed, scaleExp, iterations);
                    var seedD = FractalAnimator.Core.Kernel.Cx<DoubleDouble>.FromDouble((px - half) * offsetToDelta, (half - py) * offsetToDelta);
                    var orbD = FractalAnimator.Core.Atoms.PowerJuliaBaker.Narrow<DoubleDouble>(baked);
                    var resD = FractalAnimator.Core.Kernel.CertifiedIterator<DoubleDouble,
                        FractalAnimator.Core.Atoms.PowerJuliaAtom<DoubleDouble>,
                        FractalAnimator.Core.Atoms.BStyleTrapAtom>.Iterate(orbD, seedD, scaleExp, iterations);
                    Console.WriteLine($"    FP32 verdict={res32.Verdict} iters={res32.Iterations} decoupled={res32.DecoupledSteps} rebases={res32.Rebases}");
                    Console.WriteLine($"    DD   verdict={resD.Verdict} iters={resD.Iterations} decoupled={resD.DecoupledSteps} rebases={resD.Rebases}");
                    Console.WriteLine($"    DD   trapMin={resD.Color.TrapMin:G10} trapErr={resD.Color.TrapMinError:E3} finalMagSq={resD.Color.FinalMagnitudeSquared:G10} ptErr={resD.Color.FinalPointError:E3}");
                    var apCtx = new MathContextCompat(prec);
                    var apScale = BigDecimalCompat.Pow2(scaleExp);
                    var apRe = cx.Add(BigDecimalCompat.FromDoubleExact((px - half) * offsetToDelta).Multiply(apScale, apCtx), apCtx);
                    var apIm = cy.Add(BigDecimalCompat.FromDoubleExact((half - py) * offsetToDelta).Multiply(apScale, apCtx), apCtx);
                    var (apIters, apTrap, apMag) = TruePixel(apRe, apIm, BigDecimalCompat.FromDoubleExact(kRe), BigDecimalCompat.FromDoubleExact(kIm),
                        p.PowerReal, p.PowerImag, iterations, p.LeafOrbitOffset, prec);
                    Console.WriteLine($"    AP   iters={apIters} trapMin={apTrap:G10} finalMagSq={apMag:G10}");
                }
            }
        }
        Console.WriteLine($"arbitration: {exact}/{checkedPixels} byte-exact vs arbitrary precision, worst={worst:0.00}");
        return mismatched == 0 && stats.Uncertified == 0 ? 0 : 1;
    }

    public static int KernelBench(string[] args)
    {
        var resolution = args.Length > 1 && int.TryParse(args[1], out var r) ? r : 480;
        var iterations = args.Length > 2 && int.TryParse(args[2], out var it) ? it : 300;
        var cx = BigDecimalCompat.Parse(CorruptionCertifiedCenterReal);
        var cy = BigDecimalCompat.Parse(CorruptionCertifiedCenterImag);
        var p = CorruptionParams();
        p.Iterations = iterations;
        var kRe = p.JuliaCXRaw * 1e-5;
        var kIm = p.JuliaCYRaw * 1e-5;

        Console.WriteLine($"KERNEL BENCH  res={resolution}²  iterations={iterations}");
        Console.WriteLine("Baking reference orbit once (shared by every zoom — it depends on the centre, not the zoom)…");
        var bakeStart = Environment.TickCount64;
        var baked = FractalAnimator.Core.Atoms.PowerJuliaBaker.Bake(cx, cy, kRe, kIm, p.PowerReal, p.PowerImag, iterations);
        var bakeMs = Environment.TickCount64 - bakeStart;
        Console.WriteLine($"  bake: {bakeMs} ms at {baked.Precision} digits (zoom-independent), {baked.Length} entries");
        Console.WriteLine();

        Console.WriteLine($"{"zoom",6} {"new ms",9} {"old ms",9} {"speedup",8}  {"uncert",7}  rung retirement (FP32/FF32/FP64/DD/QD)");
        foreach (var zooms in new[] { 0.0, 45.0, 60.0, 120.0, 200.0, 300.0 })
        {
            var rgb = new float[resolution * resolution * 3];
            GC.Collect(); GC.WaitForPendingFinalizers();
            var stats = FractalAnimator.Core.Kernel.CertifiedRenderer.Render(baked, zooms, resolution, resolution, iterations, rgb);

            // Old engine on the identical view. It is the thing being replaced, so it is the baseline.
            GC.Collect(); GC.WaitForPendingFinalizers();
            var oldRgb = new float[resolution * resolution * 3];
            var oldStart = Environment.TickCount64;
            DeepOrbitTrapEngine.Render(cx, cy, BigDecimalCompat.FromDouble(kRe), BigDecimalCompat.FromDouble(kIm),
                p.PowerReal, p.PowerImag, zooms, resolution, resolution, iterations,
                p.LeafOrbitOffset, p.ColorR, p.ColorG, p.ColorB, oldRgb);
            var oldMs = Environment.TickCount64 - oldStart;

            var dist = string.Join("/", stats.RetiredPerRung);
            var speedup = stats.TotalMilliseconds > 0 ? oldMs / stats.TotalMilliseconds : 0;
            Console.WriteLine($"{zooms,6:0} {stats.TotalMilliseconds,9:0} {oldMs,9:0} {speedup,7:0.0}x  {stats.Uncertified,7}  {dist}");
        }
        Console.WriteLine();
        Console.WriteLine("uncert = pixels no rung could certify. Any non-zero value is an unproven pixel and a defect.");
        return 0;
    }

    /// <summary>z^p for a real exponent, in arbitrary precision — the reference for the double-double
    /// complex power, taking the exponent as an already-exact value.</summary>
    static (BigDecimalCompat re, BigDecimalCompat im) RefComplexPow(
        BigDecimalCompat zr, BigDecimalCompat zi, BigDecimalCompat power, MathContextCompat ctx)
    {
        var two = new BigDecimalCompat(2, 0);
        var modSq = zr.Multiply(zr, ctx).Add(zi.Multiply(zi, ctx), ctx);
        var logR = modSq.Ln(ctx).Divide(two, ctx);
        var theta = BigDecimalCompat.Atan2(zi, zr, ctx);
        var mag = power.Multiply(logR, ctx).Exp(ctx);
        var (sin, cos) = BigDecimalCompat.SinCos(power.Multiply(theta, ctx), ctx);
        return (mag.Multiply(cos, ctx), mag.Multiply(sin, ctx));
    }

    // Validates DoubleDouble against BigDecimalCompat at 60 digits — double itself is far too coarse to
    // judge a ~32-digit type, so the arbitrary-precision implementation (already checked against
    // System.Math) is the reference. Every deep-render pixel goes through this arithmetic, so it has to
    // be right before it is wired in, not after a wrong image shows up. Usage: --ddcheck
    public static int DoubleDoubleCheck()
    {
        var ctx = new MathContextCompat(60);
        var failures = 0;
        var checks = 0;
        // A correct double-double carries ~32 digits; require 28 to leave headroom for the last ulps.
        const double tolerance = 1e-28;

        // The reference must be the EXACT binary value of the double, not its shortest round-trip text.
        // BigDecimalCompat.FromDouble("0.2") parses the decimal 0.2, while the double 0.2 is really
        // 0.200000000000000011102230246251565…; comparing against the former makes every result look
        // wrong at 1e-17 (i.e. exactly double precision) and hides whether dd is doing its job at all.
        static BigDecimalCompat Exact(double v)
        {
            var bits = BitConverter.DoubleToInt64Bits(v);
            var negative = bits < 0;
            var exponent = (int)((bits >> 52) & 0x7FF);
            var mantissa = bits & 0xFFFFFFFFFFFFFL;
            if (exponent == 0) exponent = 1; else mantissa |= 1L << 52;
            exponent -= 1075;
            var m = new BigDecimalCompat(negative ? -mantissa : mantissa, 0);
            return m.Multiply(BigDecimalCompat.Pow2(exponent), new MathContextCompat(400));
        }

        // `scaleOverride` judges the error against the quantity's natural scale instead of its own value.
        // sin(x) near a zero crossing, or a complex component that happens to be tiny, has a meaningless
        // relative error — what matters is the error next to the magnitude the renderer consumes.
        void Check(string name, DoubleDouble actual, BigDecimalCompat expected, BigDecimalCompat? scaleOverride = null)
        {
            checks++;
            var actualBig = Exact(actual.Hi).Add(Exact(actual.Lo), ctx);
            var diff = actualBig.Subtract(expected, ctx).Abs();
            var scale = (scaleOverride ?? expected).Abs();
            var rel = scale.UnscaledValue.IsZero ? diff.ToDouble() : diff.Divide(scale, ctx).ToDouble();
            if (rel > tolerance) { Console.WriteLine($"  FAIL {name,-34} rel={rel:E3}"); failures++; }
        }

        Console.WriteLine("DoubleDouble vs BigDecimalCompat (60-digit reference, exact binary inputs), tolerance 1e-28:");
        var one = new BigDecimalCompat(1, 0);
        double[] positives = [0.2, 1.0, 2.718281828459045, 0.001, 17.5, 1e-8, 1234.5678, 0.9999999];
        foreach (var v in positives)
        {
            Check($"Sqrt({v})", DoubleDouble.Sqrt(new DoubleDouble(v)), Exact(v).Sqrt(ctx));
            // ln(x) for x near 1 returns a tiny difference of two nearby quantities, so its *relative*
            // error is ill-conditioned by mathematics, not by this implementation (the absolute error is
            // still a full double-double). LogP1 is the API for that regime and is checked separately.
            var lnExpected = Exact(v).Ln(ctx);
            var lnScale = lnExpected.Abs().CompareTo(one) > 0 ? lnExpected.Abs() : one;
            Check($"Log({v})", DoubleDouble.Log(new DoubleDouble(v)), lnExpected, lnScale);
        }
        // Exp only over a range double can hold — e^1234 overflows, and comparing infinities proves nothing.
        double[] expArgs = [0.2, 1.0, 2.718281828459045, 0.001, 17.5, 1e-8, -30.0, 6.9, -0.5];
        foreach (var v in expArgs)
            Check($"Exp({v})", DoubleDouble.Exp(new DoubleDouble(v)), Exact(v).Exp(ctx));

        var oneScale = new BigDecimalCompat(1, 0); // sin/cos live on [-1,1]; that is their natural scale
        double[] angles = [0.1, 0.7, 1.5, 2.9, -0.3, -2.0, 3.14159, 6.0];
        foreach (var a in angles)
        {
            var (sin, cos) = DoubleDouble.SinCos(new DoubleDouble(a));
            var (bsin, bcos) = BigDecimalCompat.SinCos(Exact(a), ctx);
            Check($"Sin({a})", sin, bsin, oneScale);
            Check($"Cos({a})", cos, bcos, oneScale);
        }
        // Small arguments are the whole point of LogP1/ExpM1 — plain Log(1+x)/Exp(x)-1 lose digits there.
        double[] smalls = [1e-8, 1e-5, 1e-3, 0.05, -1e-7, -0.02, 0.2];
        var oneBig = new BigDecimalCompat(1, 0);
        foreach (var s in smalls)
        {
            Check($"LogP1({s})", DoubleDouble.LogP1(new DoubleDouble(s)), oneBig.Add(Exact(s), ctx).Ln(ctx));
            Check($"ExpM1({s})", DoubleDouble.ExpM1(new DoubleDouble(s)), Exact(s).Exp(ctx).Subtract(oneBig, ctx));
        }
        (double y, double x)[] quads = [(1, 1), (1, -1), (-1, -1), (-1, 1), (0.77781, -0.23656), (-0.001, 5.0), (5.0, 0.0), (-5.0, 0.0), (0.0, -3.0)];
        foreach (var (y, x) in quads)
            Check($"Atan2({y},{x})", DoubleDouble.Atan2(new DoubleDouble(y), new DoubleDouble(x)),
                BigDecimalCompat.Atan2(Exact(y), Exact(x), ctx));

        // Complex power at the actual deep-zoom reference values the engine will feed it.
        (double zr, double zi)[] powPts = [(1.6960609410226892, 2.0804990249369038), (0.104, 0.02), (0.3, -0.7), (-1.1, 0.2), (-0.85, -0.28)];
        foreach (var (zr, zi) in powPts)
        {
            var dd = DdComplex.Pow(new DdComplex(zr, zi), -2.23, 0.0);
            // Reference computed with the EXACT double exponent. DeepOrbitTrapEngine.ComplexPow converts
            // the power with FromDouble, which parses the decimal literal -2.23 rather than the double
            // nearest it; comparing against that measures a 1e-17 difference in the exponent instead of
            // the accuracy of this code.
            var (bre, bim) = RefComplexPow(Exact(zr), Exact(zi), Exact(-2.23), ctx);
            // Judge a component against the magnitude of the whole complex value, not against itself: a
            // component that happens to sit near zero has a meaningless relative error, and what the
            // renderer actually consumes is the complex number's accuracy.
            var magnitude = bre.Multiply(bre, ctx).Add(bim.Multiply(bim, ctx), ctx).Sqrt(ctx);
            checks += 2;
            foreach (var (label, got, want) in new[] { ("Re", dd.Re, bre), ("Im", dd.Im, bim) })
            {
                var gotBig = Exact(got.Hi).Add(Exact(got.Lo), ctx);
                var rel = gotBig.Subtract(want, ctx).Abs().Divide(magnitude, ctx).ToDouble();
                if (rel > tolerance) { Console.WriteLine($"  FAIL Pow.{label}({zr},{zi}) rel={rel:E3}"); failures++; }
            }
        }

        // The property the whole change rests on: after a cancellation that leaves 1e-3 of the operands,
        // double keeps ~13 digits while double-double must still keep ~29.
        var a1 = new DoubleDouble(1.0) + DoubleDouble.ScaleB(new DoubleDouble(1.0), -60);
        var a2 = new DoubleDouble(1.0);
        var ddCancel = a1 - a2;
        Console.WriteLine($"  cancellation probe: (1 + 2^-60) - 1 = {ddCancel.ToDouble():E6}  (exact 2^-60 = {Math.Pow(2, -60):E6})");
        Console.WriteLine($"  {checks} checks run");

        // The property that actually matters: catastrophic cancellation must leave ~29 digits, not ~13.
        var big1 = BigDecimalCompat.Parse("1.23456789012345678901234567890123");
        var big2 = BigDecimalCompat.Parse("1.23456789012345678901234000000000");
        var ddDiff = new DoubleDouble(1.23456789012345678901234567890123) - new DoubleDouble(1.23456789012345678901234000000000);
        Console.WriteLine($"  cancellation probe: dd gives {ddDiff.ToDouble():E6}, exact is {big1.Subtract(big2, ctx).ToDouble():E6}");

        Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }

    // Repairing a glitched pixel costs an arbitrary-precision reference orbit, and those builds are ~99%
    // of a deep frame's runtime — so the glitch threshold directly buys or wastes hours. This renders the
    // same view at several thresholds and reports, for each, the reference-build count, the wall time,
    // and how far the image drifts from the most conservative (current) setting, so the threshold can be
    // chosen on measured fidelity rather than on a guess. Usage: --glitchthresholdsweep [zooms] [size] [iterations]
    public static int GlitchThresholdSweep(string[] args)
    {
        var zooms = args.Length > 1 && double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ? z : 120.0;
        var size = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 480;
        var iterations = args.Length > 3 && int.TryParse(args[3], out var it) ? it : 300;
        var cx = BigDecimalCompat.Parse(CorruptionCertifiedCenterReal);
        var cy = BigDecimalCompat.Parse(CorruptionCertifiedCenterImag);
        var p = CorruptionParams();
        p.Iterations = iterations;
        var dir = Path.Combine(AppContext.BaseDirectory, "selftest_out");
        Directory.CreateDirectory(dir);

        // Baseline first: the conservative double-era threshold, which forces the full arbitrary-precision
        // repair. Everything after is compared against it, so "no repairs needed" has to be *shown* to
        // produce the same image rather than assumed from the glitch counter reading zero.
        float[]? baseline = null;
        foreach (var factor in new[] { 1.0e-6, 1.0e-20, 1.0e-32, 1.0e-40 })
        {
            DeepOrbitTrapEngine.GlitchFactor = factor;
            DeepOrbitTrapEngine.RepairReferenceBuilds = 0;
            var rgb = new float[size * size * 3];
            var start = Environment.TickCount64;
            DeepOrbitTrapEngine.Render(cx, cy,
                BigDecimalCompat.FromDouble(p.JuliaCXRaw * 1e-5), BigDecimalCompat.FromDouble(p.JuliaCYRaw * 1e-5),
                p.PowerReal, p.PowerImag, zooms, size, size, iterations, p.LeafOrbitOffset, p.ColorR, p.ColorG, p.ColorB, rgb);
            var elapsed = (Environment.TickCount64 - start) / 1000.0;
            baseline ??= (float[])rgb.Clone();
            double sum = 0, max = 0;
            for (var i = 0; i < rgb.Length; i++)
            {
                var d = Math.Abs(rgb[i] - baseline[i]) * 255.0;
                sum += d; max = Math.Max(max, d);
            }
            using var bmp = RgbToBitmap(rgb, size, size);
            bmp.Save(Path.Combine(dir, $"glitchfactor_{factor:E0}.png"), System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"factor={factor:E0}  refs={DeepOrbitTrapEngine.RepairReferenceBuilds,5}  {elapsed,7:0.0}s  meanDiffVsBaseline={sum / rgb.Length,9:0.00000}  maxDiff={max,6:0.0}  distinctColors={CountDistinctColors(bmp)}");
        }
        DeepOrbitTrapEngine.GlitchFactor = 1.0e-32;
        return 0;
    }

    // The general-power perturbation recurrence is only a 2nd-order Taylor truncation of z^p (exact
    // finite recurrences only exist for integer powers), so unlike the power-2 case it will NOT be
    // bit-exact against direct iteration — it needs to be checked empirically. Renders the same view via
    // direct double iteration (ShaderFractalImage — ground truth, only valid at shallow zoom) and via the
    // generalized DeepOrbitTrapEngine perturbation, then reports the RGB difference. Usage:
    // --deepcorruptioncompare [zooms=20] [size=400] [iterations=2500]
    public static int DeepCorruptionCompare(string[] args)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "selftest_out");
        Directory.CreateDirectory(dir);
        var zooms = args.Length > 1 && double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ? z : 20.0;
        var size = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 400;
        var iterations = args.Length > 3 && int.TryParse(args[3], out var it) ? it : 2500;

        var p = CorruptionParams();
        p.Iterations = iterations;
        var direct = new ShaderFractalImage(p, CorruptionDeepCenterX, CorruptionDeepCenterY, zooms, size, size);
        using var directBitmap = direct.GetImage();
        directBitmap.Save(Path.Combine(dir, "deepcompare_direct.png"), System.Drawing.Imaging.ImageFormat.Png);

        var reference = DeepOrbitTrapEngine.BuildReference(
            BigDecimalCompat.FromDouble(CorruptionDeepCenterX), BigDecimalCompat.FromDouble(CorruptionDeepCenterY),
            BigDecimalCompat.FromDouble(p.JuliaCXRaw * 1e-5), BigDecimalCompat.FromDouble(p.JuliaCYRaw * 1e-5),
            p.PowerReal, p.PowerImag, zooms, iterations, default);
        var minMagSq = double.MaxValue; var minAt = -1;
        for (var i = 0; i < reference.Length; i++)
        {
            var m = reference.MagnitudeSquared[i];
            if (m < minMagSq) { minMagSq = m; minAt = i; }
        }
        Console.WriteLine($"reference: length={reference.Length} (requested {iterations}), min|Z|={Math.Sqrt(minMagSq):E3} at n={minAt}");

        DeepOrbitTrapEngine.DebugLogging = true;
        var rgb = new float[size * size * 3];
        DeepOrbitTrapEngine.Render(BigDecimalCompat.FromDouble(CorruptionDeepCenterX), BigDecimalCompat.FromDouble(CorruptionDeepCenterY),
            BigDecimalCompat.FromDouble(p.JuliaCXRaw * 1e-5), BigDecimalCompat.FromDouble(p.JuliaCYRaw * 1e-5),
            p.PowerReal, p.PowerImag, zooms, size, size, iterations, p.LeafOrbitOffset, p.ColorR, p.ColorG, p.ColorB, rgb);
        using var perturbBitmap = RgbToBitmap(rgb, size, size);
        perturbBitmap.Save(Path.Combine(dir, "deepcompare_perturb.png"), System.Drawing.Imaging.ImageFormat.Png);

        double sumAbs = 0, maxAbs = 0;
        var n = size * size;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dPix = directBitmap.GetPixel(x, y);
                var idx = (y * size + x) * 3;
                var pr = Math.Clamp(rgb[idx + 0], 0f, 1f) * 255.0;
                var pg = Math.Clamp(rgb[idx + 1], 0f, 1f) * 255.0;
                var pb = Math.Clamp(rgb[idx + 2], 0f, 1f) * 255.0;
                var diff = (Math.Abs(dPix.R - pr) + Math.Abs(dPix.G - pg) + Math.Abs(dPix.B - pb)) / 3.0;
                sumAbs += diff;
                maxAbs = Math.Max(maxAbs, diff);
            }
        Console.WriteLine($"zooms={zooms} size={size} iterations={iterations}: meanAbsRgbDiff={sumAbs / n:0.000} (0-255 scale), maxAbsRgbDiff={maxAbs:0.0}");
        Console.WriteLine("Saved deepcompare_direct.png (ground truth) and deepcompare_perturb.png (2nd-order perturbation) to selftest_out/.");
        return 0;
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
        var (cre, cim) = DeepOrbitTrapEngine.FindDeepCenter(JuliaReal, JuliaImag, 2.0, 0.0,
            BigDecimalCompat.Zero, BigDecimalCompat.Zero, -2.0, 30, 3.0, 130, iterations, DeepLeafOffset);
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
        DeepOrbitTrapEngine.Render(cre, cim, JuliaReal, JuliaImag, 2.0, 0.0, zooms, size, size, iterations,
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


