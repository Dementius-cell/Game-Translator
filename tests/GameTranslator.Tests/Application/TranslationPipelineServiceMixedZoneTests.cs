using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed partial class TranslationPipelineServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task M10_MixedZonesKeepHealthyTextAcrossRepeatedFailureAndRecovery(bool captureFailure)
    {
        var left = CreateZone("cycling-menu", "Left", new(0, 0, 100, 120)) with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var right = CreateZone("healthy-menu", "Right", new(200, 0, 100, 120)) with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var dialogue = CreateZone("healthy-dialog", "Dialogue", new(400, 0, 100, 120));
        var fail = false;
        var capture = new FakeCaptureFrameSource { FailureFactory = (r, _) => captureFailure && fail && r.X == 0
            ? new InvalidOperationException("Synthetic zone loss") : null };
        var detector = new FakeCandidateDetector(r => !captureFailure && fail && r.Frame.Region.X == 0
            ? TextCandidateDetectionResult.Unavailable("test", "Synthetic zone loss")
            : TextCandidateDetectionResult.Available("test", r.Frame.Region.X < 400
                ? Enumerable.Range(0, 3).Select(i => new TextCandidate(new(4, 4 + i * 32, 80, 20), .95)).ToArray()
                : [new TextCandidate(new(4, 4, 80, 20), .95)]));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId, BlocksFactory = r =>
            [new OcrTextBlock(r.Frame.Region.X < 100 ? $"Left option {r.Frame.Region.Y}"
                : r.Frame.Region.X < 300 ? $"Right option {r.Frame.Region.Y}" : "A steady dialogue sentence", new(2, 2, 70, 16))] };
        var translator = new FakeTranslatorProvider("Google");
        var overlay = new FakeOverlayService { IsExcludedFromCapture = true };
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(capture, engine, translator, overlay, candidateDetector: detector, timeProvider: clock,
            optimizationOptions: new TranslationPipelineOptimizationOptions());
        var profile = CreateProfile(left, right, dialogue);
        profile = profile with { OcrSettings = profile.OcrSettings with { OrientationMode = OcrOrientationMode.Horizontal } };
        using var session = service.CreateLiveSession(profile, new TranslationPipelineRunOptions());
        for (var tick = 0; tick < 240; tick++)
        {
            fail = tick % 40 is >= 20 and < 24;
            var update = await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            Assert.All(update.CandidateLifecycleEvents.Where(e => e.Menu is not null), e =>
            {
                Assert.InRange(e.Menu!.ActiveOcrCount, 0, 3);
                Assert.InRange(e.Menu.PendingOcrCount, 0, 6);
            });
            if (tick >= 6)
            {
                var items = overlay.CurrentSnapshot!.TextItems;
                Assert.Equal(3, items.Count(i => i.Text.StartsWith("Translated Right option", StringComparison.Ordinal)));
                Assert.Single(items, i => i.Text.Contains("steady dialogue", StringComparison.Ordinal));
                if (fail) Assert.DoesNotContain(items, i => i.Text.StartsWith("Translated Left", StringComparison.Ordinal));
                if (tick % 40 >= 30) Assert.Equal(7, items.Count);
                Assert.Equal(fail, captureFailure
                    ? update.CandidateLifecycleEvents.Any(e => e.ZoneId == left.Id && e.Kind == LiveCandidateLifecycleEventKind.CaptureFailed)
                    : update.BatchResult.ZoneFailures.Any(f => f.ZoneId == left.Id));
            }
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal(7, translator.CallCount); // recovery reuses translations but reconfirms menu text
        var states = (System.Collections.IDictionary)typeof(TranslationPipelineService)
            .GetField("optimizationStates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(service)!;
        Assert.InRange(states.Count, 0, 1); // only the dialogue may occupy the legacy optimizer
    }

    [Fact]
    public async Task M10_SlowRetiredMenuOcrDoesNotStarveOtherMenuOrDialogueOrPublishLate()
    {
        var slow = CreateZone("slow-menu", "Slow", new(0, 0, 100, 120)) with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var healthy = CreateZone("fast-menu", "Fast", new(200, 0, 100, 120)) with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var dialogue = CreateZone("dialogue", "Dialogue", new(400, 0, 100, 120));
        var present = true;
        var detector = new FakeCandidateDetector(r => TextCandidateDetectionResult.Available("test",
            r.Frame.Region.X == 0 && !present ? [] : [new TextCandidate(new(4, 4, 80, 20), .95)]));
        var engine = new MixedZoneDelayedOcr();
        var overlay = new FakeOverlayService { IsExcludedFromCapture = true };
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(new FakeCaptureFrameSource(), engine, new FakeTranslatorProvider("Google"), overlay,
            candidateDetector: detector, timeProvider: clock);
        var profile = CreateProfile(slow, healthy, dialogue);
        profile = profile with { OcrSettings = profile.OcrSettings with { OrientationMode = OcrOrientationMode.Horizontal } };
        using var session = service.CreateLiveSession(profile, new TranslationPipelineRunOptions());
        try
        {
            for (var i = 0; i < 6; i++)
            {
                await session.RefreshAsync(); await session.PublishCompletedWorkAsync();
                clock.Advance(TimeSpan.FromMilliseconds(500));
            }
            Assert.True(engine.Started.Task.IsCompleted);
            Assert.False(engine.Release.Task.IsCompleted);
            Assert.Equal(2, overlay.CurrentSnapshot!.TextItems.Count);
            Assert.Contains(overlay.CurrentSnapshot.TextItems, i => i.TextStyle.LayoutMode == OverlayTextLayoutMode.KeepWithinSourceBounds);
            Assert.Contains(overlay.CurrentSnapshot.TextItems, i => i.TextStyle.LayoutMode == OverlayTextLayoutMode.ExpandFromSourceCenter);
            present = false;
            await session.RefreshAsync();
            Assert.True(engine.SlowCancellation.IsCancellationRequested);
            engine.Release.TrySetResult(true);
            await engine.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
            for (var i = 0; i < 3; i++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(500));
                await session.RefreshAsync(); await session.PublishCompletedWorkAsync();
            }
            Assert.Equal(2, overlay.CurrentSnapshot!.TextItems.Count);
            Assert.DoesNotContain(overlay.Snapshots.SelectMany(s => s.TextItems), i => i.Text.Contains("Retired text", StringComparison.Ordinal));
        }
        finally { engine.Release.TrySetResult(true); }
    }

    private sealed class MixedZoneDelayedOcr : IOcrEngine
    {
        public string EngineId => OcrSettings.TesseractEngineId;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken SlowCancellation { get; private set; }
        public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            var slow = request.Frame.Region.X < 100;
            if (slow)
            {
                SlowCancellation = cancellationToken;
                Started.TrySetResult(true);
                await Release.Task; // noncooperative completion must still be rejected
                Finished.TrySetResult(true);
            }
            return new(request, [new OcrTextBlock(slow ? "Retired text" : "Healthy text", new(2, 2, 70, 16))], OcrTime);
        }
    }
}
