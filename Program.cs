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
            Console.WriteLine("[ok]   UI constructs (MainForm + formula studio)");
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
