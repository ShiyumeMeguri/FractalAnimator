namespace FractalAnimator.Core.Kernel;

public sealed class ReferenceOrbit<T> where T : struct, IPrecision<T>
{
    public required Cx<T>[] Z { get; init; }
    public required Cx<T>[][] Aux { get; init; }
    public required double[] MagnitudeSquared { get; init; }
    public required double[] ExpansionMagnitude { get; init; }
    public required int Length { get; init; }

    public required double[] Payload { get; init; }

    public required double TableRelativeUlp { get; init; }

    public required NearestPointIndex Nearest { get; init; }
}

public sealed class NearestPointIndex
{
    readonly int[] _cell;
    readonly int _resolution;
    readonly double _minX, _minY, _invSpanX, _invSpanY;

    NearestPointIndex(int[] cell, int resolution, double minX, double minY, double invSpanX, double invSpanY)
    {
        _cell = cell; _resolution = resolution;
        _minX = minX; _minY = minY; _invSpanX = invSpanX; _invSpanY = invSpanY;
    }

    public static NearestPointIndex Build(double[] real, double[] imag, int length, int resolution = 128)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        for (var i = 0; i < length; i++)
        {
            var r = real[i]; var im = imag[i];
            if (!double.IsFinite(r) || !double.IsFinite(im) || r * r + im * im > 4096.0) continue;
            if (r < minX) minX = r; if (r > maxX) maxX = r;
            if (im < minY) minY = im; if (im > maxY) maxY = im;
        }
        if (minX > maxX) { minX = -1; maxX = 1; minY = -1; maxY = 1; }
        var spanX = Math.Max(maxX - minX, 1e-300);
        var spanY = Math.Max(maxY - minY, 1e-300);

        var cell = new int[resolution * resolution];
        Array.Fill(cell, -1);
        var invSpanX = resolution / (spanX * 1.0000001);
        var invSpanY = resolution / (spanY * 1.0000001);
        for (var i = length - 1; i >= 0; i--)
        {
            var r = real[i]; var im = imag[i];
            if (!double.IsFinite(r) || !double.IsFinite(im) || r * r + im * im > 4096.0) continue;
            var gx = (int)((r - minX) * invSpanX);
            var gy = (int)((im - minY) * invSpanY);
            if ((uint)gx >= (uint)resolution || (uint)gy >= (uint)resolution) continue;
            cell[gy * resolution + gx] = i;
        }
        return new NearestPointIndex(cell, resolution, minX, minY, invSpanX, invSpanY);
    }

    public int Query(double real, double imag)
    {
        var gx = (int)((real - _minX) * _invSpanX);
        var gy = (int)((imag - _minY) * _invSpanY);
        if ((uint)gx >= (uint)_resolution || (uint)gy >= (uint)_resolution) return -1;
        var best = -1;
        for (var dy = -1; dy <= 1; dy++)
        {
            var y = gy + dy;
            if ((uint)y >= (uint)_resolution) continue;
            for (var dx = -1; dx <= 1; dx++)
            {
                var x = gx + dx;
                if ((uint)x >= (uint)_resolution) continue;
                var candidate = _cell[y * _resolution + x];
                if (candidate >= 0 && (best < 0 || candidate < best)) best = candidate;
            }
        }
        return best;
    }
}

public static class OrbitPrecision
{
    public const double DefaultDigitsPerIteration = 0.204;

    public const int TargetDigits = 63;

    public const int GuardDigits = 12;

    public static int For(int iterations, double digitsPerIteration = DefaultDigitsPerIteration) =>
        TargetDigits + (int)Math.Ceiling(iterations * digitsPerIteration) + GuardDigits;
}
