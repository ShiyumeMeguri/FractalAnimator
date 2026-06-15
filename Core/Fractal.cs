using System.Numerics;
using System.Reflection;

namespace FractalAnimator.Core;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class FractalParamAttribute : Attribute
{
    public string Label { get; }
    public double DefaultValue { get; }
    public double Min { get; }
    public double Max { get; }

    public FractalParamAttribute(string label, double defaultValue, double min, double max)
    {
        Label = label;
        DefaultValue = defaultValue;
        Min = min;
        Max = max;
    }
}

public class FractalParamInfo
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public double Value { get; set; }
    public double DefaultValue { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public MemberInfo Member { get; set; } = null!;
}

/// <summary>
/// A runtime-compiled fractal formula. Implementations describe the same iteration in three
/// forms so the deep-zoom engine can pick the most precise one it needs:
///   * <see cref="ReferenceIterate"/>           — the reference orbit in fast double precision.
///   * <see cref="ReferenceIteratePrecise"/>     — the reference orbit in arbitrary precision (deep zoom).
///   * <see cref="PerturbIterate"/>              — one rescaled perturbation step for a single pixel.
///
/// The perturbation step works on a <i>rescaled</i> delta so the zoom depth is effectively
/// unlimited: the true per-pixel delta is <c>delta * 2^scaleExp</c>, where the engine tracks
/// <c>scaleExp</c> as an integer and passes <paramref name="scale"/> = 2^scaleExp. Linear terms of
/// the recurrence stay as-is, every higher-order term is multiplied by <paramref name="scale"/>.
/// Implementing <see cref="ISimdFractal"/> in addition unlocks the vectorized fast path.
/// </summary>
public interface IFractal
{
    string Name { get; }

    /// <summary>Reference-orbit step in double precision: z -&gt; f(z, c).</summary>
    void ReferenceIterate(ref double zReal, ref double zImag, double cReal, double cImag);

    /// <summary>Reference-orbit step in arbitrary precision, used while building deep references.</summary>
    void ReferenceIteratePrecise(ref BigDecimalCompat zReal, ref BigDecimalCompat zImag,
        BigDecimalCompat cReal, BigDecimalCompat cImag, MathContextCompat context);

    /// <summary>
    /// One rescaled perturbation step for a single pixel.
    /// <paramref name="deltaReal"/>/<paramref name="deltaImag"/> are the rescaled delta in/out,
    /// <paramref name="zReal"/>/<paramref name="zImag"/> the reference orbit value at this iteration,
    /// <paramref name="deltaCReal"/>/<paramref name="deltaCImag"/> the rescaled pixel offset, and
    /// <paramref name="scale"/> = 2^scaleExp (may be 0 at extreme depth, which correctly drops the
    /// higher-order terms).
    /// </summary>
    void PerturbIterate(ref double deltaReal, ref double deltaImag, double zReal, double zImag,
        double deltaCReal, double deltaCImag, double scale);

    IReadOnlyList<FractalParamInfo> GetParams();
    void SetParam(string name, double value);

    /// <summary>
    /// False for parameter-plane formulas (Mandelbrot-like: the orbit starts at 0 and <c>c</c> varies
    /// per pixel). True for dynamical-plane formulas (Julia-like: <c>c</c> is a constant and the
    /// starting point varies per pixel, so each pixel's delta is seeded with its own offset).
    /// </summary>
    bool DynamicalPlane => false;
}

/// <summary>
/// Optional capability: a vectorized perturbation step that advances
/// <see cref="System.Numerics.Vector{T}.Count"/> pixels at once. The engine batches pixels into
/// lanes and falls back to the scalar <see cref="IFractal.PerturbIterate"/> when a formula does not
/// implement this. Semantics mirror the scalar step exactly, lane for lane.
/// </summary>
public interface ISimdFractal
{
    void PerturbIterateSimd(ref Vector<double> deltaReal, ref Vector<double> deltaImag,
        Vector<double> zReal, Vector<double> zImag,
        Vector<double> deltaCReal, Vector<double> deltaCImag, Vector<double> scale);
}
