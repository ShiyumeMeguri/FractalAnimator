namespace FractalAnimator.Core.Mb3d;

/// <summary>
/// The master per-frame parameter record. MB3D <c>TMandHeader10 = packed record</c>
/// (TypeDefinitions.pas:733), 840 bytes. Every field is read in declaration order; the trailing
/// comments are the Pascal byte offsets so the layout can be audited against the source. Runtime
/// pointer fields (<c>PHCustomF</c>, <c>PCFAddon</c>) hold garbage on disk and are skipped, but
/// their bytes are still consumed so the cursor stays aligned.
/// </summary>
public sealed class Mb3dMandHeader
{
    public const int ByteSize = 840;

    public int MandId;                 // #0
    public int Width;                  // #4
    public int Height;                 // #8
    public int Iterations;             // #12
    public ushort Options;             // #16  iOptions
    public byte NewOptions;            // #18  bit1: quaternion rotation instead of matrix; bit2: color on it nr
    public byte ColorOnIt;             // #19
    public double ZStart;              // #20
    public double ZEnd;                // #28
    public double XMid;                // #36  camera target / pivot
    public double YMid;                // #44
    public double ZMid;                // #52
    public double XWRot;               // #60  4D rotation (or quaternion components, see NewOptions bit1)
    public double YWRot;               // #68
    public double ZWRot;               // #76
    public double Zoom;                // #84
    public double RStop;               // #92  escape radius
    public int ReflectsCalcTime;       // #100
    public float FMixPow;              // #104 formula DE-mix power
    public double FOVy;                // #108 vertical field of view
    public float TRIndex;              // #116 transmission index
    public float TRScattering;         // #120 light scattering
    public byte MCOptions;             // #124
    public byte MCDiffReflects;        // #125
    public byte StereoMode;            // #126
    public byte SSAO24BorderMirrorSize;// #127
    public int AmbCalcTime;            // #128
    public byte NormalsOnDE;           // #132
    public byte CalculateHardShadow;   // #133
    public byte StepsAfterDEStop;      // #134 binary search steps
    public ushort MinimumIterations;   // #135
    public ushort MCLastY;             // #137
    public byte Calc1HSsoft;           // #139
    public int AvrgDEsteps;            // #140 value * 10
    public int AvrgIts;                // #144 value * 10
    public byte PlanarOptic;           // #148 camera: 0 planar, 1 ..., 2 spherePano, 3 dome
    public byte CalcAmbShadowAutomatic;// #149
    public float NaviMinDist;          // #150
    public double StepWidth;           // #154 raymarch step width (relative to zoom)
    public byte VaryDEstopOnFOV;       // #162
    public byte HScalculated;          // #163
    public float DOFZsharp;            // #164
    public float DOFclipR;             // #168
    public float DOFaperture;          // #172
    public byte CutOption;             // #176
    public float DEstop;               // #177 distance-estimate stop threshold
    public byte CalcDOFtype;           // #181
    public float ZStepDiv;             // #182 mZstepDiv
    public byte MCDepth;               // #186
    public byte SSAORcount;            // #187
    public byte AODEdithering;         // #188
    public byte ImageScale;            // #189 oversampling factor
    public byte IsJulia;               // #190
    public double Jx;                  // #191 Julia seed
    public double Jy;                  // #199
    public double Jz;                  // #207
    public double Jw;                  // #215
    public byte DFogIt;                // #223
    public float MCSoftShadowRadius;   // #224 ShortFloat (2 bytes)
    public float HSmaxLengthMultiplier;// #226
    public float StereoScreenWidth;    // #230
    public float StereoScreenDistance; // #234
    public float StereoMinDistance;    // #238
    public float RaystepLimiter;       // #242
    public readonly double[] VGrads = new double[9]; // #246 hVGrads: TMatrix3 (3x3, row-major)
    public byte MCSaturation;          // #318
    public float AmbShadowThreshold;   // #319
    public int CalcTime;               // #323
    public int CalcHStime;             // #327
    public byte CalcNsOnZBufAuto;      // #331
    public float SRamount;             // #332
    public byte CalcSRautomatic;       // #336
    public byte SRreflectioncount;     // #337
    public float ColorMul;             // #338
    public byte Color2Option;          // #342
    public byte VolLightNr;            // #343
    public byte Calc3D;                // #344
    public byte SliceCalc;             // #345
    public double CutX;                // #346
    public double CutY;                // #354
    public double CutZ;                // #362
    public float TransmissionAbsorption;// #370
    public float DEAOmaxL;             // #374
    public float DEcombS;              // #378
    public float DOFZsharp2;           // #410 (after the 28 pointer bytes at #382)
    public int MaxIts;                 // #414
    public int MaxItsF2;               // #418
    public byte DEmixColorOption;      // #422
    public byte MCcontrast;            // #423
    public float M3dVersion;           // #424
    public int TilingOptions;          // #428
    public Mb3dLighting Light = null!; // #432 TLightingParas9 (408 bytes)

    public static Mb3dMandHeader Read(ref Mb3dByteReader reader)
    {
        int start = reader.Position;
        var header = new Mb3dMandHeader
        {
            MandId = reader.ReadInt32(),
            Width = reader.ReadInt32(),
            Height = reader.ReadInt32(),
            Iterations = reader.ReadInt32(),
            Options = reader.ReadUInt16(),
            NewOptions = reader.ReadByte(),
            ColorOnIt = reader.ReadByte(),
            ZStart = reader.ReadDouble(),
            ZEnd = reader.ReadDouble(),
            XMid = reader.ReadDouble(),
            YMid = reader.ReadDouble(),
            ZMid = reader.ReadDouble(),
            XWRot = reader.ReadDouble(),
            YWRot = reader.ReadDouble(),
            ZWRot = reader.ReadDouble(),
            Zoom = reader.ReadDouble(),
            RStop = reader.ReadDouble(),
            ReflectsCalcTime = reader.ReadInt32(),
            FMixPow = reader.ReadSingle(),
            FOVy = reader.ReadDouble(),
            TRIndex = reader.ReadSingle(),
            TRScattering = reader.ReadSingle(),
            MCOptions = reader.ReadByte(),
            MCDiffReflects = reader.ReadByte(),
            StereoMode = reader.ReadByte(),
            SSAO24BorderMirrorSize = reader.ReadByte(),
            AmbCalcTime = reader.ReadInt32(),
            NormalsOnDE = reader.ReadByte(),
            CalculateHardShadow = reader.ReadByte(),
            StepsAfterDEStop = reader.ReadByte(),
            MinimumIterations = reader.ReadUInt16(),
            MCLastY = reader.ReadUInt16(),
            Calc1HSsoft = reader.ReadByte(),
            AvrgDEsteps = reader.ReadInt32(),
            AvrgIts = reader.ReadInt32(),
            PlanarOptic = reader.ReadByte(),
            CalcAmbShadowAutomatic = reader.ReadByte(),
            NaviMinDist = reader.ReadSingle(),
            StepWidth = reader.ReadDouble(),
            VaryDEstopOnFOV = reader.ReadByte(),
            HScalculated = reader.ReadByte(),
            DOFZsharp = reader.ReadSingle(),
            DOFclipR = reader.ReadSingle(),
            DOFaperture = reader.ReadSingle(),
            CutOption = reader.ReadByte(),
            DEstop = reader.ReadSingle(),
            CalcDOFtype = reader.ReadByte(),
            ZStepDiv = reader.ReadSingle(),
            MCDepth = reader.ReadByte(),
            SSAORcount = reader.ReadByte(),
            AODEdithering = reader.ReadByte(),
            ImageScale = reader.ReadByte(),
            IsJulia = reader.ReadByte(),
            Jx = reader.ReadDouble(),
            Jy = reader.ReadDouble(),
            Jz = reader.ReadDouble(),
            Jw = reader.ReadDouble(),
            DFogIt = reader.ReadByte(),
            MCSoftShadowRadius = reader.ReadShortFloat(),
            HSmaxLengthMultiplier = reader.ReadSingle(),
            StereoScreenWidth = reader.ReadSingle(),
            StereoScreenDistance = reader.ReadSingle(),
            StereoMinDistance = reader.ReadSingle(),
            RaystepLimiter = reader.ReadSingle(),
        };
        for (var i = 0; i < 9; i++) header.VGrads[i] = reader.ReadDouble();
        header.MCSaturation = reader.ReadByte();
        header.AmbShadowThreshold = reader.ReadSingle();
        header.CalcTime = reader.ReadInt32();
        header.CalcHStime = reader.ReadInt32();
        header.CalcNsOnZBufAuto = reader.ReadByte();
        header.SRamount = reader.ReadSingle();
        header.CalcSRautomatic = reader.ReadByte();
        header.SRreflectioncount = reader.ReadByte();
        header.ColorMul = reader.ReadSingle();
        header.Color2Option = reader.ReadByte();
        header.VolLightNr = reader.ReadByte();
        header.Calc3D = reader.ReadByte();
        header.SliceCalc = reader.ReadByte();
        header.CutX = reader.ReadDouble();
        header.CutY = reader.ReadDouble();
        header.CutZ = reader.ReadDouble();
        header.TransmissionAbsorption = reader.ReadSingle();
        header.DEAOmaxL = reader.ReadSingle();
        header.DEcombS = reader.ReadSingle();
        reader.Skip(24);   // PHCustomF: array[0..5] of Pointer (runtime addresses, garbage on disk)
        reader.Skip(4);    // PCFAddon: Pointer
        header.DOFZsharp2 = reader.ReadSingle();
        header.MaxIts = reader.ReadInt32();
        header.MaxItsF2 = reader.ReadInt32();
        header.DEmixColorOption = reader.ReadByte();
        header.MCcontrast = reader.ReadByte();
        header.M3dVersion = reader.ReadSingle();
        header.TilingOptions = reader.ReadInt32();
        header.Light = Mb3dLighting.Read(ref reader);

        int consumed = reader.Position - start;
        if (consumed != ByteSize)
            throw new InvalidDataException($"TMandHeader10 read {consumed} bytes, expected {ByteSize}.");
        return header;
    }
}
