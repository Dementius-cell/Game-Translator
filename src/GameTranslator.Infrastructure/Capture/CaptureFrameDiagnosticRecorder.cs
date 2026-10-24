using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using GameTranslator.Application.Capture;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace GameTranslator.Infrastructure.Capture;

/// <summary>Explicit local opt-in; sampled capture inputs, never a replacement capture path.</summary>
internal sealed class CaptureFrameDiagnosticRecorder
{
    private readonly object gate = new();
    private readonly string directory;
    private readonly TimeProvider clock;
    private readonly int maximumFrames;
    private readonly long maximumBytes;
    private long lastStarted;
    private int accepted;
    private long bytesWritten;
    private bool failed;
    private Task completion = Task.CompletedTask;

    internal CaptureFrameDiagnosticRecorder(string directory, TimeProvider? clock = null,
        int maximumFrames = 1800, long maximumBytes = 2L * 1024 * 1024 * 1024)
    {
        this.directory = directory;
        this.clock = clock ?? TimeProvider.System;
        this.maximumFrames = maximumFrames;
        this.maximumBytes = maximumBytes;
    }

    internal static CaptureFrameDiagnosticRecorder? CreateIfEnabled()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "capture-frames.enabled"))) return null;
        return new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameTranslator", "Diagnostics", "Frames", $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid():N}"));
    }

    internal Task Completion { get { lock (gate) return completion; } }

    internal bool TryRecord(CapturedFrame frame)
    {
        lock (gate)
        {
            var now = clock.GetTimestamp();
            if (failed || accepted >= maximumFrames || bytesWritten >= maximumBytes || !completion.IsCompleted
                || (accepted > 0 && clock.GetElapsedTime(lastStarted, now) < TimeSpan.FromSeconds(1))) return false;
            if (frame.PixelFormat != "Bgra32" || frame.Stride < (long)frame.Width * 4) return false;
            lastStarted = now;
            var sequence = ++accepted;
            // CapturedFrame owns an immutable copy. No copying/encoding/disk IO on the capture gate.
            completion = Task.Run(() => WriteAsync(frame, sequence));
            return true;
        }
    }

    private async Task WriteAsync(CapturedFrame frame, int sequence)
    {
        try
        {
            var rowBytes = checked(frame.Width * 4);
            var pixels = new byte[checked(rowBytes * frame.Height)];
            for (int y = 0; y < frame.Height; y++)
                frame.PixelData.Span.Slice(y * frame.Stride, rowBytes).CopyTo(pixels.AsSpan(y * rowBytes));
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                (uint)frame.Width, (uint)frame.Height, 96, 96, pixels);
            await encoder.FlushAsync();
            var png = new byte[checked((int)stream.Size)];
            using (var reader = new DataReader(stream.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)png.Length);
                reader.ReadBytes(png);
            }
            var metadata = JsonSerializer.SerializeToUtf8Bytes(new {
                sequence, recordedUtc = clock.GetUtcNow(), frame.CapturedAt, frame.Origin, frame.IsCachedReuse,
                frame.Region, frame.Width, frame.Height, frame.Stride, frame.PixelFormat,
                capturePoolFormat = "B8G8R8A8UIntNormalized", hdrToneMapping = "none; PNG is the current 8-bit pipeline input, not HDR scene data",
                pixelSha256 = Convert.ToHexString(SHA256.HashData(pixels)),
                pngSha256 = Convert.ToHexString(SHA256.HashData(png)),
                sampling = "at most 1 frame/sec globally; one encoder; 1800 frames / 2 GiB per capture source; no upload"
            }, new JsonSerializerOptions { WriteIndented = true });
            lock (gate)
            {
                if (bytesWritten + png.Length + metadata.Length > maximumBytes) { failed = true; return; }
                bytesWritten += png.Length + metadata.Length;
            }
            Directory.CreateDirectory(directory);
            var stem = Path.Combine(directory, $"{sequence:D4}-{frame.CapturedAt:HHmmssfff}");
            await File.WriteAllBytesAsync(stem + ".png", png);
            await File.WriteAllBytesAsync(stem + ".json", metadata);
        }
        catch (Exception exception)
        {
            // Diagnostic failures must never break capture or become unobserved task failures.
            lock (gate) failed = true;
            Trace.TraceWarning($"Capture frame diagnostics stopped: {exception.GetType().Name}");
        }
    }
}
