using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed partial class TranslationPipelineServiceTests
{
    [Fact]
    public async Task LiveMenu_KeycapBorderNoiseDoesNotTranslateOrDisturbNeighbor()
    {
        var zone = CreateZone("keycap-border", "Menu", new AbsoluteRectangle(0, 0, 240, 90))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var capture = new FakeCaptureFrameSource { PixelFactory = (r, _) =>
        {
            var pixels = new byte[r.Width * r.Height * 4];
            for (var y = 0; y < r.Height; y++)
            for (var x = 0; x < r.Width; x++)
            {
                var light = x >= 12 && x < 40 && y >= 12 && y < 40;
                var glyph = x >= 22 && x < 29 && y >= 18 && y < 34;
                var i = (y * r.Width + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = (byte)(light && !glyph ? 240 : 5);
                pixels[i + 3] = 255;
            }
            return pixels;
        } };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("synthetic",
            [new TextCandidate(new(10, 10, 32, 32), .99), new TextCandidate(new(100, 10, 100, 24), .99)]));
        var count = 0;
        var labels = new[] { "V|", "/U", @"ZZ\" };
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = r => [new OcrTextBlock(r.Frame.Region.X < 50 ? labels[count++ % labels.Length] : "Open map", new(4, 4, 20, 16))] };
        var translator = new FakeTranslatorProvider("Google");
        var overlay = new FakeOverlayService(); var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(capture, engine, translator, overlay, candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone) with
        { OcrSettings = new OcrSettings { OrientationMode = OcrOrientationMode.Horizontal } }, new TranslationPipelineRunOptions());
        var events = new List<LiveCandidateLifecycleEvent>();
        for (var i = 0; i < 10; i++)
        {
            events.AddRange((await session.RefreshAsync()).CandidateLifecycleEvents);
            events.AddRange((await session.PublishCompletedWorkAsync()).CandidateLifecycleEvents);
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal("Translated Open map", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);
        Assert.Equal(1, translator.CallCount);
        foreach (var label in labels) Assert.Contains(events, e => e.OcrTexts.Contains(label));
        Assert.DoesNotContain(events, e => e.Menu?.HadPublishedTranslation == true
            && e.Menu.Decision.StartsWith("translation-invalidated-", StringComparison.Ordinal));
    }
}
