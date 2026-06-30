namespace FractalAnimator.Core.Mb3d;

/// <summary>MB3D <c>TRGB = array[0..2] of Byte</c> (TypeDefinitions.pas:39).</summary>
public readonly record struct Mb3dRgb(byte B0, byte B1, byte B2)
{
    public static Mb3dRgb Read(ref Mb3dByteReader reader) =>
        new(reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
}

/// <summary>
/// One light source. MB3D <c>TLight8 = packed record</c> (TypeDefinitions.pas:198), 32 bytes.
/// Positions are stored as 7-byte truncated doubles; <c>Lamp</c> is a 2-byte ShortFloat reinterpret
/// of the <c>Word</c> field (HeaderTrafos.pas:585 reads it via <c>ShortFloatToSingle(@Lamp)</c>).
/// </summary>
public sealed class Mb3dLight
{
    public byte Option;            // bit1 off, bit2 lightmap, bit3 posLight, bit7 hard-shadow on
    public byte Function;          // 4-bit spec function + 2-bit diffuse function
    public ushort LampRaw;         // reinterpreted as ShortFloat for the posLight amplitude
    public Mb3dRgb Color;          // RGB 24-bit
    public ushort LightMapNr;      // 0 = none, else lightmap index
    public double PositionX;       // Double7B
    public byte AdditionalByteEx;
    public double PositionY;       // Double7B
    public byte FreeByte;
    public double PositionZ;       // Double7B

    public float LampValue
    {
        get
        {
            int mantissa = (sbyte)(LampRaw & 0xFF);
            int exponent = (sbyte)(LampRaw >> 8);
            return (float)(mantissa * Math.Pow(10, Math.Min(25, Math.Max(-25, exponent)) - 1));
        }
    }

    public static Mb3dLight Read(ref Mb3dByteReader reader)
    {
        var light = new Mb3dLight
        {
            Option = reader.ReadByte(),
            Function = reader.ReadByte(),
            LampRaw = reader.ReadUInt16(),
            Color = Mb3dRgb.Read(ref reader),
            LightMapNr = reader.ReadUInt16(),
            PositionX = reader.ReadDouble7B(),
            AdditionalByteEx = reader.ReadByte(),
            PositionY = reader.ReadDouble7B(),
            FreeByte = reader.ReadByte(),
            PositionZ = reader.ReadDouble7B(),
        };
        return light;
    }
}

/// <summary>Surface color stop. MB3D <c>TLCol8</c> (TypeDefinitions.pas:189), 10 bytes.</summary>
public readonly record struct Mb3dSurfaceColor(ushort Position, uint ColorDiffuse, uint ColorSpecular)
{
    public static Mb3dSurfaceColor Read(ref Mb3dByteReader reader) =>
        new(reader.ReadUInt16(), reader.ReadUInt32(), reader.ReadUInt32());
}

/// <summary>Interior color stop. MB3D <c>TICol8</c> (TypeDefinitions.pas:194), 6 bytes.</summary>
public readonly record struct Mb3dInteriorColor(ushort Position, uint Color)
{
    public static Mb3dInteriorColor Read(ref Mb3dByteReader reader) =>
        new(reader.ReadUInt16(), reader.ReadUInt32());
}

/// <summary>
/// Lighting + color block. MB3D <c>TLightingParas9 = packed record</c> (TypeDefinitions.pas:258),
/// 408 bytes. Embedded as the trailing <c>Light</c> field of <see cref="Mb3dMandHeader"/>.
/// </summary>
public sealed class Mb3dLighting
{
    public const int ByteSize = 408;

    public short VarColZpos;
    public byte RoughnessFactor;
    public byte ColorMap;
    public Mb3dRgb DynFogCol2;
    public byte AdditionalOptions;
    public readonly int[] TrackbarPos = new int[9];   // TBpos: array[3..11] of Integer
    public uint TrackbarOptions;
    public byte FineColAdj1;
    public byte FineColAdj2;
    public byte PicOffsetX;
    public byte PicOffsetY;
    public Mb3dRgb AmbientColor;
    public byte DynFogR;
    public Mb3dRgb AmbientColor2;
    public byte DynFogG;
    public Mb3dRgb DepthColor;
    public byte DynFogB;
    public Mb3dRgb DepthColor2;
    public byte PicOffsetZ;
    public readonly Mb3dLight[] Lights = new Mb3dLight[6];
    public readonly Mb3dSurfaceColor[] SurfaceColors = new Mb3dSurfaceColor[10];
    public readonly Mb3dInteriorColor[] InteriorColors = new Mb3dInteriorColor[4];
    public readonly byte[] BackgroundBitmapName = new byte[24];

    public static Mb3dLighting Read(ref Mb3dByteReader reader)
    {
        int start = reader.Position;
        var lighting = new Mb3dLighting
        {
            VarColZpos = reader.ReadInt16(),
            RoughnessFactor = reader.ReadByte(),
            ColorMap = reader.ReadByte(),
            DynFogCol2 = Mb3dRgb.Read(ref reader),
            AdditionalOptions = reader.ReadByte(),
        };
        for (var i = 0; i < 9; i++) lighting.TrackbarPos[i] = reader.ReadInt32();
        lighting.TrackbarOptions = reader.ReadUInt32();
        lighting.FineColAdj1 = reader.ReadByte();
        lighting.FineColAdj2 = reader.ReadByte();
        lighting.PicOffsetX = reader.ReadByte();
        lighting.PicOffsetY = reader.ReadByte();
        lighting.AmbientColor = Mb3dRgb.Read(ref reader);
        lighting.DynFogR = reader.ReadByte();
        lighting.AmbientColor2 = Mb3dRgb.Read(ref reader);
        lighting.DynFogG = reader.ReadByte();
        lighting.DepthColor = Mb3dRgb.Read(ref reader);
        lighting.DynFogB = reader.ReadByte();
        lighting.DepthColor2 = Mb3dRgb.Read(ref reader);
        lighting.PicOffsetZ = reader.ReadByte();
        for (var i = 0; i < 6; i++) lighting.Lights[i] = Mb3dLight.Read(ref reader);
        for (var i = 0; i < 10; i++) lighting.SurfaceColors[i] = Mb3dSurfaceColor.Read(ref reader);
        for (var i = 0; i < 4; i++) lighting.InteriorColors[i] = Mb3dInteriorColor.Read(ref reader);
        reader.ReadBytes(24).CopyTo(lighting.BackgroundBitmapName);

        int consumed = reader.Position - start;
        if (consumed != ByteSize)
            throw new InvalidDataException($"TLightingParas9 read {consumed} bytes, expected {ByteSize}.");
        return lighting;
    }
}
