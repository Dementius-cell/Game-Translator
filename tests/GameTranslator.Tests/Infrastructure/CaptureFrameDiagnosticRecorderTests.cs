using System.IO;
using System.Text.Json;
using GameTranslator.Application.Capture;
using GameTranslator.Infrastructure.Capture;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace GameTranslator.Tests.Infrastructure;

public sealed class CaptureFrameDiagnosticRecorderTests
{
    private static CapturedFrame Frame() => new(new CaptureRegion(7, 9, 2, 1), 2, 1, 12,
        "Bgra32", new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 99, 99, 99, 99 }, DateTimeOffset.UtcNow,
        new CaptureFrameOrigin(Guid.NewGuid(), TimeSpan.FromSeconds(42), DateTimeOffset.UtcNow));

    [Fact]
    public async Task PngPreservesPixelsWithoutStridePaddingAndRecordsOrigin()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gt-frame-test-" + Guid.NewGuid());
        var recorder = new CaptureFrameDiagnosticRecorder(directory, maximumFrames: 1);
        var frame = Frame();
        try
        {
            Assert.True(recorder.TryRecord(frame));
            await recorder.Completion;
            Assert.False(recorder.TryRecord(frame));
            var png = await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(directory, "*.png")));
            using var stream = new InMemoryRandomAccessStream();
            using var writer = new DataWriter(stream);
            writer.WriteBytes(png);
            await writer.StoreAsync();
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            Assert.Equal(frame.PixelData.Span[..8].ToArray(), data.DetachPixelData());
            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(directory, "*.json"))));
            Assert.Equal(frame.Origin!.SourceId, metadata.RootElement.GetProperty("Origin").GetProperty("SourceId").GetGuid());
            Assert.Equal(7, metadata.RootElement.GetProperty("Region").GetProperty("X").GetInt32());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ByteBudgetStopsBeforeWritingOversizedSample()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gt-frame-test-" + Guid.NewGuid());
        var recorder = new CaptureFrameDiagnosticRecorder(directory, maximumBytes: 1);
        Assert.True(recorder.TryRecord(Frame()));
        await recorder.Completion;
        Assert.False(Directory.Exists(directory));
        Assert.False(recorder.TryRecord(Frame()));
    }

    [Fact]
    public async Task UnwritableDirectoryDoesNotFailCaptureCaller()
    {
        var path = Path.GetTempFileName();
        try
        {
            var recorder = new CaptureFrameDiagnosticRecorder(path);
            Assert.True(recorder.TryRecord(Frame()));
            await recorder.Completion;
            Assert.False(recorder.TryRecord(Frame()));
        }
        finally { File.Delete(path); }
    }
}
