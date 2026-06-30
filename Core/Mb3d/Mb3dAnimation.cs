namespace FractalAnimator.Core.Mb3d;

/// <summary>A low-resolution preview frame that MB3D stored alongside each keyframe (32-bit pixels,
/// useful as pixel-accurate ground truth when validating the renderer).</summary>
public sealed class Mb3dPreviewImage
{
    public int Width;
    public int Height;
    public uint[] PixelsArgb = [];   // top-to-bottom, left-to-right
}

/// <summary>One animation keyframe: the timing fields plus the full parameter set.</summary>
public sealed class Mb3dKeyframe
{
    public int FrameCount;            // KFcount: number of in-between frames after this keyframe
    public int RenderTimeMs;          // KFtime: measured render time of the preview (ms)
    public int Smooth;                // KFsmooth: per-key easing flag
    public Mb3dMandHeader Header = null!;
    public Mb3dHybridAddon Addon = null!;
    public Mb3dPreviewImage? Preview;

    /// <summary>The active hybrid formula stack (slots with a non-zero iteration count), in order.</summary>
    public IEnumerable<Mb3dFormulaSlot> ActiveFormulas => Addon.Slots.Where(s => s.IsActive);
}

/// <summary>
/// A parsed Mandelbulb3D animation (<c>.m3a</c>). The byte layout is produced by Animation.pas
/// <c>SpeedButton9Click</c> (save) and consumed by <c>SpeedButton11Click</c> (load); this reader
/// mirrors that exact sequence. Version 5 (the only one this app targets) stores a
/// <see cref="Mb3dMandHeader"/> (TMandHeader10) + <see cref="Mb3dHybridAddon"/> per keyframe.
/// </summary>
public sealed class Mb3dAnimation
{
    public int Version;
    public int Width;
    public int Height;
    public int Scale;                 // oversampling factor (AniScale)
    public bool Calc3D;
    public string OutputFolder = "";
    public uint Flags;
    public readonly List<Mb3dKeyframe> Keyframes = [];

    // Decoded flag bits (Animation.pas:636-644 / 1237-1242).
    public int InterpolationType => (int)(Flags & 3);        // 0 linear, 1 bezier, 2 cubic
    public bool LoopAnimation => (Flags & 4) != 0;
    public int OutputFileType => (int)((Flags >> 3) & 7);
    public bool StereoAnimation => (Flags & 64) != 0;
    public bool OnlyLeftEye => (Flags & 128) != 0;
    public bool OverwriteExisting => (Flags & 256) == 0;
    public bool SaveZBuffer => (Flags & 512) != 0;

    public static Mb3dAnimation Load(string path) => Parse(File.ReadAllBytes(path), path);

    public static Mb3dAnimation Parse(ReadOnlySpan<byte> bytes, string sourceName = "")
    {
        var reader = new Mb3dByteReader(bytes);
        var animation = new Mb3dAnimation { Version = reader.ReadInt32() };
        if (animation.Version > 5)
            throw new InvalidDataException($"Unsupported .m3a version {animation.Version} (max 5) in {sourceName}.");

        animation.Width = reader.ReadInt32();
        animation.Height = reader.ReadInt32();
        animation.Scale = reader.ReadInt32();
        animation.Calc3D = reader.ReadInt32() != 0;       // AniCalc3D stored as 4-byte LongBool
        animation.OutputFolder = reader.ReadShortString(256);

        if (animation.Version > 1) animation.Flags = reader.ReadUInt32();
        int headerCount = reader.ReadInt32();
        if (headerCount is < 1 or > 4095)
            throw new InvalidDataException($"Implausible keyframe count {headerCount} in {sourceName}.");

        for (var i = 0; i < headerCount; i++)
        {
            var keyframe = new Mb3dKeyframe
            {
                FrameCount = reader.ReadInt32(),
                RenderTimeMs = reader.ReadInt32(),
                Smooth = reader.ReadInt32(),
                Header = Mb3dMandHeader.Read(ref reader),
                Addon = Mb3dHybridAddon.Read(ref reader),
            };
            animation.Keyframes.Add(keyframe);
        }

        // Trailing per-keyframe preview thumbnails (Animation.pas:687-708): width, height, then
        // 32-bit scanlines stored bottom-up. Best-effort: stop cleanly if the file is truncated.
        for (var i = 0; i < headerCount && reader.Remaining >= 8; i++)
        {
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            if (width <= 0 || height <= 0 || (long)width * height * 4 > reader.Remaining) break;
            animation.Keyframes[i].Preview = ReadPreview(ref reader, width, height);
        }

        return animation;
    }

    private static Mb3dPreviewImage ReadPreview(ref Mb3dByteReader reader, int width, int height)
    {
        var preview = new Mb3dPreviewImage { Width = width, Height = height, PixelsArgb = new uint[width * height] };
        // MB3D writes ScanLine[y] for y = height-1 downto 0, so de-flip into top-down order.
        for (var y = height - 1; y >= 0; y--)
        {
            int row = y * width;
            for (var x = 0; x < width; x++) preview.PixelsArgb[row + x] = reader.ReadUInt32();
        }
        return preview;
    }
}
