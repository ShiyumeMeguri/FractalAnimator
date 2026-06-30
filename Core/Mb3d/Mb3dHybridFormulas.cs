namespace FractalAnimator.Core.Mb3d;

/// <summary>
/// One hybrid formula slot. MB3D <c>THAformula = packed record</c> (TypeDefinitions.pas:972),
/// 188 bytes. <c>IterationCount</c> = how many iterations this formula runs inside the hybrid loop
/// (0 means the slot is inactive). <c>FormulaNumber</c> &lt; 20 selects a built-in formula; >= 20
/// means the slot is an external formula identified by <see cref="Name"/>.
/// </summary>
public sealed class Mb3dFormulaSlot
{
    public const int ByteSize = 188;

    public int IterationCount;        // iItCount
    public int FormulaNumber;         // iFnr (< 20 built-in, >= 20 external-by-name)
    public int OptionCount;           // iOptionCount
    public string Name = "";          // CustomFname[32]
    public readonly byte[] OptionTypes = new byte[16];   // byOptionType[16]
    public readonly double[] OptionValues = new double[16]; // dOptionValue[16]

    public bool IsActive => IterationCount > 0;

    public static Mb3dFormulaSlot Read(ref Mb3dByteReader reader)
    {
        int start = reader.Position;
        var slot = new Mb3dFormulaSlot
        {
            IterationCount = reader.ReadInt32(),
            FormulaNumber = reader.ReadInt32(),
            OptionCount = reader.ReadInt32(),
            Name = reader.ReadFixedName(32),
        };
        reader.ReadBytes(16).CopyTo(slot.OptionTypes);
        for (var i = 0; i < 16; i++) slot.OptionValues[i] = reader.ReadDouble();

        int consumed = reader.Position - start;
        if (consumed != ByteSize)
            throw new InvalidDataException($"THAformula read {consumed} bytes, expected {ByteSize}.");
        return slot;
    }
}

/// <summary>
/// The hybrid-formula addon attached after each header. New format:
/// MB3D <c>THeaderCustomAddon = packed record</c> (TypeDefinitions.pas:981), 1136 bytes (8-byte
/// head + 6 slots). The discriminator mirrors <c>LoadHAddon</c> (FileHandling.pas:1820): if the
/// first byte (<c>bHCAversion</c>) is in 16..99 the new layout is used, otherwise the legacy
/// <c>THeaderCustomAddonOld</c> (1016 bytes) is read and mapped into the same slots.
/// </summary>
public sealed class Mb3dHybridAddon
{
    public const int NewByteSize = 1136;
    public const int OldByteSize = 1016;

    public byte Version;              // bHCAversion
    public byte Options1;            // hybrid kind: 0 alt, 1 interpolated, 2 DE-combined, 3 IFS
    public byte Options2;            // bit1 disable analytic DE; bit2+3 inside/outside render mode
    public byte Options3;            // DE-combination kind
    public byte FormulaCount;        // iFCount
    public byte HybridOption1;       // end1, repeat1 (2x 4-bit)
    public ushort HybridOption2;     // start2, end2, repeat2 (3x 4-bit)
    public readonly Mb3dFormulaSlot[] Slots = new Mb3dFormulaSlot[6];

    public static Mb3dHybridAddon Read(ref Mb3dByteReader reader)
    {
        // Peek the version byte (low byte of the first int) without advancing.
        int peekStart = reader.Position;
        byte versionByte = reader.ReadByte();
        reader.Seek(peekStart);

        return versionByte is >= 16 and <= 99 ? ReadNew(ref reader) : ReadOld(ref reader);
    }

    private static Mb3dHybridAddon ReadNew(ref Mb3dByteReader reader)
    {
        int start = reader.Position;
        var addon = new Mb3dHybridAddon
        {
            Version = reader.ReadByte(),
            Options1 = reader.ReadByte(),
            Options2 = reader.ReadByte(),
            Options3 = reader.ReadByte(),
            FormulaCount = reader.ReadByte(),
            HybridOption1 = reader.ReadByte(),
            HybridOption2 = reader.ReadUInt16(),
        };
        for (var i = 0; i < 6; i++) addon.Slots[i] = Mb3dFormulaSlot.Read(ref reader);

        int consumed = reader.Position - start;
        if (consumed != NewByteSize)
            throw new InvalidDataException($"THeaderCustomAddon read {consumed} bytes, expected {NewByteSize}.");
        return addon;
    }

    /// <summary>
    /// Legacy <c>THeaderCustomAddonOld</c> (TypeDefinitions.pas:964): iHCAversion, iCustomCount,
    /// iItCounts[6], iFormula[6], CustomFname[6][32], dOptionValues[6][16]. Mapped into the modern
    /// slot model exactly as <c>LoadHAddon</c> does.
    /// </summary>
    private static Mb3dHybridAddon ReadOld(ref Mb3dByteReader reader)
    {
        int start = reader.Position;
        var addon = new Mb3dHybridAddon { Version = 0 };
        for (var i = 0; i < 6; i++) addon.Slots[i] = new Mb3dFormulaSlot();

        reader.ReadInt32();                 // iHCAversion
        reader.ReadInt32();                 // iCustomCount
        for (var i = 0; i < 6; i++) addon.Slots[i].IterationCount = reader.ReadInt32();
        for (var i = 0; i < 6; i++) addon.Slots[i].FormulaNumber = reader.ReadInt32();
        for (var i = 0; i < 6; i++) addon.Slots[i].Name = ReadNameFrom(ref reader, 32);
        for (var i = 0; i < 6; i++)
            for (var j = 0; j < 16; j++) addon.Slots[i].OptionValues[j] = reader.ReadDouble();

        int consumed = reader.Position - start;
        if (consumed != OldByteSize)
            throw new InvalidDataException($"THeaderCustomAddonOld read {consumed} bytes, expected {OldByteSize}.");
        return addon;
    }

    private static string ReadNameFrom(ref Mb3dByteReader reader, int size)
    {
        var buffer = reader.ReadBytes(size);
        int length = buffer.IndexOf((byte)0);
        if (length < 0) length = size;
        return System.Text.Encoding.Latin1.GetString(buffer.Slice(0, length)).TrimEnd();
    }
}
