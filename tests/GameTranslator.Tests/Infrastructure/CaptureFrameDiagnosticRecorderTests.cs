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
    public async Task SampleEvidence_IdentifiesFilesAndExplainsLimitWithoutLeakingStorageExceptionMessage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gt-sample-evidence-" + Guid.NewGuid());
        var logger = new SampleLogger();
        var recorder = new CaptureFrameDiagnosticRecorder(directory, maximumFrames: 1, logger: logger);
        try
        {
            var frame = Frame();
            Assert.True(recorder.TryRecord(frame));
            await recorder.Completion;
            var saved = Assert.Single(logger.Messages);
            Assert.Contains("saved", saved);
            Assert.Contains(frame.Origin!.SourceId.ToString(), saved);
            Assert.Contains(Path.GetFileNameWithoutExtension(Assert.Single(Directory.GetFiles(directory, "*.png"))), saved);
            Assert.False(recorder.TryRecord(frame));
            Assert.False(recorder.TryRecord(frame));
            Assert.Single(logger.Messages, m => m.Contains("frame-limit"));
            var blocked = Path.Combine(directory, "blocked");
            File.WriteAllText(blocked, "synthetic");
            var failed = new CaptureFrameDiagnosticRecorder(blocked, logger: logger);
            Assert.True(failed.TryRecord(frame));
            await failed.Completion;
            Assert.Contains(logger.Messages, m => m.Contains("storage-or-encoding-failed") && m.Contains("HResult"));
            Assert.DoesNotContain(logger.Messages, m => m.Contains(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class SampleLogger : GameTranslator.Application.Abstractions.IApplicationLogger
    {
        public List<string> Messages { get; } = new();
        public void Information(string message) => Messages.Add(message);
        public void Warning(string message) => Messages.Add(message);
        public void Error(Exception error, string message) => Messages.Add(message);
    }

    [Theory]
    [InlineData("HDR", "R16G16B16A16Float")]
    [InlineData("SDR", "B8G8R8A8UIntNormalized")]
    public async Task RecordsActualCaptureColorMode(string mode, string format)
    {
        var directory = Path.Combine(Path.GetTempPath(), "gt-color-" + Guid.NewGuid());
        var recorder = new CaptureFrameDiagnosticRecorder(directory);
        try
        {
            Assert.True(recorder.TryRecord(Frame(), new CaptureColorInfo(mode, 240, "synthetic")));
            await recorder.Completion;
            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(directory, "*.json"))));
            Assert.Equal(mode, metadata.RootElement.GetProperty("displayColorMode").GetString());
            Assert.Equal(format, metadata.RootElement.GetProperty("capturePoolFormat").GetString());
            Assert.Equal(240, metadata.RootElement.GetProperty("sdrWhiteNits").GetDouble());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LiveSettingsGateCaptureAndRaisedBudgetResumesSampling()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gt-frame-settings-" + Guid.NewGuid());
        var settingsPath = Path.Combine(Path.GetTempPath(), "gt-frame-settings-" + Guid.NewGuid() + ".json");
        var preferences = new GameTranslator.Application.Settings.LiveDiagnosticsPreferences(
            new GameTranslator.Infrastructure.Settings.JsonSettingsService(settingsPath));
        preferences.Save(new(Frames: true, FramesMiB: 1));
        var clock = new TestClock();
        var recorder = new CaptureFrameDiagnosticRecorder(directory, clock, preferences: preferences);
        var pixels = new byte[600 * 600 * 4];
        new Random(42).NextBytes(pixels);
        var large = new CapturedFrame(new(0, 0, 600, 600), 600, 600, 2400,
            "Bgra32", pixels, DateTimeOffset.UtcNow);
        try
        {
            Assert.False(recorder.TryRecord(large)); // idle
            preferences.Live = true;
            preferences.Save(preferences.Current with { Enabled = false });
            Assert.False(recorder.TryRecord(large));
            preferences.Save(preferences.Current with { Enabled = true, Frames = false });
            Assert.False(recorder.TryRecord(large));
            preferences.Save(preferences.Current with { Frames = true });
            Assert.True(recorder.TryRecord(large));
            await recorder.Completion;
            Assert.False(Directory.Exists(directory)); // exceeds 1 MiB before writing either file
            clock.Ticks += TimeSpan.TicksPerSecond * 2;
            Assert.False(recorder.TryRecord(large));
            preferences.Save(preferences.Current with { FramesMiB = 2 });
            Assert.True(recorder.TryRecord(large));
            await recorder.Completion;
            Assert.Single(Directory.GetFiles(directory, "*.png"));
            Assert.Single(Directory.GetFiles(directory, "*.json"));
            Assert.InRange(Directory.GetFiles(directory).Sum(path => new FileInfo(path).Length), 1, 2 * 1024 * 1024);
            preferences.Live = false;
            clock.Ticks += TimeSpan.TicksPerSecond * 2;
            Assert.False(recorder.TryRecord(large));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            File.Delete(settingsPath);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        internal long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
    }

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
