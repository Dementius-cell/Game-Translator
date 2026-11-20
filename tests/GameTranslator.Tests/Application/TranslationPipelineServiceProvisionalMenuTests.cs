using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;
using GameTranslator.Application.Translation;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed partial class TranslationPipelineServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProvisionalMenu_MovingBackgroundRetainsCadenceButRequiresFreshConfirmation(bool changedText)
    {
        var zone = CreateZone("cadence", "Menu", new AbsoluteRectangle(0, 0, 400, 160))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var anchor = new BoundingBox(100, 80, 120, 24);
        var padded = new BoundingBox(96, 74, 128, 36);
        var bounds = anchor;
        var text = "Mode 10";
        var capture = new FakeCaptureFrameSource { PixelFactory = (r, n) =>
            Enumerable.Repeat((byte)(25 + n % 25), r.Width * r.Height * 4).ToArray() };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("synthetic",
            [new TextCandidate(bounds, .99)]));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => [new OcrTextBlock(text, new(8, 4, 90, 16))] };
        var clock = new ManualTimeProvider(FrameTime);
        var overlay = new FakeOverlayService();
        var service = CreateService(capture, engine, new FakeTranslatorProvider("Google"), overlay,
            candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone) with
        { OcrSettings = new OcrSettings { OrientationMode = OcrOrientationMode.Horizontal } },
            new TranslationPipelineRunOptions { AllowFreshCachedMenuAssociation = true });
        var events = new List<LiveCandidateLifecycleEvent>();
        async Task Step()
        {
            capture.SourceTimestampOverride = TimeSpan.FromSeconds(clock.GetTimestamp() / (double)clock.TimestampFrequency);
            events.AddRange((await session.RefreshAsync()).CandidateLifecycleEvents);
            events.AddRange((await session.PublishCompletedWorkAsync()).CandidateLifecycleEvents);
            clock.Advance(TimeSpan.FromMilliseconds(100));
        }
        for (var n = 0; n < 30; n++) await Step();
        Assert.Contains(overlay.CurrentSnapshot!.TextItems, t => t.Text == "Translated Mode 10");
        Assert.Contains(events, e => e.Menu is { Decision: "ocr-started", Periodic: true });
        events.Clear();
        bounds = padded;
        if (changedText) text = "Mode 18.";
        await Step();
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
        var proposal = Assert.Single(events, e => e.Menu?.Decision == "association-proposed");
        Assert.Equal(0, proposal.Menu!.ObservationCount);
        Assert.False(proposal.Menu.Confirmed);
        Assert.True(proposal.Menu.Periodic);
        // Continuing motion never provides the 200ms quiet window. A mature track
        // must still recognize and confirm its new generation within two starts.
        for (var n = 0; n < 10; n++)
        {
            await Step();
            if (changedText) Assert.DoesNotContain(overlay.CurrentSnapshot!.TextItems, t => t.Text == "Translated Mode 10");
        }
        Assert.Contains(events, e => e.Menu is { Decision: "association-confirmed", ObservationCount: >= 2 });
        Assert.Contains(overlay.CurrentSnapshot!.TextItems, t => t.Text == "Translated " + text);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ProvisionalMenu_FreshCachedAssociationCanBeRolledBack(bool enabled, bool changedText)
    {
        var zone = CreateZone("cached-proposal", "Menu", new AbsoluteRectangle(0, 0, 400, 160))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var anchor = new BoundingBox(100, 80, 120, 24);
        var padded = new BoundingBox(96, 74, 128, 36);
        var bounds = anchor;
        var text = "Mode 10";
        var capture = new FakeCaptureFrameSource { PixelFactory = (r, _) =>
            Enumerable.Repeat((byte)(bounds == anchor ? 35 : 36), r.Width * r.Height * 4).ToArray() };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("synthetic",
            [new TextCandidate(bounds, .99)]));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => [new OcrTextBlock(text, new(8, 4, 90, 16))] };
        var clock = new ManualTimeProvider(FrameTime);
        var overlay = new FakeOverlayService();
        var service = CreateService(capture, engine, new FakeTranslatorProvider("Google"), overlay,
            candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone) with
        { OcrSettings = new OcrSettings { OrientationMode = OcrOrientationMode.Horizontal } },
            new TranslationPipelineRunOptions { AllowFreshCachedMenuAssociation = enabled });
        var events = new List<LiveCandidateLifecycleEvent>();
        async Task Step(bool fresh = true)
        {
            if (fresh) capture.SourceTimestampOverride = TimeSpan.FromSeconds(clock.GetTimestamp() / (double)clock.TimestampFrequency);
            events.AddRange((await session.RefreshAsync()).CandidateLifecycleEvents);
            events.AddRange((await session.PublishCompletedWorkAsync()).CandidateLifecycleEvents);
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        for (var i = 0; i < 4; i++) await Step();
        Assert.Contains(overlay.CurrentSnapshot!.TextItems, t => t.Text == "Translated Mode 10");
        events.Clear();
        bounds = padded;
        if (changedText) text = "Mode 18.";
        await Step();
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
        Assert.Equal(enabled, events.Any(e => e.Menu?.Decision == "association-proposed"));
        if (enabled)
        {
            foreach (var decision in new[] { "association-proposed", "translation-invalidated-association-proposed" })
            {
                var diagnostic = Assert.Single(events, e => e.Menu?.Decision == decision);
                Assert.Equal(padded, diagnostic.CandidateBounds);
                Assert.Equal(anchor, diagnostic.Menu!.PreviousBounds);
                Assert.Equal(padded, diagnostic.Menu.ProposedBounds);
                Assert.Equal(anchor, diagnostic.Menu.AnchorBounds);
                Assert.Equal(capture.SourceTimestampOverride!.Value.Ticks, diagnostic.Menu.SourceTimestampTicks);
            }
        }
        await Step(false);
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
        Assert.DoesNotContain(events, e => e.Menu?.Decision == "association-confirmed");
        for (var i = 0; i < 10; i++)
        {
            bounds = i % 2 == 0 ? anchor : padded;
            await Step();
            if (changedText) Assert.DoesNotContain(overlay.CurrentSnapshot!.TextItems, t => t.Text == "Translated Mode 10");
        }
        if (enabled)
        {
            Assert.Single(events, e => e.Menu?.Decision == "association-proposed");
            Assert.Contains(events, e => e.Menu?.Decision == "association-confirmed");
            Assert.Contains(overlay.CurrentSnapshot!.TextItems, t => t.Text == "Translated " + text);
        }
        else Assert.DoesNotContain(events, e => e.Menu?.Decision == "association-confirmed");
        clock.Advance(TimeSpan.FromSeconds(3));
        await Step(false);
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
    }

    [Theory]
    [InlineData(OcrOrientationMode.Horizontal, OcrPreprocessingMode.None, WatermarkFilterMode.Off, false, true)]
    [InlineData(OcrOrientationMode.Horizontal, OcrPreprocessingMode.None, WatermarkFilterMode.Off, true, true)]
    [InlineData(OcrOrientationMode.Auto, OcrPreprocessingMode.None, WatermarkFilterMode.Off, false, false)]
    [InlineData(OcrOrientationMode.Vertical, OcrPreprocessingMode.None, WatermarkFilterMode.Off, false, false)]
    [InlineData(OcrOrientationMode.Horizontal, OcrPreprocessingMode.Manual, WatermarkFilterMode.Off, false, false)]
    [InlineData(OcrOrientationMode.Horizontal, OcrPreprocessingMode.None, WatermarkFilterMode.Cautious, false, false)]
    public async Task ProvisionalMenu_JitterConfirmsNewGenerationWithoutInheritingTranslation(
        OcrOrientationMode orientation, OcrPreprocessingMode preprocessing, WatermarkFilterMode watermark, bool sameText, bool enabled)
    {
        var zone = CreateZone("provisional", "Menu", new AbsoluteRectangle(0, 0, 400, 160))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var anchor = new BoundingBox(100, 80, 120, 24);
        var padded = new BoundingBox(96, 74, 128, 36);
        var bounds = anchor;
        var capture = new FakeCaptureFrameSource { PixelFactory = (r, n) =>
            Enumerable.Repeat((byte)(bounds == anchor ? 35 : 36), r.Width * r.Height * 4).ToArray() };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("synthetic",
            [new TextCandidate(bounds, .99)]));
        var text = "Mode 10";
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => [new OcrTextBlock(text, new(8, 4, 90, 16))] };
        var clock = new ManualTimeProvider(FrameTime);
        var overlay = new FakeOverlayService();
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(capture, engine, translator, overlay, candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone) with
        {
            OcrSettings = new OcrSettings { OrientationMode = orientation, WatermarkFilter = watermark },
            OcrPreprocessingSettings = new OcrPreprocessingSettings { Mode = preprocessing },
        }, new TranslationPipelineRunOptions());
        var events = new List<LiveCandidateLifecycleEvent>();
        async Task Step()
        {
            events.AddRange((await session.RefreshAsync()).CandidateLifecycleEvents);
            events.AddRange((await session.PublishCompletedWorkAsync()).CandidateLifecycleEvents);
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        for (var i = 0; i < 4; i++) await Step();
        Assert.Contains(overlay.CurrentSnapshot!.TextItems, t => t.Text == "Translated Mode 10");
        var before = events.Last(e => e.Menu?.Decision == "translation-published").Menu!.Generation;
        events.Clear();
        bounds = padded;
        text = sameText ? "Mode 10" : "Mode 18.";
        await Step();
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
        Assert.Equal(enabled, events.Any(e => e.Menu?.Decision == "association-proposed"));
        if (enabled)
        {
            capture.SourceTimestampOverride = TimeSpan.FromSeconds(2);
            await Step();
            Assert.Empty(overlay.CurrentSnapshot!.TextItems);
            Assert.DoesNotContain(events, e => e.Menu?.Decision == "association-confirmed");
            capture.SourceTimestampOverride = null;
        }
        for (var i = 0; i < 9; i++)
        {
            bounds = i % 2 == 0 ? anchor : padded;
            await Step();
            if (!sameText) Assert.DoesNotContain(overlay.CurrentSnapshot!.TextItems, t => t.Text == "Translated Mode 10");
        }
        if (!enabled)
        {
            Assert.DoesNotContain(events, e => e.Menu?.Decision == "association-confirmed");
            return;
        }
        var proposal = Assert.Single(events, e => e.Menu?.Decision == "association-proposed");
        Assert.NotEqual(before, proposal.Menu!.Generation);
        Assert.False(proposal.Menu.Confirmed);
        Assert.Equal(0, proposal.Menu.ObservationCount);
        Assert.Contains(events, e => e.Menu?.Decision == "association-confirmed" && e.Menu.ObservationCount >= 2);
        Assert.DoesNotContain(events, e => e.Menu?.Decision == "geometry-no-compatible-region");
        Assert.Contains(overlay.CurrentSnapshot!.TextItems, t => t.Text == "Translated " + text);
        // Same confirmed text goes through the ordinary translation cache, not a copied response.
        Assert.Equal(sameText ? 1 : 2, translator.CallCount);
        capture.SourceTimestampOverride = TimeSpan.FromMilliseconds(6000);
        clock.Advance(TimeSpan.FromSeconds(3));
        await Step();
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
    }

    [Fact]
    public async Task ProvisionalMenu_LateOldTranslationCannotPublishAfterAssociation()
    {
        var zone = CreateZone("late-proposal", "Menu", new AbsoluteRectangle(0, 0, 400, 160))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var shifted = false;
        var capture = new FakeCaptureFrameSource { PixelFactory = (r, n) =>
            Enumerable.Repeat((byte)(shifted ? 36 : 35), r.Width * r.Height * 4).ToArray() };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("synthetic",
            [new TextCandidate(shifted ? new(96, 74, 128, 36) : new(100, 80, 120, 24), .99)]));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => [new OcrTextBlock(shifted ? "Mode 18" : "Mode 10", new(8, 4, 90, 16))] };
        var release = new TaskCompletionSource<TranslateResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldRequested = false;
        var translator = new FakeTranslatorProvider("Google", translateAsync: (r, _) =>
        {
            if (r.Texts.Contains("Mode 10")) { oldRequested = true; return release.Task; }
            return Task.FromResult(new TranslateResponse(r.Texts.Select(t => "Translated " + t), TranslatedAt, "Google"));
        });
        var clock = new ManualTimeProvider(FrameTime); var overlay = new FakeOverlayService();
        var service = CreateService(capture, engine, translator, overlay, candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone) with
        { OcrSettings = new OcrSettings { OrientationMode = OcrOrientationMode.Horizontal } }, new TranslationPipelineRunOptions());
        async Task Step()
        {
            await session.RefreshAsync(); await session.PublishCompletedWorkAsync(); clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        try
        {
            for (var i = 0; i < 4; i++) await Step();
            Assert.True(oldRequested);
            shifted = true;
            for (var i = 0; i < 5; i++) await Step();
            Assert.Contains(overlay.CurrentSnapshot!.TextItems, t => t.Text == "Translated Mode 18");
            release.SetResult(new TranslateResponse(["Translated Mode 10"], TranslatedAt, "Google"));
            for (var i = 0; i < 3; i++) await Step();
            Assert.DoesNotContain(overlay.CurrentSnapshot.TextItems, t => t.Text == "Translated Mode 10");
        }
        finally { release.TrySetResult(new TranslateResponse(["Translated Mode 10"], TranslatedAt, "Google")); }
    }

    [Fact]
    public async Task ProvisionalMenu_RetiredOcrCannotConfirmNewGenerationAndNeighborSurvivesRemoval()
    {
        var zone = CreateZone("retired-ocr", "Menu", new AbsoluteRectangle(0, 0, 640, 180))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var shifted = false; var visible = true;
        var capture = new FakeCaptureFrameSource();
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("synthetic",
            (visible ? new[] { new TextCandidate(shifted ? new(96, 74, 128, 36) : new(100, 80, 120, 24), .99) } : [])
                .Concat([new TextCandidate(new(430, 80, 120, 24), .99)])));
        var engine = new BlockingFirstProposalOcr();
        var clock = new ManualTimeProvider(FrameTime); var overlay = new FakeOverlayService();
        var service = CreateService(capture, engine, new FakeTranslatorProvider("Google"), overlay,
            candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone) with
        { OcrSettings = new OcrSettings { OrientationMode = OcrOrientationMode.Horizontal } }, new TranslationPipelineRunOptions());
        var events = new List<LiveCandidateLifecycleEvent>();
        async Task Step()
        {
            events.AddRange((await session.RefreshAsync()).CandidateLifecycleEvents);
            events.AddRange((await session.PublishCompletedWorkAsync()).CandidateLifecycleEvents);
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        try
        {
            await Step(); await Step();
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            shifted = true;
            for (var i = 0; i < 5; i++) await Step();
            Assert.Single(events, e => e.Menu?.Decision == "association-proposed");
            Assert.Contains(overlay.CurrentSnapshot!.TextItems, t => t.Text == "Translated New action");
            Assert.Contains(overlay.CurrentSnapshot.TextItems, t => t.Text == "Translated Neighbor action");
            engine.Release.TrySetResult(true);
            await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(2));
            for (var i = 0; i < 3; i++) await Step();
            var retired = Assert.Single(events, e => e.Menu?.Decision == "ocr-retired-generation").Menu!;
            var proposed = Assert.Single(events, e => e.Menu?.Decision == "association-proposed").Menu!;
            Assert.NotEqual(proposed.Generation, retired.Generation);
            Assert.Equal("retired", retired.QueueBlockReason);
            Assert.False(retired.HadPublishedTranslation);
            Assert.DoesNotContain(overlay.CurrentSnapshot.TextItems, t => t.Text == "Translated Old action");
            visible = false;
            await Step();
            Assert.DoesNotContain(overlay.CurrentSnapshot.TextItems, t => t.Text == "Translated New action");
            Assert.Contains(overlay.CurrentSnapshot.TextItems, t => t.Text == "Translated Neighbor action");
        }
        finally { engine.Release.TrySetResult(true); }
    }

    private sealed class BlockingFirstProposalOcr : IOcrEngine
    {
        private bool blocked;
        public string EngineId => OcrSettings.TesseractEngineId;
        internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            var text = request.Frame.Region.X > 300 ? "Neighbor action" : "New action";
            if (request.Frame.Region.X < 300 && !blocked)
            {
                blocked = true;
                Started.TrySetResult(true);
                await Release.Task; // Intentionally ignores cancellation; still occupies its lease.
                text = "Old action";
            }
            return new OcrResult(request, [new OcrTextBlock(text, new(8, 4, 100, 16))], OcrTime);
        }
    }
}
