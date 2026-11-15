using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed partial class TranslationPipelineServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncrementalDetection_OnlyChangedAreaIsScannedAndNeighborSurvives(bool disappears)
    {
        var zone = CreateZone("local-detector", "Menu", new AbsoluteRectangle(0, 0, 1024, 512))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var changed = false;
        var left = new BoundingBox(100, 100, 120, 24);
        var right = new BoundingBox(700, 100, 120, 24);
        var sent = new List<CaptureRegion>();
        var detector = new FakeCandidateDetector(r =>
        {
            sent.Add(r.Frame.Region);
            var boxes = changed && disappears ? new[] { right } : new[] { left, right };
            return TextCandidateDetectionResult.Available("synthetic", boxes
                .Where(b => b.X >= r.Frame.Region.X && b.Y >= r.Frame.Region.Y
                    && b.Right <= r.Frame.Region.X + r.Frame.Width && b.Bottom <= r.Frame.Region.Y + r.Frame.Height)
                .Select(b => new TextCandidate(new(b.X - r.Frame.Region.X, b.Y - r.Frame.Region.Y, b.Width, b.Height), .99)));
        });
        var capture = new FakeCaptureFrameSource { PixelFactory = (r, _) =>
        {
            var pixels = Enumerable.Repeat((byte)42, r.Width * r.Height * 4).ToArray();
            if (changed) pixels[(110 * r.Width + 110) * 4] = 180;
            return pixels;
        } };
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = r => [new OcrTextBlock(changed && r.Frame.Region.X < 500 ? "Updated label" : "Stable label", new(4, 2, 100, 18))] };
        var clock = new ManualTimeProvider(FrameTime);
        var overlay = new FakeOverlayService();
        var service = CreateService(capture, engine, new FakeTranslatorProvider("Google"), overlay,
            candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone) with
        { OcrSettings = new OcrSettings { OrientationMode = OcrOrientationMode.Auto } }, new TranslationPipelineRunOptions
        { EnableIncrementalMenuDetection = true });
        var reused = 0;
        for (var i = 0; i < 4; i++)
        {
            var result = await session.RefreshAsync();
            reused += result.CandidateLifecycleEvents.Count(e => e.RegionRefresh?.Reason == "unchanged");
            await session.PublishCompletedWorkAsync();
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.True(reused > 0);
        Assert.Equal(2, overlay.CurrentSnapshot!.TextItems.Count);
        var rightCalls = engine.Requests.Count(r => r.Frame.Region.X >= 500);
        sent.Clear();
        capture.SourceTimestampOverride = TimeSpan.FromMilliseconds(1500);
        var replay = await session.RefreshAsync();
        var replayScope = Assert.Single(replay.CandidateLifecycleEvents, e => e.RegionRefresh is not null).RegionRefresh!;
        Assert.Equal("cached-source-reused", replayScope.Reason);
        Assert.Equal("available", replayScope.BaselineState);
        Assert.Equal("available", replayScope.BaselineAfter);
        Assert.True(replayScope.CachedReuse);
        Assert.Equal(500, replayScope.SourceAgeMs);
        Assert.Equal(0, replayScope.DetectorCalls);
        Assert.Empty(sent);
        Assert.DoesNotContain(replay.CandidateLifecycleEvents, e => e.Menu?.Decision == "visual-source-renewed");

        capture.SourceTimestampOverride = TimeSpan.FromMilliseconds(1600);
        var unchangedCached = await session.RefreshAsync();
        var unchangedScope = Assert.Single(unchangedCached.CandidateLifecycleEvents, e => e.RegionRefresh is not null).RegionRefresh!;
        Assert.Equal("unchanged", unchangedScope.Reason);
        Assert.Equal(0, unchangedScope.DetectorCalls);
        Assert.True(unchangedScope.CachedReuse);
        Assert.Equal("available", unchangedScope.BaselineAfter);
        Assert.Empty(sent);
        Assert.DoesNotContain(unchangedCached.CandidateLifecycleEvents, e => e.Menu?.Decision == "visual-source-renewed");

        changed = true;
        capture.SourceTimestampOverride = TimeSpan.FromMilliseconds(1750);
        var newCached = await session.RefreshAsync();
        var verified = Assert.Single(newCached.CandidateLifecycleEvents, e => e.RegionRefresh is not null).RegionRefresh!;
        Assert.Equal("local-change", verified.Reason);
        Assert.Equal(1, verified.DetectorCalls);
        Assert.Equal(1, verified.ChangedTiles);
        Assert.True(verified.CachedReuse);
        Assert.Equal(1, verified.RetainedCandidates);
        Assert.True(verified.ProcessedPixels < 1024 * 512 / 4);
        Assert.Equal("available", verified.BaselineAfter);
        Assert.True(Assert.Single(sent).Width < 512);
        Assert.DoesNotContain(newCached.CandidateLifecycleEvents, e => e.Menu?.Decision == "visual-source-renewed");
        await session.PublishCompletedWorkAsync();
        rightCalls = engine.Requests.Count(r => r.Frame.Region.X >= 500);
        sent.Clear();
        var cachedAgain = await session.RefreshAsync();
        Assert.Equal("cached-source-reused", Assert.Single(cachedAgain.CandidateLifecycleEvents, e => e.RegionRefresh is not null).RegionRefresh!.Reason);
        Assert.Empty(sent);
        Assert.DoesNotContain(cachedAgain.CandidateLifecycleEvents, e => e.Menu?.Decision == "visual-source-renewed");
        capture.SourceTimestampOverride = null;
        changed = false;
        var local = await session.RefreshAsync();
        var scope = Assert.Single(local.CandidateLifecycleEvents, e => e.RegionRefresh is not null).RegionRefresh!;
        Assert.False(scope.FullScan);
        Assert.Equal(1, scope.DetectorCalls);
        Assert.Equal(1, scope.RetainedCandidates);
        Assert.True(scope.ProcessedPixels < 1024 * 512 / 4);
        Assert.True(Assert.Single(sent).Width < 512);
        clock.Advance(TimeSpan.FromMilliseconds(250));
        await session.PublishCompletedWorkAsync();
        Assert.Contains(overlay.CurrentSnapshot!.TextItems, t => t.X >= 700 && t.Text == "Translated Stable label");
        if (disappears) Assert.Single(overlay.CurrentSnapshot.TextItems);
        Assert.Equal(rightCalls, engine.Requests.Count(r => r.Frame.Region.X >= 500));
        changed = true;
        for (var i = 0; i < 3; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(500));
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
        }
        if (!disappears) Assert.Contains(overlay.CurrentSnapshot.TextItems, t => t.Text == "Translated Updated label");
        else Assert.Single(overlay.CurrentSnapshot.TextItems);
    }

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
