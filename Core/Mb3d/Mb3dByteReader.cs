using System.Buffers.Binary;

namespace FractalAnimator.Core.Mb3d;

/// <summary>
/// A forward-only little-endian cursor over a Mandelbulb3D parameter blob. The MB3D files
/// (.m3a / .m3i / .m3p binary records) are produced by Delphi <c>BlockWrite</c> of <i>packed</i>
/// records on x86, so the on-disk layout is simply each field's raw bytes, little-endian, in
/// declaration order. Every reader method here corresponds 1:1 to one Pascal field, which keeps
/// the port auditable: the sequence of calls mirrors the record definition line for line.
///
/// Source of the on-disk format: Animation.pas SpeedButton9Click (save) / SpeedButton11Click
/// (load), TypeDefinitions.pas record definitions, Math3D.pas float helpers.
/// </summary>
public ref struct Mb3dByteReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public Mb3dByteReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    public readonly int Position => _position;
    public readonly int Length => _data.Length;
    public readonly int Remaining => _data.Length - _position;

    public void Seek(int absolutePosition) => _position = absolutePosition;
    public void Skip(int byteCount) => _position += byteCount;

    /// <summary>Pascal <c>Integer</c> / <c>LongInt</c> (4 bytes, signed).</summary>
    public int ReadInt32()
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(_position, 4));
        _position += 4;
        return value;
    }

    /// <summary>Pascal <c>Cardinal</c> / <c>LongWord</c> (4 bytes, unsigned).</summary>
    public uint ReadUInt32()
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_position, 4));
        _position += 4;
        return value;
    }

    /// <summary>Pascal <c>Word</c> (2 bytes, unsigned).</summary>
    public ushort ReadUInt16()
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(_position, 2));
        _position += 2;
        return value;
    }

    /// <summary>Pascal <c>Smallint</c> (2 bytes, signed).</summary>
    public short ReadInt16()
    {
        var value = BinaryPrimitives.ReadInt16LittleEndian(_data.Slice(_position, 2));
        _position += 2;
        return value;
    }

    /// <summary>Pascal <c>Byte</c> (1 byte, unsigned).</summary>
    public byte ReadByte() => _data[_position++];

    /// <summary>Pascal <c>Shortint</c> (1 byte, signed).</summary>
    public sbyte ReadSByte() => (sbyte)_data[_position++];

    /// <summary>Pascal <c>Single</c> / <c>ShortFloat</c> alias used for 32-bit floats (4 bytes).</summary>
    public float ReadSingle()
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(_data.Slice(_position, 4));
        _position += 4;
        return value;
    }

    /// <summary>Pascal <c>Double</c> (8 bytes, IEEE 754).</summary>
    public double ReadDouble()
    {
        var value = BinaryPrimitives.ReadDoubleLittleEndian(_data.Slice(_position, 8));
        _position += 8;
        return value;
    }

    /// <summary>
    /// MB3D <c>ShortFloat = array[0..1] of Shortint</c> (Math3D.pas:38): a tiny base-10 float
    /// stored as mantissa+exponent. Decode mirrors <c>ShortFloatToSingle</c> (Math3D.pas:1182):
    /// <c>mantissa * 10^(clamp(exp,-25,25) - 1)</c>.
    /// </summary>
    public float ReadShortFloat()
    {
        int mantissa = ReadSByte();
        int exponent = ReadSByte();
        return (float)(mantissa * Math.Pow(10, Math.Min(25, Math.Max(-25, exponent)) - 1));
    }

    /// <summary>
    /// MB3D <c>Double7B = array[0..6] of Byte</c> (Math3D.pas:33): a double truncated to its high
    /// 7 bytes (the lowest mantissa byte is dropped). Decode mirrors <c>D7BtoDouble</c>
    /// (Math3D.pas:529): rebuild an 8-byte double whose low byte is 0 and whose upper 7 bytes are
    /// the stored array.
    /// </summary>
    public double ReadDouble7B()
    {
        ulong bits = 0;
        for (var i = 0; i < 7; i++)
            bits |= (ulong)_data[_position + i] << (8 * (i + 1));
        _position += 7;
        return BitConverter.Int64BitsToDouble((long)bits);
    }

    /// <summary>Reads <paramref name="count"/> raw bytes and advances.</summary>
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        var slice = _data.Slice(_position, count);
        _position += count;
        return slice;
    }

    /// <summary>
    /// Reads a Delphi <c>ShortString</c>: a leading length byte followed by a fixed-size character
    /// buffer (<paramref name="bufferSize"/> includes the length byte). Used for the animation
    /// output folder, stored as <c>s[0..255]</c> = 256 bytes (Animation.pas:1236).
    /// </summary>
    public string ReadShortString(int bufferSize)
    {
        var buffer = ReadBytes(bufferSize);
        int length = Math.Min(buffer[0], bufferSize - 1);
        return System.Text.Encoding.Latin1.GetString(buffer.Slice(1, length));
    }

    /// <summary>
    /// Reads a fixed-size, null-padded ASCII name buffer (MB3D <c>CustomFname: array[0..n] of Byte</c>),
    /// trimming at the first NUL. Used for hybrid formula names (THAformula.CustomFname, 32 bytes).
    /// </summary>
    public string ReadFixedName(int bufferSize)
    {
        var buffer = ReadBytes(bufferSize);
        int length = buffer.IndexOf((byte)0);
        if (length < 0) length = bufferSize;
        return System.Text.Encoding.Latin1.GetString(buffer.Slice(0, length)).TrimEnd();
    }
}
