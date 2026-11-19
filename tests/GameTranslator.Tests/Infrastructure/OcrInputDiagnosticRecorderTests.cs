using Microsoft.Extensions.DependencyInjection;
using GameTranslator.Infrastructure.Composition;
using System.IO;
using System.Text.Json;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Settings;
using GameTranslator.Infrastructure.Ocr;
using GameTranslator.Infrastructure.Settings;

namespace GameTranslator.Tests.Infrastructure;

public sealed class OcrInputDiagnosticRecorderTests
{
    [Fact]
    public void CompositionResolvesBothDiagnosticEnginesWithOriginalIdentifiers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gt-ocr-composition-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(Preferences(directory));
            new InfrastructureServiceModule().RegisterServices(services);
            using var provider = services.BuildServiceProvider();
            var engines = provider.GetServices<IOcrEngine>().ToArray();
            Assert.Equal(2, engines.Length);
            Assert.All(engines, engine => Assert.IsType<DiagnosticOcrEngine>(engine));
            Assert.Contains(engines, engine => engine.EngineId == provider.GetRequiredService<TesseractOcrEngine>().EngineId);
            Assert.Contains(engines, engine => engine.EngineId == provider.GetRequiredService<WindowsOcrEngine>().EngineId);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ConcurrentInputsRetainTheirOwnPixelsSettingsAndResults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gt-ocr-input-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var preferences = Preferences(directory);
        var recorder = new OcrInputDiagnosticRecorder(preferences, Path.Combine(directory, "inputs"));
        var engine = new DiagnosticOcrEngine(new FakeEngine(), recorder, preferences);
        try
        {
            var requests = Enumerable.Range(0, 12).Select(Request).ToArray();
            var results = await Task.WhenAll(requests.Select(r => engine.RecognizeAsync(r)));
            Assert.All(results, r => Assert.Contains(requests, q => ReferenceEquals(q, r.Request)));
            await recorder.Completion;
            var files = Directory.GetFiles(Path.Combine(directory, "inputs"), "*.json");
            Assert.Equal(12, files.Length);
            var zones = new HashSet<string>();
            foreach (var file in files)
            {
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(file));
                var root = json.RootElement;
                var input = root.GetProperty("inputMetadata");
                var zone = input.GetProperty("ZoneId").GetString()!;
                Assert.True(zones.Add(zone));
                Assert.Equal(zone, input.GetProperty("blocks")[0].GetProperty("Text").GetString());
                Assert.Equal("eng", input.GetProperty("Language").GetString());
                Assert.Equal(0, root.GetProperty("minimumIntervalMs").GetDouble());
                Assert.True(File.Exists(Path.ChangeExtension(file, ".png")));
                var request = requests.Single(r => r.ZoneId == zone);
                Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(request.Frame.PixelData.Span)),
                    root.GetProperty("pixelSha256").GetString());
                Assert.Equal(request.Frame.Origin!.SourceId, root.GetProperty("Origin").GetProperty("SourceId").GetGuid());
            }
        }
        finally { await recorder.Completion; Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledOrChangedCollectionDoesNotRecordButReturnsRecognition(bool changeDuringCall)
    {
        var directory = Path.Combine(Path.GetTempPath(), "gt-ocr-gate-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var preferences = Preferences(directory);
        var recorder = new OcrInputDiagnosticRecorder(preferences, Path.Combine(directory, "inputs"));
        var inner = new FakeEngine();
        if (changeDuringCall) inner.BeforeReturn = () =>
        {
            preferences.Save(preferences.Current with { Enabled = false });
            preferences.Save(preferences.Current with { Enabled = true });
        };
        else preferences.Save(preferences.Current with { Frames = false });
        try
        {
            var engine = new DiagnosticOcrEngine(inner, recorder, preferences);
            Assert.Single((await engine.RecognizeAsync(Request(1))).TextBlocks);
            await recorder.Completion;
            Assert.False(Directory.Exists(Path.Combine(directory, "inputs")));
        }
        finally { await recorder.Completion; Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task StorageFailureDoesNotChangeResultOrEscapeRecognition()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gt-ocr-failure-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var preferences = Preferences(directory);
        var blocked = Path.Combine(directory, "blocked");
        File.WriteAllText(blocked, "file instead of directory");
        var recorder = new OcrInputDiagnosticRecorder(preferences, blocked);
        try
        {
            var engine = new DiagnosticOcrEngine(new FakeEngine(), recorder, preferences);
            Assert.Single((await engine.RecognizeAsync(Request(2))).TextBlocks);
            await recorder.Completion;
        }
        finally { await recorder.Completion; Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OversizedInputIsNotRetainedAndCancelledRecognitionDoesNotRecord()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gt-ocr-bound-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var preferences = Preferences(directory);
        var recorder = new OcrInputDiagnosticRecorder(preferences, Path.Combine(directory, "inputs"));
        try
        {
            var frame = new CapturedFrame(new(0, 0, 2049, 2048), 2049, 2048, 2049 * 4, "Bgra32",
                new byte[2049 * 2048 * 4], DateTimeOffset.UtcNow);
            var request = new OcrRequest(frame, "eng");
            recorder.Record(request, new OcrResult(request, [], DateTimeOffset.UtcNow), preferences.CollectionGeneration);
            var engine = new DiagnosticOcrEngine(new FakeEngine(), recorder, preferences);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.RecognizeAsync(Request(1), cancellation.Token));
            await recorder.Completion;
            Assert.False(Directory.Exists(Path.Combine(directory, "inputs")));
        }
        finally { await recorder.Completion; Directory.Delete(directory, true); }
    }

    private static LiveDiagnosticsPreferences Preferences(string directory)
    {
        var preferences = new LiveDiagnosticsPreferences(new JsonSettingsService(Path.Combine(directory, "settings.json")));
        preferences.Save(new(Frames: true));
        preferences.Live = true;
        return preferences;
    }

    private static OcrRequest Request(int id)
    {
        var pixels = Enumerable.Repeat((byte)id, 8 * 4 * 4).ToArray();
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var frame = new CapturedFrame(new(id, 0, 8, 4), 8, 4, 32, "Bgra32", pixels, DateTimeOffset.UtcNow,
            new CaptureFrameOrigin(Guid.NewGuid(), TimeSpan.FromSeconds(id), DateTimeOffset.UtcNow));
        return new(frame, "eng", "sample-" + id);
    }

    private sealed class FakeEngine : IOcrEngine
    {
        public string EngineId => "synthetic";
        internal Action? BeforeReturn;
        public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeReturn?.Invoke();
            return Task.FromResult(new OcrResult(request, [new(request.ZoneId!, new(0, 0, 8, 4))], DateTimeOffset.UtcNow));
        }
    }
}
