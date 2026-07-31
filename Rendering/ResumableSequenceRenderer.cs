using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FractalAnimator.Core;
using FractalAnimator.Core.Atoms;
using FractalAnimator.Core.Kernel;

namespace FractalAnimator.Rendering;

public sealed class SequenceSpec
{
    public required string OutputDirectory { get; init; }
    public required BigDecimalCompat CenterReal { get; init; }
    public required BigDecimalCompat CenterImag { get; init; }
    public required double JuliaRawReal { get; init; }
    public required double JuliaRawImag { get; init; }
    public required double PowerReal { get; init; }
    public required double PowerImag { get; init; }
    public required double LeafOffset { get; init; }
    public required double ColorR { get; init; }
    public required double ColorG { get; init; }
    public required double ColorB { get; init; }
    public required double StartZooms { get; init; }
    public required double EndZooms { get; init; }
    public required int Frames { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Iterations { get; init; }

    public double ZoomAt(int frame) =>
        Frames <= 1 ? StartZooms : StartZooms + (EndZooms - StartZooms) * frame / (Frames - 1);

    public string Fingerprint()
    {
        var sb = new StringBuilder();
        sb.Append(CenterReal.ToPlainString()).Append('|').Append(CenterImag.ToPlainString()).Append('|');
        sb.Append(JuliaRawReal.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        sb.Append(JuliaRawImag.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        sb.Append(PowerReal.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        sb.Append(PowerImag.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        sb.Append(LeafOffset.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        sb.Append(ColorR.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        sb.Append(ColorG.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        sb.Append(ColorB.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        sb.Append(StartZooms.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        sb.Append(EndZooms.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        sb.Append(Frames).Append('|').Append(Width).Append('x').Append(Height).Append('|').Append(Iterations);
        return sb.ToString();
    }
}

public sealed class SequenceProgress
{
    public int FrameIndex;
    public double Zooms;
    public int Uncertified;
    public double RenderMilliseconds;
    public int Completed;
    public int Total;
    public bool Skipped;
}

public static class ResumableSequenceRenderer
{
    const int WriterQueueCapacity = 4;
    const string ManifestName = "sequence.manifest";

    sealed class FrameJob
    {
        public required int Index { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required byte[] Bgr { get; init; }
        public required string FinalPath { get; init; }
    }

    public static string FramePath(SequenceSpec spec, int index) =>
        Path.Combine(spec.OutputDirectory, $"frame_{index:D6}.png");

    public static int Render(SequenceSpec spec, Action<SequenceProgress>? onProgress, CancellationToken token)
    {
        Directory.CreateDirectory(spec.OutputDirectory);
        ValidateManifest(spec);
        PurgeStaleTemporaries(spec.OutputDirectory);

        var baked = PowerJuliaBaker.Bake(
            spec.CenterReal, spec.CenterImag, spec.JuliaRawReal, spec.JuliaRawImag,
            spec.PowerReal, spec.PowerImag, spec.Iterations, token);

        var queue = new BlockingCollection<FrameJob>(WriterQueueCapacity);
        Exception? writerFailure = null;
        var writer = new Thread(() =>
        {
            try
            {
                foreach (var job in queue.GetConsumingEnumerable())
                    WriteAtomically(job);
            }
            catch (Exception ex) { writerFailure = ex; }
        })
        {
            IsBackground = false,
            Name = "fractal-frame-writer",
            Priority = ThreadPriority.BelowNormal,
        };
        writer.Start();

        var completed = 0;
        try
        {
            for (var frame = 0; frame < spec.Frames; frame++)
            {
                token.ThrowIfCancellationRequested();
                if (writerFailure != null) throw new IOException("frame writer failed", writerFailure);

                var finalPath = FramePath(spec, frame);
                var zooms = spec.ZoomAt(frame);

                if (IsFrameComplete(finalPath))
                {
                    completed++;
                    onProgress?.Invoke(new SequenceProgress
                    {
                        FrameIndex = frame, Zooms = zooms, Uncertified = 0, RenderMilliseconds = 0,
                        Completed = completed, Total = spec.Frames, Skipped = true,
                    });
                    continue;
                }

                var rgb = new float[spec.Width * spec.Height * 3];
                var stats = CertifiedRenderer.Render(baked, zooms, spec.Width, spec.Height, spec.Iterations, rgb, token);

                var bgr = PackBgr(rgb, spec.Width, spec.Height);
                queue.Add(new FrameJob
                {
                    Index = frame, Width = spec.Width, Height = spec.Height,
                    Bgr = bgr, FinalPath = finalPath,
                }, token);

                completed++;
                onProgress?.Invoke(new SequenceProgress
                {
                    FrameIndex = frame, Zooms = zooms, Uncertified = stats.Uncertified,
                    RenderMilliseconds = stats.TotalMilliseconds,
                    Completed = completed, Total = spec.Frames, Skipped = false,
                });
            }
        }
        finally
        {
            queue.CompleteAdding();
            writer.Join();
        }

        if (writerFailure != null) throw new IOException("frame writer failed", writerFailure);
        return completed;
    }

    static void ValidateManifest(SequenceSpec spec)
    {
        var path = Path.Combine(spec.OutputDirectory, ManifestName);
        var fingerprint = spec.Fingerprint();
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path, Encoding.UTF8).Trim();
            if (!string.Equals(existing, fingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"output directory holds frames from a different sequence; refusing to mix. " +
                    $"clear '{spec.OutputDirectory}' or choose another directory.");
            return;
        }
        WriteTextAtomically(path, fingerprint);
    }

    static void PurgeStaleTemporaries(string directory)
    {
        foreach (var stale in Directory.EnumerateFiles(directory, "*.tmp"))
        {
            try { File.Delete(stale); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    static bool IsFrameComplete(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 20) return false;
            Span<byte> header = stackalloc byte[8];
            if (stream.Read(header) != 8) return false;
            if (header[0] != 0x89 || header[1] != 'P' || header[2] != 'N' || header[3] != 'G') return false;
            stream.Seek(-12, SeekOrigin.End);
            Span<byte> trailer = stackalloc byte[12];
            var read = 0;
            while (read < 12)
            {
                var n = stream.Read(trailer[read..]);
                if (n <= 0) return false;
                read += n;
            }
            return trailer[4] == 'I' && trailer[5] == 'E' && trailer[6] == 'N' && trailer[7] == 'D';
        }
        catch (IOException) { return false; }
    }

    static byte[] PackBgr(float[] rgb, int width, int height)
    {
        var bytes = new byte[width * height * 3];
        for (var i = 0; i < width * height; i++)
        {
            var s = i * 3;
            bytes[s + 0] = ToByte(rgb[s + 2]);
            bytes[s + 1] = ToByte(rgb[s + 1]);
            bytes[s + 2] = ToByte(rgb[s + 0]);
        }
        return bytes;
    }

    static byte ToByte(float v)
    {
        if (float.IsNaN(v)) return 0;
        var scaled = (int)Math.Round(v * 255.0f);
        return (byte)Math.Clamp(scaled, 0, 255);
    }

    static void WriteAtomically(FrameJob job)
    {
        var temporary = job.FinalPath + ".tmp";
        using (var bitmap = new Bitmap(job.Width, job.Height, PixelFormat.Format24bppRgb))
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, job.Width, job.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var rowBytes = job.Width * 3;
                for (var y = 0; y < job.Height; y++)
                    Marshal.Copy(job.Bgr, y * rowBytes, IntPtr.Add(data.Scan0, y * data.Stride), rowBytes);
            }
            finally { bitmap.UnlockBits(data); }

            using var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
            bitmap.Save(stream, ImageFormat.Png);
            stream.Flush(true);
        }
        File.Move(temporary, job.FinalPath, overwrite: true);
    }

    static void WriteTextAtomically(string path, string content)
    {
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var text = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            text.Write(content);
            text.Flush();
            stream.Flush(true);
        }
        File.Move(temporary, path, overwrite: true);
    }
}
