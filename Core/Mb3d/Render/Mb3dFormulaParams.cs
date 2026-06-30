using System.Runtime.InteropServices;

namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>
/// The packed parameter buffer a Mandelbulb3D formula reads from (the <c>PVar</c> region). It is
/// produced exactly like <c>FillCustomVBufWithVars</c> (CustomFormulas.pas:393): a base pointer
/// <c>pConstPointer16</c> with the constant <c>0.5</c> at <c>[base-8]</c>, then each option packed at
/// successively <i>lower</i> byte offsets according to its <c>byOptionType</c>. Formula code reads
/// fields at fixed negative offsets (e.g. Amazing Box reads Scale at <c>[base-16]</c>), so the layout
/// here must match the Pascal packer byte for byte.
/// </summary>
public sealed class Mb3dFormulaParams
{
    // MemNeeded[type] = bytes consumed in the buffer (CustomFormulas.pas:400).
    private static readonly int[] MemNeeded =
        [8, 4, 4, 16, 8, 72, 36, 16, 40, 8, 0, 32, 64, 8, 16, 8, 8, 16, 36, 8, 8, 8, 16];

    private const double Pid180 = Math.PI / 180.0;

    private readonly byte[] _buffer;
    private readonly int _base;

    private Mb3dFormulaParams(byte[] buffer, int @base)
    {
        _buffer = buffer;
        _base = @base;
    }

    public double F64(int negativeOffset) =>
        MemoryMarshal.Read<double>(_buffer.AsSpan(_base + negativeOffset, 8));
    public float F32(int negativeOffset) =>
        MemoryMarshal.Read<float>(_buffer.AsSpan(_base + negativeOffset, 4));
    public int I32(int negativeOffset) =>
        MemoryMarshal.Read<int>(_buffer.AsSpan(_base + negativeOffset, 4));

    /// <summary>The raw buffer + base, for feeding the assembly self-test harness.</summary>
    public byte[] RawBuffer => _buffer;
    public int BaseOffset => _base;

    /// <summary>
    /// Builds the buffer from a formula's option types + values, mirroring
    /// <c>FillCustomVBufWithVars</c>. <paramref name="optionTypes"/> and <paramref name="optionValues"/>
    /// are the per-slot arrays parsed from the .m3a (<see cref="Mb3dFormulaSlot"/>).
    /// </summary>
    public static Mb3dFormulaParams Build(ReadOnlySpan<byte> optionTypes, ReadOnlySpan<double> optionValues, int optionCount)
    {
        // 256 bytes below the base covers the largest packing (Amazing Surf ~88 bytes); a generous
        // pad above keeps any positive-offset constant reads in-bounds.
        const int below = 256;
        const int above = 256;
        var buffer = new byte[below + above];
        int baseIndex = below;

        // p starts at base; Dec(p,2) writes a double 8 bytes lower, Dec(p,1) a single 4 bytes lower.
        int p = baseIndex;
        void Dec(int units) => p -= units * 4;
        void PutDouble(double v) => MemoryMarshal.Write(buffer.AsSpan(p, 8), in v);
        void PutSingle(double v) { var f = (float)v; MemoryMarshal.Write(buffer.AsSpan(p, 4), in f); }
        void PutInt(double v) { var i = (int)Math.Round(v); MemoryMarshal.Write(buffer.AsSpan(p, 4), in i); }

        Dec(2);
        PutDouble(0.5); // [base-8] = 0.5 constant (CustomFormulas.pas:408)

        int i = 0;
        int count = Math.Min(16, optionCount);
        while (i < count)
        {
            int type = optionTypes[i];
            switch (type)
            {
                case 0: // DOUBLE
                    Dec(2); PutDouble(optionValues[i]);
                    break;
                case 1: // SINGLE
                    Dec(1); PutSingle(optionValues[i]);
                    break;
                case 2: // INTEGER
                    Dec(1); PutInt(optionValues[i]);
                    break;
                case 3: // DOUBLEANGLE -> sin, cos
                    Dec(2); PutDouble(Math.Sin(optionValues[i] * Pid180));
                    Dec(2); PutDouble(Math.Cos(optionValues[i] * Pid180));
                    break;
                case 4: // SINGLEANGLE -> sin, cos
                    Dec(1); PutSingle(Math.Sin(optionValues[i] * Pid180));
                    Dec(1); PutSingle(Math.Cos(optionValues[i] * Pid180));
                    break;
                case 5: // 3DOUBLEANGLES -> 3x3 rotation matrix, 9 doubles
                {
                    var m = BuildRotMatrix(optionValues[i] * Pid180, optionValues[i + 1] * Pid180, optionValues[i + 2] * Pid180);
                    for (var j = 0; j < 9; j++) { Dec(2); PutDouble(m[j]); }
                    i += 2;
                    break;
                }
                case 6: // 3SINGLEANGLES -> 3x3 rotation matrix, 9 singles
                {
                    var m = BuildRotMatrix(optionValues[i] * Pid180, optionValues[i + 1] * Pid180, optionValues[i + 2] * Pid180);
                    for (var j = 0; j < 9; j++) { Dec(1); PutSingle(m[j]); }
                    i += 2;
                    break;
                }
                case 7: // BOXSCALE -> Scale/MinR^2, MinR^2  (uses previous option value as Scale)
                {
                    double minR = optionValues[i];
                    double prev = optionValues[i - 1];
                    double minR2 = Math.Max(1e-40, minR) * Math.Max(1e-40, minR);
                    // Pascal: Scale/Sqr(Max(1e-40,MinR)) then Sqr(Max(1e-40,MinR)).
                    double sqrMinR = Math.Max(1e-40, minR * minR);
                    Dec(2); PutDouble(prev / sqrMinR);
                    Dec(2); PutDouble(sqrMinR);
                    break;
                }
                case 8: // FOLDING -> R, 2R, -R, -2R (+ int-power function pointer, not needed for value reads)
                    Dec(2); PutDouble(optionValues[i]);
                    Dec(2); PutDouble(2 * optionValues[i]);
                    Dec(2); PutDouble(-optionValues[i]);
                    Dec(2); PutDouble(-2 * optionValues[i]);
                    Dec(1); // function pointer slot (left zero)
                    break;
                case 9: // DSQUARE
                    Dec(2); PutDouble(optionValues[i] * optionValues[i]);
                    break;
                case 10: // NOVARIABLE
                    break;
                case 11: // FOLDING16 -> R, R, -R, -R
                    Dec(2); PutDouble(optionValues[i]);
                    Dec(2); PutDouble(optionValues[i]);
                    Dec(2); PutDouble(-optionValues[i]);
                    Dec(2); PutDouble(-optionValues[i]);
                    break;
                case 13: // DRECIPRO -> 1/value
                    Dec(2); PutDouble(1.0 / MaxAbs(1e-40, optionValues[i]));
                    break;
                case 15: // DSQRRECI -> 1/value^2
                    Dec(2); PutDouble(1.0 / Math.Max(1e-40, optionValues[i] * optionValues[i]));
                    break;
                case 22: // DRECI2 -> value, 1/value
                    Dec(2); PutDouble(optionValues[i]);
                    Dec(2); PutDouble(1.0 / Math.Max(1e-40, optionValues[i]));
                    break;
                default:
                    throw new NotSupportedException($"byOptionType {type} not yet implemented in the parameter packer.");
            }
            i++;
        }

        return new Mb3dFormulaParams(buffer, baseIndex);
    }

    private static double MaxAbs(double floor, double v) => Math.Abs(v) < floor ? floor : v;

    /// <summary>
    /// MB3D <c>BuildRotMatrix</c> (Math3D.pas): a row-major 3x3 rotation from three Euler angles.
    /// Returned as a flat 9-element array (row-major).
    /// </summary>
    private static double[] BuildRotMatrix(double xa, double ya, double za)
    {
        double sinX = Math.Sin(xa), cosX = Math.Cos(xa);
        double sinY = Math.Sin(ya), cosY = Math.Cos(ya);
        double sinZ = Math.Sin(za), cosZ = Math.Cos(za);
        // Exact MB3D form (Math3D.pas:2486-2494), row-major.
        return
        [
            cosY * cosZ, -cosY * sinZ, sinY,
            sinX * sinY * cosZ + cosX * sinZ, cosX * cosZ - sinX * sinY * sinZ, -sinX * cosY,
            sinX * sinZ - cosX * sinY * cosZ, cosX * sinY * sinZ + sinX * cosZ, cosX * cosY,
        ];
    }
}
