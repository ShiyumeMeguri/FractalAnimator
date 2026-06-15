using FractalAnimator.Core;

namespace FractalAnimator.Interp;

public abstract class Interpolator
{
    public abstract double Get(double x);
    public abstract double GetFirst();
    public abstract double GetLast();
    public virtual double GetDuration() => GetLast();
    public virtual bool IsOutside(double x) => x > GetLast();
}

public sealed record RenderParams(int Width, int Height, double Fps, int MergeFrames, double StartTime, double EndTime, string Ffmpeg, EncodingParam Param);

public sealed class KeyPoint : IComparable<KeyPoint>
{
    readonly Dictionary<string, double> _data;
    public KeyPoint(Dictionary<string, double> d) => _data = d;
    public double GetX() => _data["time"];
    public double GetY() => _data["image"];
    public double GetData(string k) => _data[k];
    public IEnumerable<KeyValuePair<string, double>> GetData() => _data;
    public int CompareTo(KeyPoint? o) => o is null ? 1 : GetX().CompareTo(o.GetX());
    public override string ToString() => $"[{_data["time"]}] " + string.Join(" | ", _data.Select(e => $"{e.Key}: {e.Value}"));
}

public sealed class LinearInterpolator : Interpolator
{
    readonly double[] _x, _y;
    public LinearInterpolator(double[] x, double[] y) { _x = x; _y = y; }
    public override double Get(double nx)
    {
        try { var i = Find(nx); var s = (_y[i + 1] - _y[i]) / (_x[i + 1] - _x[i]); return s * nx + (_y[i] - s * _x[i]); }
        catch { return _y[^1] + nx; }
    }
    public override double GetFirst() => _x[0];
    public override double GetLast() => _x[^1];
    int Find(double x) { var i = 0; while (i < _x.Length - 1 && x > _x[i + 1]) i++; return i; }
}

public sealed class QuadraticInterpolator : Interpolator
{
    readonly double[] _x, _y;
    public QuadraticInterpolator(double[] x, double[] y) { _x = x; _y = y; }
    public override double Get(double nx)
    {
        try
        {
            var i = Find(nx); if (i == 0) i = 1; if (i >= _x.Length - 1) i = _x.Length - 2;
            var (x0, y0, x1, y1, x2, y2) = (_x[i - 1], _y[i - 1], _x[i], _y[i], _x[i + 1], _y[i + 1]);
            return (nx - x1) * (nx - x2) / ((x0 - x1) * (x0 - x2)) * y0 +
                   (nx - x0) * (nx - x2) / ((x1 - x0) * (x1 - x2)) * y1 +
                   (nx - x0) * (nx - x1) / ((x2 - x0) * (x2 - x1)) * y2;
        }
        catch { return _y[^1] + nx; }
    }
    public override double GetFirst() => _x[0];
    public override double GetLast() => _x[^1];
    int Find(double x) { var i = 0; while (i < _x.Length - 1 && x > _x[i + 1]) i++; return i; }
}

public sealed class AccelInterpolator : Interpolator
{
    readonly SortedDictionary<double, (double Zooms, double St, double Et)> _p = new();
    public AccelInterpolator(double[][] d) { foreach (var a in d) _p[a[0]] = (a[1], a[2], a[3]); }
    public override double Get(double nx)
    {
        var a = Lower(nx) ?? _p.First(); var b = Higher(a.Key) ?? _p.Last();
        var dur = b.Key - a.Key;
        return dur > 0 ? Interp(a.Value.Zooms, b.Value.Zooms, (nx - a.Key) / dur, a.Value.St / dur, a.Value.Et / dur) : b.Value.Zooms + nx;
    }
    public override double GetFirst() => _p.First().Key;
    public override double GetLast() => _p.Last().Key;
    double Interp(double a, double b, double x, double t1, double t2)
    {
        if (t1 + t2 > 1) throw new IndexOutOfRangeException();
        var c = 1 - t1 - t2; var h = 2 / (c + 1); var s = b - a; var f = 1 - t2;
        if (x <= t1) { var r = x / t1; return a + x * (h * r) / 2 * s; }
        if (x <= 1 - t2) return a + (h * t1 / 2 + h * (x - t1)) * s;
        var r2 = (x - f) / t2; return a + (h * t1 / 2 + h * c + (h + h * (1 - r2)) * (x - f) / 2) * s;
    }
    KeyValuePair<double, (double Zooms, double St, double Et)>? Lower(double k) { KeyValuePair<double, (double Zooms, double St, double Et)>? r = null; foreach (var e in _p) { if (e.Key < k) r = e; else break; } return r; }
    KeyValuePair<double, (double Zooms, double St, double Et)>? Higher(double k) { foreach (var e in _p) if (e.Key > k) return e; return null; }
}

public sealed class SlopeAccelInterpolator : Interpolator
{
    readonly SortedDictionary<double, (double Zooms, double MaxT)> _p = new();
    public SlopeAccelInterpolator(double[][] d) { foreach (var a in d) _p[a[0]] = (a[1], a[2]); }
    public override double Get(double nx)
    {
        var a = Lower(nx) ?? _p.First(); var b = Higher(a.Key) ?? _p.Last();
        var dur = b.Key - a.Key;
        if (dur <= 0) return b.Value.Zooms + nx;
        double s1;
        if (a.Key == _p.First().Key) s1 = 0;
        else { var c = Lower(a.Key) ?? _p.First(); s1 = ((a.Value.Zooms - c.Value.Zooms) / (a.Key - c.Key) + (b.Value.Zooms - a.Value.Zooms) / (b.Key - a.Key)) / 2; }
        double s2;
        if (b.Key == _p.Last().Key) s2 = 0;
        else { var c = Higher(b.Key) ?? _p.Last(); s2 = ((c.Value.Zooms - b.Value.Zooms) / (c.Key - b.Key) + (b.Value.Zooms - a.Value.Zooms) / (b.Key - a.Key)) / 2; }
        double ia, ib;
        var len = b.Key - a.Key;
        if (len > a.Value.MaxT) { ia = a.Value.MaxT / len / 2; ib = ia; }
        else { ia = 0.5; ib = 0.5; }
        return Interpolate(a.Key, b.Key, a.Value.Zooms, b.Value.Zooms, (nx - a.Key) / dur, s1, s2, ia, ib);
    }
    public override double GetFirst() => _p.First().Key;
    public override double GetLast() => _p.Last().Key;
    double Interpolate(double x1, double x2, double y1, double y2, double x, double k1, double k2, double a, double b)
    {
        if (a + b > 1) throw new ArgumentException("a+b>1");
        var d = x2 - x1; var y1o = y1; y1 /= d; y2 /= d; var s = y2 - y1;
        var yt = (2 * s - a * k1 - b * k2) / (2 - a - b);
        var rev = (a * k1 / a >= 0) ^ (yt * a / a >= 0);
        var integral = y1o;
        if (rev)
        {
            var p1 = s / k1; var t = Math.Min(x, p1); integral += ((-k1 * (t * t)) / (2 * p1) + k1 * t) * d;
            var bot = s / 4; t = x; integral += bot * t * d;
            if (k2 == 0) integral += bot * t * d;
            else { var p2 = s / (k2 * 2); t = Math.Max(0, Math.Min(x - 1 + p2, 1)); integral += d * t * k2 * t / (2 * p2); }
        }
        else
        {
            var t = Math.Min(x, a); integral += (((yt - k1) * (t * t)) / (2 * a) + k1 * t) * d;
            t = Math.Max(0, Math.Min(x - a, 1 - a - b)); integral += yt * t * d;
            t = Math.Max(0, Math.Min(x - 1 + b, 1)); integral += -d * (t * ((t - 2 * b) * yt - k2 * t) / (2 * b));
        }
        return integral;
    }
    KeyValuePair<double, (double Zooms, double MaxT)>? Lower(double k) { KeyValuePair<double, (double Zooms, double MaxT)>? r = null; foreach (var e in _p) { if (e.Key < k) r = e; else break; } return r; }
    KeyValuePair<double, (double Zooms, double MaxT)>? Higher(double k) { foreach (var e in _p) if (e.Key > k) return e; return null; }
}
