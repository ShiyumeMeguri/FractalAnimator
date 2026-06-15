using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace FractalAnimator.Core;

/// <summary>
/// Compiles fractal formulas from C# source text at runtime (Roslyn) and instantiates them as
/// <see cref="IFractal"/>. The built-in presets double as a tutorial: each one is a complete,
/// SIMD-accelerated implementation the user can edit live in the formula editor and recompile.
/// </summary>
public static class FractalCompiler
{
    static readonly string[] BuiltInSources =
    [
        // -------- Mandelbrot: z -> z^2 + c --------
        """
        using System;
        using System.Collections.Generic;
        using System.Numerics;
        using FractalAnimator.Core;

        // The classic Mandelbrot set. A formula must describe its iteration three ways so the deep-zoom
        // engine can stay precise at any depth, plus an optional SIMD path for the fast lane.
        public sealed class Mandelbrot : IFractal, ISimdFractal
        {
            public string Name => "Mandelbrot  z² + c";

            // Reference orbit in fast double precision.
            public void ReferenceIterate(ref double zReal, ref double zImag, double cReal, double cImag)
            {
                var t = zReal * zReal - zImag * zImag + cReal;
                zImag = 2.0 * zReal * zImag + cImag;
                zReal = t;
            }

            // Reference orbit in arbitrary precision (used once per keyframe, only when zoomed deep).
            public void ReferenceIteratePrecise(ref BigDecimalCompat zReal, ref BigDecimalCompat zImag,
                BigDecimalCompat cReal, BigDecimalCompat cImag, MathContextCompat context)
            {
                var t = zReal.Pow(2, context).Subtract(zImag.Pow(2, context), context).Add(cReal, context);
                zImag = zReal.Multiply(zImag, context).Multiply(new(2, 0), context).Add(cImag, context);
                zReal = t;
            }

            // One rescaled perturbation step for a single pixel. Linear term as-is, quadratic term * scale.
            public void PerturbIterate(ref double deltaReal, ref double deltaImag, double zReal, double zImag,
                double deltaCReal, double deltaCImag, double scale)
            {
                var newReal = 2.0 * (zReal * deltaReal - zImag * deltaImag)
                            + scale * (deltaReal * deltaReal - deltaImag * deltaImag) + deltaCReal;
                var newImag = 2.0 * (zReal * deltaImag + zImag * deltaReal)
                            + scale * (2.0 * deltaReal * deltaImag) + deltaCImag;
                deltaReal = newReal;
                deltaImag = newImag;
            }

            // The same step for Vector<double>.Count pixels at once.
            public void PerturbIterateSimd(ref Vector<double> deltaReal, ref Vector<double> deltaImag,
                Vector<double> zReal, Vector<double> zImag, Vector<double> deltaCReal, Vector<double> deltaCImag,
                Vector<double> scale)
            {
                var two = new Vector<double>(2.0);
                var newReal = two * (zReal * deltaReal - zImag * deltaImag)
                            + scale * (deltaReal * deltaReal - deltaImag * deltaImag) + deltaCReal;
                var newImag = two * (zReal * deltaImag + zImag * deltaReal)
                            + scale * (two * deltaReal * deltaImag) + deltaCImag;
                deltaReal = newReal;
                deltaImag = newImag;
            }

            public IReadOnlyList<FractalParamInfo> GetParams() => [];
            public void SetParam(string name, double value) { }
        }
        """,

        // -------- Burning Ship: z -> (|Re z| + i|Im z|)^2 + c --------
        """
        using System;
        using System.Collections.Generic;
        using System.Numerics;
        using FractalAnimator.Core;

        public sealed class BurningShip : IFractal, ISimdFractal
        {
            public string Name => "Burning Ship";

            public void ReferenceIterate(ref double zReal, ref double zImag, double cReal, double cImag)
            {
                var ar = Math.Abs(zReal);
                var ai = Math.Abs(zImag);
                var t = ar * ar - ai * ai + cReal;
                zImag = 2.0 * ar * ai + cImag;
                zReal = t;
            }

            public void ReferenceIteratePrecise(ref BigDecimalCompat zReal, ref BigDecimalCompat zImag,
                BigDecimalCompat cReal, BigDecimalCompat cImag, MathContextCompat context)
            {
                var ar = Abs(zReal);
                var ai = Abs(zImag);
                var t = ar.Pow(2, context).Subtract(ai.Pow(2, context), context).Add(cReal, context);
                zImag = ar.Multiply(ai, context).Multiply(new(2, 0), context).Add(cImag, context);
                zReal = t;
            }

            public void PerturbIterate(ref double deltaReal, ref double deltaImag, double zReal, double zImag,
                double deltaCReal, double deltaCImag, double scale)
            {
                var a = DiffAbs(zReal, deltaReal, scale);
                var b = DiffAbs(zImag, deltaImag, scale);
                var absZr = Math.Abs(zReal);
                var absZi = Math.Abs(zImag);
                var newReal = 2.0 * absZr * a + scale * a * a - 2.0 * absZi * b - scale * b * b + deltaCReal;
                var newImag = 2.0 * (absZr * b + absZi * a + scale * a * b) + deltaCImag;
                deltaReal = newReal;
                deltaImag = newImag;
            }

            public void PerturbIterateSimd(ref Vector<double> deltaReal, ref Vector<double> deltaImag,
                Vector<double> zReal, Vector<double> zImag, Vector<double> deltaCReal, Vector<double> deltaCImag,
                Vector<double> scale)
            {
                var two = new Vector<double>(2.0);
                var a = DiffAbsSimd(zReal, deltaReal, scale);
                var b = DiffAbsSimd(zImag, deltaImag, scale);
                var absZr = Vector.Abs(zReal);
                var absZi = Vector.Abs(zImag);
                var newReal = two * absZr * a + scale * a * a - two * absZi * b - scale * b * b + deltaCReal;
                var newImag = two * (absZr * b + absZi * a + scale * a * b) + deltaCImag;
                deltaReal = newReal;
                deltaImag = newImag;
            }

            // Rescaled diffabs: (|reference + scale*delta| - |reference|) / scale, stable as scale -> 0.
            static double DiffAbs(double reference, double delta, double scale)
            {
                var full = reference + scale * delta;
                if (reference >= 0.0)
                    return full >= 0.0 ? delta : (-2.0 * reference - scale * delta) / scale;
                return full < 0.0 ? -delta : (2.0 * reference + scale * delta) / scale;
            }

            static Vector<double> DiffAbsSimd(Vector<double> reference, Vector<double> delta, Vector<double> scale)
            {
                var zero = Vector<double>.Zero;
                var two = new Vector<double>(2.0);
                var inverse = Vector.ConditionalSelect(Vector.Equals(scale, zero), zero, Vector<double>.One / scale);
                var full = reference + scale * delta;
                var refNonNegative = Vector.GreaterThanOrEqual(reference, zero);
                var fullNonNegative = Vector.GreaterThanOrEqual(full, zero);
                var positiveMixed = (-two * reference - scale * delta) * inverse;
                var positiveResult = Vector.ConditionalSelect(fullNonNegative, delta, positiveMixed);
                var negativeMixed = (two * reference + scale * delta) * inverse;
                var negativeResult = Vector.ConditionalSelect(fullNonNegative, negativeMixed, -delta);
                return Vector.ConditionalSelect(refNonNegative, positiveResult, negativeResult);
            }

            static BigDecimalCompat Abs(BigDecimalCompat v) =>
                v.CompareTo(BigDecimalCompat.Zero) < 0 ? v.Multiply(new(-1, 0), new(100)) : v;

            public IReadOnlyList<FractalParamInfo> GetParams() => [];
            public void SetParam(string name, double value) { }
        }
        """,

        // -------- Tricorn / Mandelbar: z -> conj(z)^2 + c --------
        """
        using System;
        using System.Collections.Generic;
        using System.Numerics;
        using FractalAnimator.Core;

        public sealed class Tricorn : IFractal, ISimdFractal
        {
            public string Name => "Tricorn  (Mandelbar)";

            public void ReferenceIterate(ref double zReal, ref double zImag, double cReal, double cImag)
            {
                var t = zReal * zReal - zImag * zImag + cReal;
                zImag = -2.0 * zReal * zImag + cImag;
                zReal = t;
            }

            public void ReferenceIteratePrecise(ref BigDecimalCompat zReal, ref BigDecimalCompat zImag,
                BigDecimalCompat cReal, BigDecimalCompat cImag, MathContextCompat context)
            {
                var t = zReal.Pow(2, context).Subtract(zImag.Pow(2, context), context).Add(cReal, context);
                zImag = zReal.Multiply(zImag, context).Multiply(new(-2, 0), context).Add(cImag, context);
                zReal = t;
            }

            public void PerturbIterate(ref double deltaReal, ref double deltaImag, double zReal, double zImag,
                double deltaCReal, double deltaCImag, double scale)
            {
                var newReal = 2.0 * (zReal * deltaReal - zImag * deltaImag)
                            + scale * (deltaReal * deltaReal - deltaImag * deltaImag) + deltaCReal;
                var newImag = -2.0 * (zReal * deltaImag + zImag * deltaReal)
                            - scale * (2.0 * deltaReal * deltaImag) + deltaCImag;
                deltaReal = newReal;
                deltaImag = newImag;
            }

            public void PerturbIterateSimd(ref Vector<double> deltaReal, ref Vector<double> deltaImag,
                Vector<double> zReal, Vector<double> zImag, Vector<double> deltaCReal, Vector<double> deltaCImag,
                Vector<double> scale)
            {
                var two = new Vector<double>(2.0);
                var newReal = two * (zReal * deltaReal - zImag * deltaImag)
                            + scale * (deltaReal * deltaReal - deltaImag * deltaImag) + deltaCReal;
                var newImag = -two * (zReal * deltaImag + zImag * deltaReal)
                            - scale * (two * deltaReal * deltaImag) + deltaCImag;
                deltaReal = newReal;
                deltaImag = newImag;
            }

            public IReadOnlyList<FractalParamInfo> GetParams() => [];
            public void SetParam(string name, double value) { }
        }
        """,

        // -------- Multibrot (cubic): z -> z^3 + c --------
        """
        using System;
        using System.Collections.Generic;
        using System.Numerics;
        using FractalAnimator.Core;

        public sealed class CubicMultibrot : IFractal, ISimdFractal
        {
            public string Name => "Multibrot  z³ + c";

            public void ReferenceIterate(ref double zReal, ref double zImag, double cReal, double cImag)
            {
                var zr2 = zReal * zReal;
                var zi2 = zImag * zImag;
                var newReal = zReal * (zr2 - 3.0 * zi2) + cReal;
                var newImag = zImag * (3.0 * zr2 - zi2) + cImag;
                zReal = newReal;
                zImag = newImag;
            }

            public void ReferenceIteratePrecise(ref BigDecimalCompat zReal, ref BigDecimalCompat zImag,
                BigDecimalCompat cReal, BigDecimalCompat cImag, MathContextCompat context)
            {
                var zr2 = zReal.Pow(2, context);
                var zi2 = zImag.Pow(2, context);
                var newReal = zReal.Multiply(zr2.Subtract(zi2.Multiply(new(3, 0), context), context), context).Add(cReal, context);
                var newImag = zImag.Multiply(zr2.Multiply(new(3, 0), context).Subtract(zi2, context), context).Add(cImag, context);
                zReal = newReal;
                zImag = newImag;
            }

            public void PerturbIterate(ref double deltaReal, ref double deltaImag, double zReal, double zImag,
                double deltaCReal, double deltaCImag, double scale)
            {
                var z2r = zReal * zReal - zImag * zImag;
                var z2i = 2.0 * zReal * zImag;
                var d2r = deltaReal * deltaReal - deltaImag * deltaImag;
                var d2i = 2.0 * deltaReal * deltaImag;
                var linR = 3.0 * (z2r * deltaReal - z2i * deltaImag);
                var linI = 3.0 * (z2r * deltaImag + z2i * deltaReal);
                var quadR = 3.0 * (zReal * d2r - zImag * d2i);
                var quadI = 3.0 * (zReal * d2i + zImag * d2r);
                var cubR = deltaReal * d2r - deltaImag * d2i;
                var cubI = deltaReal * d2i + deltaImag * d2r;
                var scale2 = scale * scale;
                deltaReal = linR + scale * quadR + scale2 * cubR + deltaCReal;
                deltaImag = linI + scale * quadI + scale2 * cubI + deltaCImag;
            }

            public void PerturbIterateSimd(ref Vector<double> deltaReal, ref Vector<double> deltaImag,
                Vector<double> zReal, Vector<double> zImag, Vector<double> deltaCReal, Vector<double> deltaCImag,
                Vector<double> scale)
            {
                var two = new Vector<double>(2.0);
                var three = new Vector<double>(3.0);
                var z2r = zReal * zReal - zImag * zImag;
                var z2i = two * zReal * zImag;
                var d2r = deltaReal * deltaReal - deltaImag * deltaImag;
                var d2i = two * deltaReal * deltaImag;
                var linR = three * (z2r * deltaReal - z2i * deltaImag);
                var linI = three * (z2r * deltaImag + z2i * deltaReal);
                var quadR = three * (zReal * d2r - zImag * d2i);
                var quadI = three * (zReal * d2i + zImag * d2r);
                var cubR = deltaReal * d2r - deltaImag * d2i;
                var cubI = deltaReal * d2i + deltaImag * d2r;
                var scale2 = scale * scale;
                deltaReal = linR + scale * quadR + scale2 * cubR + deltaCReal;
                deltaImag = linI + scale * quadI + scale2 * cubI + deltaCImag;
            }

            public IReadOnlyList<FractalParamInfo> GetParams() => [];
            public void SetParam(string name, double value) { }
        }
        """,

        // -------- Julia: z -> z^2 + c, with c held constant (dynamical plane) --------
        """
        using System;
        using System.Collections.Generic;
        using System.Numerics;
        using System.Reflection;
        using FractalAnimator.Core;

        public sealed class Julia : IFractal, ISimdFractal
        {
            double _juliaReal = -0.7269;
            double _juliaImag = 0.1889;

            public string Name => "Julia  z² + c (constant c)";

            // Julia explores the dynamical plane: c is fixed, the starting point varies per pixel.
            public bool DynamicalPlane => true;

            public void ReferenceIterate(ref double zReal, ref double zImag, double cReal, double cImag)
            {
                var t = zReal * zReal - zImag * zImag + _juliaReal;
                zImag = 2.0 * zReal * zImag + _juliaImag;
                zReal = t;
            }

            public void ReferenceIteratePrecise(ref BigDecimalCompat zReal, ref BigDecimalCompat zImag,
                BigDecimalCompat cReal, BigDecimalCompat cImag, MathContextCompat context)
            {
                var jr = BigDecimalCompat.FromDouble(_juliaReal);
                var ji = BigDecimalCompat.FromDouble(_juliaImag);
                var t = zReal.Pow(2, context).Subtract(zImag.Pow(2, context), context).Add(jr, context);
                zImag = zReal.Multiply(zImag, context).Multiply(new(2, 0), context).Add(ji, context);
                zReal = t;
            }

            // No deltaC term: in the dynamical plane the per-pixel offset only seeds the initial delta.
            public void PerturbIterate(ref double deltaReal, ref double deltaImag, double zReal, double zImag,
                double deltaCReal, double deltaCImag, double scale)
            {
                var newReal = 2.0 * (zReal * deltaReal - zImag * deltaImag)
                            + scale * (deltaReal * deltaReal - deltaImag * deltaImag);
                var newImag = 2.0 * (zReal * deltaImag + zImag * deltaReal)
                            + scale * (2.0 * deltaReal * deltaImag);
                deltaReal = newReal;
                deltaImag = newImag;
            }

            public void PerturbIterateSimd(ref Vector<double> deltaReal, ref Vector<double> deltaImag,
                Vector<double> zReal, Vector<double> zImag, Vector<double> deltaCReal, Vector<double> deltaCImag,
                Vector<double> scale)
            {
                var two = new Vector<double>(2.0);
                var newReal = two * (zReal * deltaReal - zImag * deltaImag)
                            + scale * (deltaReal * deltaReal - deltaImag * deltaImag);
                var newImag = two * (zReal * deltaImag + zImag * deltaReal)
                            + scale * (two * deltaReal * deltaImag);
                deltaReal = newReal;
                deltaImag = newImag;
            }

            // Expose the two constants as live sliders in the formula panel.
            public IReadOnlyList<FractalParamInfo> GetParams() =>
            [
                new FractalParamInfo { Name = "_juliaReal", Label = "Julia c (real)", Value = _juliaReal,
                    DefaultValue = -0.7269, Min = -2.0, Max = 2.0,
                    Member = typeof(Julia).GetField("_juliaReal", BindingFlags.NonPublic | BindingFlags.Instance)! },
                new FractalParamInfo { Name = "_juliaImag", Label = "Julia c (imag)", Value = _juliaImag,
                    DefaultValue = 0.1889, Min = -2.0, Max = 2.0,
                    Member = typeof(Julia).GetField("_juliaImag", BindingFlags.NonPublic | BindingFlags.Instance)! },
            ];

            public void SetParam(string name, double value)
            {
                if (name == "_juliaReal") _juliaReal = value;
                else if (name == "_juliaImag") _juliaImag = value;
            }
        }
        """,

        // -------- Custom template: a fully commented starting point to fork your own formula --------
        """
        using System;
        using System.Collections.Generic;
        using System.Numerics;
        using FractalAnimator.Core;

        // ===========================================================================================
        //  YOUR OWN FRACTAL
        //  Edit the iteration below and press Compile. To stay correct at extreme zoom, describe the
        //  same recurrence in all four methods. The engine runs PerturbIterateSimd on every CPU core
        //  with AVX vectors, so this compiles to genuinely fast code.
        //
        //  Rescaled perturbation cheat-sheet (true delta = delta * 2^scaleExp, passed as 'scale'):
        //    * keep every term that is LINEAR in delta exactly as written;
        //    * multiply every higher-order term (delta squared, cubed, ...) by 'scale';
        //    * 'scale' may be 0 at extreme depth, which correctly drops those tiny terms.
        // ===========================================================================================
        public sealed class CustomFractal : IFractal, ISimdFractal
        {
            public string Name => "My Fractal";

            public void ReferenceIterate(ref double zReal, ref double zImag, double cReal, double cImag)
            {
                var t = zReal * zReal - zImag * zImag + cReal;
                zImag = 2.0 * zReal * zImag + cImag;
                zReal = t;
            }

            public void ReferenceIteratePrecise(ref BigDecimalCompat zReal, ref BigDecimalCompat zImag,
                BigDecimalCompat cReal, BigDecimalCompat cImag, MathContextCompat context)
            {
                var t = zReal.Pow(2, context).Subtract(zImag.Pow(2, context), context).Add(cReal, context);
                zImag = zReal.Multiply(zImag, context).Multiply(new(2, 0), context).Add(cImag, context);
                zReal = t;
            }

            public void PerturbIterate(ref double deltaReal, ref double deltaImag, double zReal, double zImag,
                double deltaCReal, double deltaCImag, double scale)
            {
                var newReal = 2.0 * (zReal * deltaReal - zImag * deltaImag)
                            + scale * (deltaReal * deltaReal - deltaImag * deltaImag) + deltaCReal;
                var newImag = 2.0 * (zReal * deltaImag + zImag * deltaReal)
                            + scale * (2.0 * deltaReal * deltaImag) + deltaCImag;
                deltaReal = newReal;
                deltaImag = newImag;
            }

            public void PerturbIterateSimd(ref Vector<double> deltaReal, ref Vector<double> deltaImag,
                Vector<double> zReal, Vector<double> zImag, Vector<double> deltaCReal, Vector<double> deltaCImag,
                Vector<double> scale)
            {
                var two = new Vector<double>(2.0);
                var newReal = two * (zReal * deltaReal - zImag * deltaImag)
                            + scale * (deltaReal * deltaReal - deltaImag * deltaImag) + deltaCReal;
                var newImag = two * (zReal * deltaImag + zImag * deltaReal)
                            + scale * (two * deltaReal * deltaImag) + deltaCImag;
                deltaReal = newReal;
                deltaImag = newImag;
            }

            public IReadOnlyList<FractalParamInfo> GetParams() => [];
            public void SetParam(string name, double value) { }
        }
        """,
    ];

    static readonly CSharpCompilationOptions CompileOpts = new(
        OutputKind.DynamicallyLinkedLibrary,
        optimizationLevel: OptimizationLevel.Release,
        allowUnsafe: true);

    static readonly Lazy<PortableExecutableReference[]> _references = new(LoadReferences);

    // Reference every framework assembly via the trusted-platform-assemblies list so user formulas can
    // use the full BCL (System.Numerics vectors, Math, reflection, ...) plus this app's own types.
    static PortableExecutableReference[] LoadReferences()
    {
        var byFileName = new Dictionary<string, PortableExecutableReference>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
        {
            foreach (var path in trusted.Split(Path.PathSeparator))
            {
                if (path.Length == 0 || !File.Exists(path)) continue;
                try { byFileName[Path.GetFileName(path)] = MetadataReference.CreateFromFile(path); }
                catch { }
            }
        }
        var self = typeof(IFractal).Assembly.Location;
        if (File.Exists(self))
            byFileName[Path.GetFileName(self)] = MetadataReference.CreateFromFile(self);
        return byFileName.Values.ToArray();
    }

    public static IFractal? Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));
        var assemblyName = $"Fractal_{Guid.NewGuid():N}";
        var compilation = CSharpCompilation.Create(assemblyName, [tree], _references.Value, CompileOpts);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            var builder = new StringBuilder();
            foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                builder.AppendLine($"{diagnostic.Id}: {diagnostic.GetMessage()}");
            throw new InvalidOperationException($"Compilation failed:\n{builder}");
        }
        stream.Position = 0;
        var assembly = Assembly.Load(stream.ToArray());
        var type = assembly.GetExportedTypes()
            .FirstOrDefault(t => typeof(IFractal).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
        return type != null ? (IFractal)Activator.CreateInstance(type)! : null;
    }

    public static IFractal CreateBuiltIn(int index)
    {
        var source = BuiltInSources[index];
        return Compile(source) ?? throw new InvalidOperationException("Built-in fractal failed to compile");
    }

    public static IReadOnlyList<string> BuiltInNames { get; } =
    [
        "Mandelbrot  (z² + c)",
        "Burning Ship",
        "Tricorn / Mandelbar",
        "Multibrot  (z³ + c)",
        "Julia  (z² + c)",
        "Custom template…",
    ];

    public static string GetBuiltInSource(int index) =>
        index >= 0 && index < BuiltInSources.Length ? BuiltInSources[index] : "";
}
