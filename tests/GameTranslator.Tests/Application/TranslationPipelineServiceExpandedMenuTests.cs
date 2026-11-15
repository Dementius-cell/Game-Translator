using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;
using GameTranslator.Application.Translation;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed partial class TranslationPipelineServiceTests
{
    [Theory]
    [InlineData(OcrOrientationMode.Horizontal, true, false)]
    [InlineData(OcrOrientationMode.Horizontal, true, true)]
    [InlineData(OcrOrientationMode.Auto, false, false)]
    [InlineData(OcrOrientationMode.Vertical, false, false)]
    public async Task ExpandedMenu_ContextRequiresExplicitHorizontalAndRechecksChangedText(OcrOrientationMode orientation, bool expands, bool lateTranslation)
    {
        var zone = CreateZone("expanded", "Menu", new AbsoluteRectangle(0, 0, 400, 120))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var shifted = false; var changed = false;
        var capture = new FakeCaptureFrameSource { PixelFactory = (_, _) =>
        {
            var bytes = MenuExpandedContextTests.Pixels(1);
            // Weak punctuation is invisible to bright-core geometry but changes the complete OCR input.
            if (changed) for (var y = 50; y < 52; y++)
            {
                var i = (y * 400 + 278) * 4; bytes[i] = bytes[i + 1] = bytes[i + 2] = 150;
            }
            return bytes;
        } };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("synthetic",
            [new TextCandidate(shifted ? MenuExpandedContextTests.Box(1) : new(18, 38, 270, 24), .99)]));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = r => [new OcrTextBlock(changed ? "Value 10." : "Value 10", new(8, 4, 180, 16))] };
        var clock = new ManualTimeProvider(FrameTime); var overlay = new FakeOverlayService();
        var release = new TaskCompletionSource<TranslateResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldRequested = false;
        var translator = new FakeTranslatorProvider("Google", translateAsync: (request, _) =>
        {
            if (lateTranslation && request.Texts.Contains("Value 10"))
            {
                oldRequested = true;
                return release.Task; // Deliberately ignores cancellation.
            }
            return Task.FromResult(new TranslateResponse(request.Texts.Select(t => "Translated " + t), TranslatedAt, "Google"));
        });
        var service = CreateService(capture, engine, translator, overlay,
            candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone) with
        { OcrSettings = new OcrSettings { OrientationMode = orientation } }, new TranslationPipelineRunOptions());
        for (var i = 0; i < 4; i++)
        {
            await session.RefreshAsync(); await session.PublishCompletedWorkAsync(); clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        if (lateTranslation) Assert.True(oldRequested);
        else Assert.NotEmpty(overlay.CurrentSnapshot!.TextItems);
        engine.Requests.Clear(); shifted = true;
        var removed = 0;
        for (var i = 0; i < 4; i++)
        {
            var update = await session.RefreshAsync();
            removed += update.CandidateLifecycleEvents.Count(e => e.Menu?.Decision == "geometry-no-compatible-region");
            await session.PublishCompletedWorkAsync(); clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.NotEmpty(engine.Requests);
        Assert.Equal(expands, engine.Requests.Any(r => r.Frame.Width > 270));
        if (!expands) return;
        Assert.Equal(0, removed);
        if (!lateTranslation) Assert.Contains(overlay.CurrentSnapshot!.TextItems, t => t.Text == "Translated Value 10");
        engine.Requests.Clear(); changed = true;
        for (var i = 0; i < 4; i++)
        {
            await session.RefreshAsync(); await session.PublishCompletedWorkAsync(); clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.NotEmpty(engine.Requests);
        Assert.Contains(overlay.CurrentSnapshot!.TextItems, t => t.Text == "Translated Value 10.");
        Assert.DoesNotContain(overlay.CurrentSnapshot.TextItems, t => t.Text == "Translated Value 10");
        if (lateTranslation)
        {
            release.SetResult(new TranslateResponse(["Translated Value 10"], TranslatedAt, "Google"));
            for (var i = 0; i < 3; i++)
            {
                await session.RefreshAsync(); await session.PublishCompletedWorkAsync();
                clock.Advance(TimeSpan.FromMilliseconds(200));
            }
            Assert.DoesNotContain(overlay.CurrentSnapshot.TextItems, t => t.Text == "Translated Value 10");
        }
    }
}
