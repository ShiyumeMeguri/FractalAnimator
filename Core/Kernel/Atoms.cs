namespace FractalAnimator.Core.Kernel;

public interface IFractalAtom<T> where T : struct, IPrecision<T>
{
    static abstract void Step(ReferenceOrbit<T> orbit, int n, ref Cx<T> delta, ref int scaleExp, out bool decoupled, double relativeError, out bool branchUndecidable);

    static abstract Cx<T> Apply(Cx<T> z);

    static abstract double BailoutSquared { get; }

    static abstract double DigitsPerIteration { get; }

    static abstract int AuxCount { get; }

    static abstract double DerivativeSupremum(ReferenceOrbit<T> orbit, double innerRadius);

    static abstract double TruncationCoefficient { get; }
}

public struct ColorState
{
    public double TrapLo;
    public double TrapHi;
    public double WindowKey;
    public double WindowLoA;
    public double WindowHiA;
    public double WindowLoB;
    public double WindowHiB;
    public double FinalMagnitudeSquared;
    public double FinalPointError;

    public readonly double TrapMin => 0.5 * (TrapLo + TrapHi);
    public readonly double TrapMinError => 0.5 * (TrapHi - TrapLo);

    public static ColorState Fresh(double seed) => new()
    {
        TrapLo = seed,
        TrapHi = seed,
        WindowKey = double.NaN,
        FinalMagnitudeSquared = 1.0,
        FinalPointError = 0.0,
    };
}

public interface IColoringAtom
{
    static abstract void Accumulate(ref ColorState state, double re, double im, double pointError);

    static abstract (double R, double G, double B) Resolve(in ColorState state, int iterations);

    static abstract bool IsCertified(in ColorState state, int iterations);
}
