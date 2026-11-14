using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed partial class TranslationPipelineServiceTests
{
    [Fact]
    public async Task IncrementalMenu_ChangedAreaRunsOcrWhileUnchangedNeighborKeepsTranslation()
    {
        var zone = CreateZone("incremental", "Menu", new AbsoluteRectangle(0, 0, 100, 64))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var changed = false;
        var capture = new FakeCaptureFrameSource { PixelFactory = (r, _) =>
        {
            var pixels = Enumerable.Repeat((byte)42, r.Width * r.Height * 4).ToArray();
            if (changed) pixels[(8 * r.Width + 8) * 4] = 180;
            return pixels;
        } };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("synthetic",
            [new TextCandidate(new(4, 4, 80, 20), .99), new TextCandidate(new(4, 36, 80, 20), .99)]));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId, BlocksFactory = r =>
            [new OcrTextBlock(changed && r.Frame.Region.Y < 30 ? "New action" : "Stable action", new(2, 2, 65, 16))] };
        var clock = new ManualTimeProvider(FrameTime);
        var overlay = new FakeOverlayService();
        var service = CreateService(capture, engine, new FakeTranslatorProvider("Google"), overlay,
            candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone), new TranslationPipelineRunOptions());
        for (var i = 0; i < 8; i++)
        {
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            if (i >= 2) Assert.Equal(2, overlay.CurrentSnapshot!.TextItems.Count);
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        var stableCalls = engine.Requests.Count(r => r.Frame.Region.Y >= 30);
        var changedCalls = engine.Requests.Count(r => r.Frame.Region.Y < 30);
        Assert.InRange(stableCalls, 2, 3);
        changed = true;
        for (var i = 0; i < 3; i++)
        {
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            Assert.Contains(overlay.CurrentSnapshot!.TextItems, t => t.Y >= 36 && t.Text == "Translated Stable action");
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal(stableCalls, engine.Requests.Count(r => r.Frame.Region.Y >= 30));
        Assert.True(engine.Requests.Count(r => r.Frame.Region.Y < 30) > changedCalls);
        Assert.Contains(overlay.CurrentSnapshot!.TextItems, t => t.Y < 30 && t.Text == "Translated New action");
    }

    [Theory]
    [InlineData("Corrected action")]
    [InlineData("")]
    public async Task IncrementalMenu_StaticAreaSkipsOcrButPeriodicAuditCanRevokeWrongText(string correction)
    {
        var zone = CreateZone("audit", "Menu", new AbsoluteRectangle(0, 0, 100, 32))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var text = "Initial action";
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("synthetic",
            [new TextCandidate(new(4, 4, 80, 20), .99)]));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => text.Length == 0 ? [] : [new OcrTextBlock(text, new(2, 2, 65, 16))] };
        var clock = new ManualTimeProvider(FrameTime);
        var overlay = new FakeOverlayService();
        var service = CreateService(new FakeCaptureFrameSource(), engine, new FakeTranslatorProvider("Google"), overlay,
            candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone), new TranslationPipelineRunOptions());
        var renewals = 0;
        for (var i = 0; i < 24; i++)
        {
            var update = await session.RefreshAsync();
            renewals += update.CandidateLifecycleEvents.Count(e => e.Menu?.Decision == "visual-source-renewed");
            await session.PublishCompletedWorkAsync();
            if (i >= 2) Assert.Single(overlay.CurrentSnapshot!.TextItems);
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.True(renewals >= 12);
        Assert.InRange(engine.Requests.Count, 4, 8); // 24 refreshes, including repeated independent audits.
        text = correction; // Simulate correcting a stable OCR error on the same pixels.
        for (var i = 0; i < 14; i++)
        {
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        if (correction.Length == 0) Assert.Empty(overlay.CurrentSnapshot!.TextItems);
        else Assert.Equal("Translated Corrected action", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);
    }
}
