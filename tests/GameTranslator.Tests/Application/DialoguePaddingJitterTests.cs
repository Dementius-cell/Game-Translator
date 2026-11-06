using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed partial class TranslationPipelineServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LiveDialogue_DetectorPaddingChanges_PreservesOnlyUnchangedPixels(int pixelChange)
    {
        var zone = CreateZone("dialogue", "Dialogue", new AbsoluteRectangle(0, 0, 160, 100));
        var original = new BoundingBox(20, 20, 80, 50);
        var expanded = new BoundingBox(19, 20, 81, 50);
        var ink = new BoundingBox(25, 25, 65, 35);
        var frames = Enumerable.Range(0, 6).Select(i =>
            CreateCandidatePilotPixels(zone, (byte)(i + 1), (ink, (byte)90))).ToArray();
        if (pixelChange != 0)
            for (var i = 1; i < frames.Length; i++)
                frames[i][(30 * 160 + (pixelChange == 1 ? 30 : 19)) * 4] = 120;
        var observation = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("synthetic",
            [new TextCandidate(++observation % 2 == 1 ? original : expanded, .95)
            { SourceCandidateBounds = observation % 2 == 1
                ? [new(20, 20, 80, 20), new(22, 50, 60, 20)]
                : [new(19, 20, 81, 20), new(21, 49, 62, 21)] }]));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => [new OcrTextBlock("Synthetic dialogue", new BoundingBox(5, 5, 60, 30))] };
        var overlay = new FakeOverlayService();
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(new FakeCaptureFrameSource { PixelFrames = frames }, engine, translator,
            overlay, candidateDetector: detector);
        using var session = service.CreateLiveSession(CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        Assert.True((await session.RefreshAsync()).OverlayChanged);
        var update = await session.RefreshAsync();
        if (pixelChange != 0)
        {
            Assert.Contains(update.CandidateLifecycleEvents, e => e.Kind == LiveCandidateLifecycleEventKind.CandidateSourceChanged);
            Assert.True(engine.Requests.Count > 1);
        }
        else
        {
            for (var i = 1; i < 6; i++)
            {
                if (i > 1) update = await session.RefreshAsync();
                Assert.False(update.OverlayChanged);
                Assert.DoesNotContain(update.CandidateLifecycleEvents, e =>
                    e.Kind is LiveCandidateLifecycleEventKind.CandidateSourceChanged or LiveCandidateLifecycleEventKind.CandidateGroupingChanged);
            }
            Assert.Single(engine.Requests);
            Assert.Equal(1, translator.CallCount);
            Assert.Single(overlay.CurrentSnapshot!.TextItems);
        }
    }
}
