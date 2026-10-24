using System.IO;
using System.Net;
using GameTranslator.Application.Cache;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Credentials;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Pipeline;
using GameTranslator.Application.Translation;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class TranslationPipelineServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveMenu_DisposedSessionCannotRestoreOverlayAfterDelayedCapture(bool sourceFailsLate)
    {
        var zone = CreateZone("capture-session", "Menu", new AbsoluteRectangle(0, 0, 60, 24))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var text = "Power 7";
        var failCapture = false;
        var capture = new FakeCaptureFrameSource { FailureFactory = (_, _) => failCapture
            ? new CaptureFrameSourceException("Synthetic late failure") : null };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test",
            [new TextCandidate(new BoundingBox(0, 0, 60, 24), .95)]));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => [new OcrTextBlock(text, new BoundingBox(4, 3, 48, 18))] };
        var overlay = new FakeOverlayService();
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(capture, engine, new FakeTranslatorProvider("Google"), overlay,
            candidateDetector: detector, timeProvider: clock);
        var profile = CreateProfile(zone);
        var options = new TranslationPipelineRunOptions(restorePreviousOverlayAfterCapture: true);
        using var old = service.CreateLiveSession(profile, options);
        for (var i = 0; i < 3; i++)
        {
            await old.RefreshAsync();
            await old.PublishCompletedWorkAsync();
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal("Translated Power 7", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.CaptureGate = gate.Task;
        failCapture = sourceFailsLate;
        var pendingCapture = old.RefreshAsync();
        Assert.False(pendingCapture.IsCompleted);
        Assert.False(overlay.IsVisible);
        old.Dispose();
        capture.CaptureGate = null;
        failCapture = false;
        text = "Power 8";
        using var current = service.CreateLiveSession(profile, options);
        for (var i = 0; i < 3; i++)
        {
            await current.RefreshAsync();
            await current.PublishCompletedWorkAsync();
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal("Translated Power 8", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);
        var historyStart = overlay.Snapshots.Count;
        gate.SetResult(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingCapture);
        Assert.Equal(historyStart, overlay.Snapshots.Count);
        Assert.Equal("Translated Power 8", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);
    }

    [Fact]
    public async Task LiveSession_FailedZoneCancelsOnlyItsPendingTranslation()
    {
        var failed = CreateZone("failed-pending", "Failed", new AbsoluteRectangle(0, 0, 60, 24))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var healthy = failed with { Id = "healthy-pending", AbsoluteBounds = new AbsoluteRectangle(200, 0, 60, 24) };
        var fail = false;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test",
            [new TextCandidate(new BoundingBox(0, 0, 60, 24), .95)]));
        var capture = new FakeCaptureFrameSource { FailureFactory = (region, _) => fail && region.X == 0
            ? new CaptureFrameSourceException("Synthetic zone failure") : null };
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = r => [new OcrTextBlock(r.Frame.Region.X < 100 ? "Failed" : "Healthy", new BoundingBox(4, 3, 48, 18))] };
        var failedResponse = new TaskCompletionSource<TranslateResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var healthyResponse = new TaskCompletionSource<TranslateResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken failedToken = default, healthyToken = default;
        var translator = new FakeTranslatorProvider("Google", translateAsync: (request, token) =>
        {
            if (request.Texts.Single() == "Failed") { failedToken = token; return failedResponse.Task; }
            healthyToken = token;
            return healthyResponse.Task;
        });
        var overlay = new FakeOverlayService();
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(capture, engine, translator, overlay, candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(failed, healthy), new TranslationPipelineRunOptions());
        for (var i = 0; i < 3; i++)
        {
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal(2, translator.CallCount);
        fail = true;
        var update = await session.RefreshAsync();
        Assert.True(failedToken.IsCancellationRequested);
        Assert.False(healthyToken.IsCancellationRequested);
        Assert.Contains(update.CandidateLifecycleEvents, e => e.Kind == LiveCandidateLifecycleEventKind.CaptureFailed && e.ZoneId == failed.Id);
        Assert.DoesNotContain(update.CancelledZoneIds, id => id.StartsWith(healthy.Id, StringComparison.Ordinal));
        healthyResponse.SetResult(new TranslateResponse(["Translated Healthy"], TranslatedAt, "Google"));
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await session.PublishCompletedWorkAsync();
        Assert.Equal("Translated Healthy", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);
        await session.PublishCompletedWorkAsync();
        failedResponse.SetResult(new TranslateResponse(["Translated Failed"], TranslatedAt, "Google"));
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await session.PublishCompletedWorkAsync();
        Assert.Equal("Translated Healthy", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveMenu_NewSessionWithChangedSettingsCannotPublishOldWork(bool delayedOcr)
    {
        var zone = CreateZone("session-menu", "Menu", new AbsoluteRectangle(0, 0, 60, 24))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var text = "Power 7";
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test",
            [new TextCandidate(new BoundingBox(0, 0, 60, 24), .95)]));
        var blockedOcr = new NonCooperativeMenuOcrEngine();
        IOcrEngine engine = delayedOcr ? blockedOcr : new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => [new OcrTextBlock(text, new BoundingBox(4, 3, 48, 18))] };
        var oldResponse = new TaskCompletionSource<TranslateResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldProviderFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var translator = new FakeTranslatorProvider("Google", translateAsync: async (request, _) =>
        {
            if (request.TargetLanguage == "ru")
            {
                var response = await oldResponse.Task;
                oldProviderFinished.TrySetResult(true);
                return response;
            }
            return new TranslateResponse(["New Power 8"], TranslatedAt, "Google");
        });
        var overlay = new FakeOverlayService();
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(new FakeCaptureFrameSource(), engine, translator, overlay,
            candidateDetector: detector, timeProvider: clock);
        var profile = CreateProfile(zone);
        using var oldSession = service.CreateLiveSession(profile, new TranslationPipelineRunOptions());
        for (var i = 0; i < 3; i++)
        {
            await oldSession.RefreshAsync();
            await oldSession.PublishCompletedWorkAsync();
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal(delayedOcr ? 0 : 1, translator.CallCount);
        oldSession.Dispose();
        text = "Power 8";
        var newProfile = profile with { TranslatorSettings = profile.TranslatorSettings with { TargetLanguage = "de" } };
        using var current = service.CreateLiveSession(newProfile, new TranslationPipelineRunOptions());
        for (var i = 0; i < 3; i++)
        {
            await current.RefreshAsync();
            await current.PublishCompletedWorkAsync();
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal("New Power 8", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);
        if (delayedOcr) blockedOcr.Release.SetResult(true);
        else
        {
            oldResponse.SetResult(new TranslateResponse(["Old Power 7"], TranslatedAt, "Google"));
            await oldProviderFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        for (var i = 0; i < 3; i++)
        {
            await current.RefreshAsync();
            await current.PublishCompletedWorkAsync();
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal(delayedOcr ? 1 : 2, translator.CallCount);
        Assert.All(overlay.Snapshots, snapshot => Assert.DoesNotContain(snapshot.TextItems, item => item.Text == "Old Power 7"));
        Assert.Equal("New Power 8", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => oldSession.PublishCompletedWorkAsync());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task LiveSession_OneCaptureFailureKeepsOtherZoneAndRecoversWithFreshConfirmation(bool failedFirst, bool healthyMenu)
    {
        var failed = CreateZone("failed-capture", "Failed", new AbsoluteRectangle(0, 0, 60, 24))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var healthy = CreateZone("healthy-capture", "Healthy", new AbsoluteRectangle(200, 0, 60, 24))
            with { ContentLayoutMode = healthyMenu ? ContentLayoutMode.StaticMenu : ContentLayoutMode.DialogComic };
        var fail = false;
        var capture = new FakeCaptureFrameSource { FailureFactory = (region, _) => fail && region.X == 0
            ? new CaptureFrameSourceException("Synthetic zone failure") : null };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test",
            [new TextCandidate(new BoundingBox(0, 0, 60, 24), .95)]));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = r => [new OcrTextBlock(r.Frame.Region.X < 100 ? "Failed action" : "Healthy action", new BoundingBox(4, 3, 48, 18))] };
        var overlay = new FakeOverlayService();
        var translator = new FakeTranslatorProvider("Google");
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(capture, engine, translator, overlay, candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(failedFirst ? [failed, healthy] : [healthy, failed]),
            new TranslationPipelineRunOptions(restorePreviousOverlayAfterCapture: true));
        for (var i = 0; i < 3; i++)
        {
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal(2, overlay.CurrentSnapshot!.TextItems.Count);
        var historyStart = overlay.Snapshots.Count;
        var beforeFailureCaptures = capture.CapturedRegions.Count;
        fail = true;
        var lost = await session.RefreshAsync();
        Assert.Equal(2, capture.CapturedRegions.Count - beforeFailureCaptures);
        Assert.Equal("Translated Healthy action", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);
        Assert.DoesNotContain(lost.CandidateLifecycleEvents, e => e.ZoneId == healthy.Id
            && e.Kind is LiveCandidateLifecycleEventKind.CandidateRemoved or LiveCandidateLifecycleEventKind.CandidateWorkCancelled);
        Assert.All(overlay.Snapshots.Skip(historyStart), snapshot =>
            Assert.DoesNotContain(snapshot.TextItems, item => item.Text == "Translated Failed action"));
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await session.RefreshAsync();
        Assert.Equal("Translated Healthy action", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);
        fail = false;
        clock.Advance(TimeSpan.FromMilliseconds(500));
        var recovering = await session.RefreshAsync();
        Assert.DoesNotContain(recovering.CandidateLifecycleEvents, e => e.ZoneId == healthy.Id && e.Kind == LiveCandidateLifecycleEventKind.CandidateDiscovered);
        Assert.Single(overlay.CurrentSnapshot!.TextItems);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await session.RefreshAsync();
        await session.PublishCompletedWorkAsync();
        Assert.Single(overlay.CurrentSnapshot!.TextItems);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await session.RefreshAsync();
        await session.PublishCompletedWorkAsync();
        Assert.Equal(2, overlay.CurrentSnapshot!.TextItems.Count);
        Assert.Equal(2, translator.CallCount);
    }

    [Fact]
    public async Task LiveMenu_ConfirmedSmallDriftMovesOverlayWithoutRetranslation()
    {
        var zone = CreateZone("small-drift", "Menu", new AbsoluteRectangle(0, 0, 120, 40))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var x = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test",
            new[] { new TextCandidate(new BoundingBox(x, 0, 100, 40), .95) }));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => [new OcrTextBlock("Action", new BoundingBox(5, 5, 80, 30))] };
        var overlay = new FakeOverlayService();
        var translator = new FakeTranslatorProvider("Google");
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(new FakeCaptureFrameSource(), engine, translator, overlay,
            candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone), new TranslationPipelineRunOptions());
        for (var i = 0; i < 3; i++)
        {
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        var oldX = Assert.Single(overlay.CurrentSnapshot!.TextItems).X;
        x = 1;
        for (var i = 0; i < 2; i++)
        {
            var update = await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            Assert.DoesNotContain(update.CandidateLifecycleEvents, e => e.Kind == LiveCandidateLifecycleEventKind.CandidateRemoved);
            Assert.Single(overlay.CurrentSnapshot!.TextItems);
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal(oldX + 1, Assert.Single(overlay.CurrentSnapshot!.TextItems).X);
        Assert.Equal(1, translator.CallCount);
        Assert.Equal(2, overlay.Events.Count(e => e.StartsWith("Show:", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task LiveMenu_LargeGeometryChangeRequiresNewConfirmationBeforeCacheReuse(int scale)
    {
        var zone = CreateZone("geometry-menu", "Menu", new AbsoluteRectangle(0, 0, 100 * scale, 24 * scale))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var x = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test",
            new[] { new TextCandidate(new BoundingBox(x, 0, 60 * scale, 24 * scale), .95) }));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => [new OcrTextBlock("Action", new BoundingBox(4 * scale, 3 * scale, 48 * scale, 18 * scale))] };
        var overlay = new FakeOverlayService();
        var translator = new FakeTranslatorProvider("Google");
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(new FakeCaptureFrameSource(), engine, translator, overlay,
            candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone), new TranslationPipelineRunOptions());
        for (var i = 0; i < 3; i++)
        {
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        var oldX = Assert.Single(overlay.CurrentSnapshot!.TextItems).X;
        x = 20 * scale;
        await session.RefreshAsync();
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await session.RefreshAsync();
        await session.PublishCompletedWorkAsync();
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await session.RefreshAsync();
        await session.PublishCompletedWorkAsync();
        Assert.Equal(oldX + 20 * scale, Assert.Single(overlay.CurrentSnapshot!.TextItems).X);
        Assert.Equal(1, translator.CallCount);
    }

    [Fact]
    public async Task LiveMenu_SimilarNeighborKeepsItsAuthorityWhenOneItemChanges()
    {
        var zone = CreateZone("neighbor-menu", "Menu", new AbsoluteRectangle(0, 0, 60, 44))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var changed = false;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test",
            new[] { new TextCandidate(new BoundingBox(0, 0, 60, 16), .95), new TextCandidate(new BoundingBox(0, 24, 60, 16), .95) }));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = r => [new OcrTextBlock(changed && r.Frame.Region.Y == 0 ? "Action 8" : "Action 7", new BoundingBox(4, 2, 48, 12))] };
        var overlay = new FakeOverlayService();
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(new FakeCaptureFrameSource(), engine, new FakeTranslatorProvider("Google"), overlay,
            candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone), new TranslationPipelineRunOptions());
        for (var i = 0; i < 3; i++)
        {
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal(2, overlay.CurrentSnapshot!.TextItems.Count);
        changed = true;
        await session.RefreshAsync();
        await session.PublishCompletedWorkAsync();
        var remaining = Assert.Single(overlay.CurrentSnapshot!.TextItems);
        Assert.Equal("Translated Action 7", remaining.Text);
        Assert.True(remaining.Y >= 24);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await session.RefreshAsync();
        await session.PublishCompletedWorkAsync();
        Assert.Equal(2, overlay.CurrentSnapshot!.TextItems.Count);
        Assert.Contains(overlay.CurrentSnapshot.TextItems, item => item.Text == "Translated Action 8" && item.Y < 24);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveMenu_LateOcrIsRejectedForExpiredSourceOrRetiredGeneration(bool retire)
    {
        var zone = CreateZone("late-ocr", "Menu", new AbsoluteRectangle(0, 0, 60, 24))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var visible = true;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test", visible
            ? new[] { new TextCandidate(new BoundingBox(0, 0, 60, 24), .95) } : []));
        var engine = new NonCooperativeMenuOcrEngine();
        var overlay = new FakeOverlayService();
        var translator = new FakeTranslatorProvider("Google");
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(new FakeCaptureFrameSource(), engine, translator, overlay,
            candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone), new TranslationPipelineRunOptions());
        await session.RefreshAsync();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await session.RefreshAsync();
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (retire)
        {
            visible = false;
            clock.Advance(TimeSpan.FromMilliseconds(500));
            await session.RefreshAsync();
            visible = true;
            for (var i = 0; i < 3; i++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(500));
                await session.RefreshAsync();
                await session.PublishCompletedWorkAsync();
            }
            Assert.Contains(overlay.CurrentSnapshot!.TextItems, item => item.Text == "Translated Power 8");
        }
        else clock.Advance(TimeSpan.FromSeconds(3));
        await session.PublishCompletedWorkAsync();
        engine.Release.SetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(2));
        var update = await session.PublishCompletedWorkAsync();
        Assert.Contains(update.CandidateLifecycleEvents, e => e.Menu?.Decision ==
            (retire ? "ocr-retired-generation" : "ocr-rejected-source"));
        Assert.DoesNotContain(overlay.CurrentSnapshot?.TextItems ?? [], item => item.Text == "Translated Power 7");
        Assert.Equal(retire ? 1 : 0, translator.CallCount);
    }

    private sealed class NonCooperativeMenuOcrEngine : IOcrEngine
    {
        private int calls;
        public string EngineId => OcrSettings.TesseractEngineId;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            var first = Interlocked.Increment(ref calls) == 1;
            if (first) { Started.TrySetResult(true); await Release.Task; }
            return new OcrResult(request, [new OcrTextBlock(first ? "Power 7" : "Power 8", new BoundingBox(4, 3, 48, 18))], OcrTime);
        }
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("empty")]
    [InlineData("expired")]
    [InlineData("disappeared")]
    [InlineData("capture-lost")]
    public async Task LiveMenu_LateNonCooperativeTranslationCannotRegainAuthority(string invalidation)
    {
        var zone = CreateZone("late-menu", "Menu", new AbsoluteRectangle(0, 0, 60, 24))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var text = "Power 7";
        var visible = true;
        var captureLost = false;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test", visible
            ? new[] { new TextCandidate(new BoundingBox(0, 0, 60, 24), .95) } : []));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => text.Length == 0 ? [] : new[] { new OcrTextBlock(text, new BoundingBox(4, 3, 48, 18)) } };
        var oldResponse = new TaskCompletionSource<TranslateResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        var translator = new FakeTranslatorProvider("Google", translateAsync: (request, token) =>
        {
            if (request.Texts.Single() == "Power 7") { oldToken = token; return oldResponse.Task; }
            return Task.FromResult(new TranslateResponse(["Translated Power 8"], TranslatedAt, "Google"));
        });
        var capture = new FakeCaptureFrameSource { FailureFactory = (_, _) => captureLost
            ? new CaptureFrameSourceException("Synthetic loss") : null };
        var overlay = new FakeOverlayService();
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(capture, engine, translator, overlay, candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone), new TranslationPipelineRunOptions());
        for (var i = 0; i < 3; i++)
        {
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal(1, translator.CallCount);
        switch (invalidation)
        {
            case "changed": text = "Power 8"; break;
            case "empty": text = ""; break;
            case "disappeared": visible = false; break;
            case "capture-lost": captureLost = true; break;
            case "expired": clock.Advance(TimeSpan.FromSeconds(2)); break;
        }
        if (invalidation == "expired") await session.PublishCompletedWorkAsync();
        else await session.RefreshAsync();
        Assert.True(oldToken.IsCancellationRequested);
        Assert.True(overlay.CurrentSnapshot?.TextItems.Count is null or 0);
        if (invalidation == "changed")
        {
            clock.Advance(TimeSpan.FromMilliseconds(500));
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            Assert.Contains(overlay.CurrentSnapshot!.TextItems, item => item.Text == "Translated Power 8");
        }
        await session.PublishCompletedWorkAsync();
        oldResponse.SetResult(new TranslateResponse(["Translated Power 7"], TranslatedAt, "Google"));
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await session.PublishCompletedWorkAsync();
        Assert.DoesNotContain(overlay.CurrentSnapshot?.TextItems ?? [], item => item.Text == "Translated Power 7");
        Assert.Equal(invalidation == "changed" ? 2 : 1, translator.CallCount);
    }

    [Fact]
    public async Task LiveMenu_StaticFreshFramesRenewWithoutRetranslation_CachedFrameExpires()
    {
        var zone = CreateZone("static-menu", "Menu", new AbsoluteRectangle(0, 0, 60, 24))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test",
            new[] { new TextCandidate(new BoundingBox(0, 0, 60, 24), .95) }));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => [new OcrTextBlock("Action", new BoundingBox(4, 3, 48, 18))] };
        var capture = new FakeCaptureFrameSource();
        var translator = new FakeTranslatorProvider("Google");
        var overlay = new FakeOverlayService();
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(capture, engine, translator, overlay, candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone), new TranslationPipelineRunOptions());
        for (var i = 0; i < 24; i++)
        {
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            if (i >= 2) Assert.Single(overlay.CurrentSnapshot!.TextItems);
            if (i < 23) clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal(1, translator.CallCount);
        capture.SourceTimestampOverride = TimeSpan.FromMilliseconds(11500);
        Assert.Single(overlay.Events, e => e.StartsWith("Show:", StringComparison.Ordinal));
        // Drain the distinct final frame that was legitimately waiting behind the previous OCR.
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await session.PublishCompletedWorkAsync();
        var count = engine.Requests.Count;
        for (var i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(500));
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
        }
        Assert.Equal(count, engine.Requests.Count);
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
        Assert.Equal(1, translator.CallCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task LiveMenu_TexturedMotionAndFrameDrift_ConfirmsUnchangedText(int scale)
    {
        const int count = 16;
        var zone = CreateZone("texture", "Texture", new AbsoluteRectangle(0, 0, 80 * scale, 32 * scale))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var frames = Enumerable.Range(0, count).Select(phase =>
        {
            var pixels = new byte[80 * 32 * scale * scale * 4];
            for (var y = 0; y < 32 * scale; y++)
            for (var x = 0; x < 80 * scale; x++)
            {
                var xx = x / scale;
                var yy = y / scale;
                var ink = yy is >= 8 and <= 23 && xx is >= 10 and <= 55 && xx % 10 < 3;
                var value = (byte)(ink ? 240 : 20 + ((xx * 17 + yy * 31 + phase * 43) % 150));
                var offset = (y * 80 * scale + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
            return pixels;
        }).ToArray();
        var captures = new FakeCaptureFrameSource { PixelFrames = frames,
            CapturedAtFrames = Enumerable.Range(0, count).Select(i => FrameTime.AddMilliseconds(i * 500)).ToArray() };
        var detectionIndex = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test",
            new[] { new TextCandidate(new BoundingBox(4 * scale, 4 * scale,
                (60 + detectionIndex++ % 2) * scale, 24 * scale), .95) }));
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            RecognizedAtFrames = Enumerable.Range(0, count).Select(i => FrameTime.AddMilliseconds(i * 500)).ToArray(),
            BlocksFactory = _ => new[] { new OcrTextBlock("Synthetic action", new BoundingBox(6 * scale, 4 * scale, 46 * scale, 16 * scale)) } };
        var translator = new FakeTranslatorProvider("Google");
        var overlay = new FakeOverlayService();
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(captures, engine, translator, overlay, candidateDetector: detector, timeProvider: clock);
        var options = new TranslationPipelineRunOptions(requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.FromMilliseconds(200), minimumStableTextObservations: 2,
            minimumCandidateGroupingObservations: 1) { MinimumCandidateGroupingDuration = TimeSpan.Zero };
        using var session = service.CreateLiveSession(CreateProfile(zone), options);
        for (var i = 0; i < count; i++)
        {
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.True(engine.Requests.Count >= 2, "The sequence must exercise independent OCR observations.");
        Assert.Equal(1, translator.CallCount);
        Assert.Contains(overlay.CurrentSnapshot!.TextItems, t => t.Text == "Translated Synthetic action");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task LiveMenu_PendingOcrSurvivesPixelChangesUntilTextIsObserved(bool menu, bool strokeChanged)
    {
        var zone = CreateZone("pending", "Pending", new AbsoluteRectangle(0, 0, 60, 24))
            with { ContentLayoutMode = menu ? ContentLayoutMode.StaticMenu : ContentLayoutMode.DialogComic };
        var capture = new FakeCaptureFrameSource { PixelFrames = Enumerable.Range(0, 10).Select(i =>
            MenuTextStructureTests.Pixels(1, i < 2 ? 25 : 45, i >= 2 && strokeChanged)).ToArray() };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test",
            new[] { new TextCandidate(new BoundingBox(0, 0, 60, 24), .95) }));
        var engine = new DelayedMenuOcrEngine();
        var overlay = new FakeOverlayService();
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(capture, engine, new FakeTranslatorProvider("Google"), overlay,
            candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone), new TranslationPipelineRunOptions());
        await session.RefreshAsync();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await session.RefreshAsync();
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromMilliseconds(500));
        var update = await session.RefreshAsync();
        Assert.Equal(!menu, update.CandidateLifecycleEvents.Any(e => e.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCancelled));
        Assert.True(overlay.CurrentSnapshot?.TextItems.Count is null or 0);
        engine.Release.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await session.PublishCompletedWorkAsync();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(500));
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
        }
        for (var attempt = 0; attempt < 3 && overlay.CurrentSnapshot?.TextItems.Count is not > 0; attempt++)
        {
            await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(2));
            await session.PublishCompletedWorkAsync();
        }
        Assert.Contains(overlay.CurrentSnapshot!.TextItems,
            t => t.Text == (strokeChanged ? "Translated Power 8" : "Translated Power 7"));
    }

    private sealed class DelayedMenuOcrEngine : IOcrEngine
    {
        public string EngineId => OcrSettings.TesseractEngineId;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            var text = request.Frame.PixelData.Span[(10 * 60 + 11) * 4] == 240 ? "Power 7" : "Power 8";
            return new OcrResult(request, new[] { new OcrTextBlock(text, new BoundingBox(4, 3, 48, 18)) }, OcrTime);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LiveMenu_BackgroundMotionKeepsTextConfirmationButStrokeChangeAndRemovalInvalidate(bool visibleStrokeChange)
    {
        var zone = CreateZone("menu", "Menu", new AbsoluteRectangle(0, 0, 60, 24))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var frames = Enumerable.Range(0, 12).Select(i =>
            MenuTextStructureTests.Pixels(1, i < 3 ? 25 : 25 + i * 3,
                changedStroke: visibleStrokeChange && i >= 4)).ToArray();
        var captures = new FakeCaptureFrameSource { PixelFrames = frames };
        var detectorCalls = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test",
            ++detectorCalls < 10 ? new[] { new TextCandidate(new BoundingBox(0, 0, 60, 24), .95) } : Array.Empty<TextCandidate>()));
        var recognition = 0;
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = r => new[] { new OcrTextBlock(++recognition <= 3
                ? "Power 7" : "Power 8", new BoundingBox(4, 3, 48, 18)) } };
        var translator = new FakeTranslatorProvider("Google");
        var overlay = new FakeOverlayService();
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(captures, engine, translator, overlay, candidateDetector: detector, timeProvider: clock);
        var options = new TranslationPipelineRunOptions(requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.FromMilliseconds(200), minimumStableTextObservations: 2,
            minimumCandidateGroupingObservations: 1, minimumCandidateOverlayVisibleDuration: TimeSpan.Zero);
        using var session = service.CreateLiveSession(CreateProfile(zone), options);
        var sawSeven = false;
        var sawCleared = false;
        var sawEight = false;
        for (var i = 0; i < 9; i++)
        {
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            var texts = overlay.CurrentSnapshot?.TextItems.Select(t => t.Text).ToArray() ?? [];
            sawSeven |= texts.Contains("Translated Power 7");
            sawCleared |= sawSeven && recognition == 4 && texts.Length == 0;
            sawEight |= texts.Contains("Translated Power 8");
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.True(sawSeven);
        Assert.True(sawCleared, "A fresh mismatching OCR must clear the old translation before the replacement is confirmed.");
        Assert.True(sawEight);
        Assert.Equal(2, translator.CallCount);
        await session.RefreshAsync();
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
    }

    [Fact]
    public async Task LiveSession_MixedMenuAndDialogue_KeepsIndependentPoliciesAndReusesUnchangedText()
    {
        var menu = CreateZone("menu", "Menu", new AbsoluteRectangle(10, 20, 100, 60))
            with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var dialogue = CreateZone("dialog", "Dialogue", new AbsoluteRectangle(210, 20, 100, 60));
        var profile = CreateProfile(menu, dialogue) with
        {
            OcrSettings = new OcrSettings { Engine = "Tesseract", OrientationMode = OcrOrientationMode.Horizontal },
            TranslatorSettings = new TranslatorSettings { Provider = "GoogleWeb", SourceLanguage = "en", TargetLanguage = "ru" },
        };
        var engine = new FakeOcrEngine { EngineId = "Tesseract", BlocksFactory = request => new[]
        {
            new OcrTextBlock(request.ContentLayoutMode == ContentLayoutMode.StaticMenu
                ? (request.Frame.Region.Y == 21 ? "Continue" : "Settings") : "A synthetic sentence",
                new BoundingBox(0, 0, request.Frame.Width, request.Frame.Height)),
        } };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test", new[]
        {
            new TextCandidate(new BoundingBox(1, 1, 60, 16), .95),
            new TextCandidate(new BoundingBox(1, 19, 60, 16), .95),
        }));
        var translator = new FakeTranslatorProvider("GoogleWeb");
        var overlay = new FakeOverlayService();
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(new FakeCaptureFrameSource(), engine, translator, overlay, candidateDetector: detector, timeProvider: clock);
        using var session = service.CreateLiveSession(profile, new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        for (var i = 0; i < 3; i++)
        {
            await session.RefreshAsync();
            await session.PublishCompletedWorkAsync();
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.Equal(3, overlay.CurrentSnapshot!.TextItems.Count);
        var items = overlay.CurrentSnapshot!.TextItems;
        Assert.Equal(2, items.Count(i => i.TextStyle.LayoutMode == OverlayTextLayoutMode.KeepWithinSourceBounds));
        Assert.Single(items, i => i.TextStyle.LayoutMode == OverlayTextLayoutMode.ExpandFromSourceCenter);
        var calls = translator.CallCount;
        await session.RefreshAsync();
        await session.PublishCompletedWorkAsync();
        Assert.Equal(calls, translator.CallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaticMenu_TranslatesSeparateButtonsAndRevalidatesCachedValues(bool changeNumber)
    {
        var zone = CreateZone() with { ContentLayoutMode = ContentLayoutMode.StaticMenu };
        var profile = CreateProfile(zone) with
        {
            OcrSettings = new OcrSettings { Engine = "Tesseract", OrientationMode = OcrOrientationMode.Horizontal },
            TranslatorSettings = new TranslatorSettings { Provider = "GoogleWeb", SourceLanguage = "en", TargetLanguage = "ru" },
        };
        var calls = 0;
        var translator = new FakeTranslatorProvider("GoogleWeb", translateAsync: (request, _) =>
        {
            Interlocked.Increment(ref calls);
            Assert.Single(request.Texts);
            return Task.FromResult(new TranslateResponse(request.Texts.Select(t =>
                t.StartsWith("Cost", StringComparison.Ordinal) ? (changeNumber ? "Цена 99" : "Цена 50") : "Настройки"),
                TranslatedAt, "GoogleWeb"));
        });
        var engine = new FakeOcrEngine { EngineId = "Tesseract", BlocksFactory = request => new[]
        {
            new OcrTextBlock(request.Frame.Region.Y == 21 ? "Cost 50" : "Settings", new BoundingBox(0, 0, 60, 16)),
        } };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test", new[]
        {
            new TextCandidate(new BoundingBox(1, 1, 60, 16), .95),
            new TextCandidate(new BoundingBox(1, 19, 60, 16), .95),
        }));
        var service = CreateService(new FakeCaptureFrameSource(), engine, translator,
            new FakeOverlayService(), candidateDetector: detector);

        var first = await service.RunAllZonesAsync(profile);
        var second = await service.RunAllZonesAsync(profile);

        Assert.Equal(2, calls);
        Assert.Equal(new[] { changeNumber ? "Cost 50" : "Цена 50", "Настройки" },
            first.OverlaySnapshot.TextItems.Select(item => item.Text));
        Assert.Equal(first.OverlaySnapshot.TextItems.Select(i => i.Text), second.OverlaySnapshot.TextItems.Select(i => i.Text));
        Assert.All(first.OverlaySnapshot.TextItems, item =>
        {
            Assert.Equal(OverlayTextLayoutMode.KeepWithinSourceBounds, item.TextStyle.LayoutMode);
            Assert.Equal(60, item.Width);
            Assert.Equal(16, item.Height);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AutoLanguageGroups_PreserveDialogueOrIndependentFragmentsAndCache(bool candidates, bool independent)
    {
        var zone = CreateZone() with { TranslationGroupingMode = TranslationGroupingMode.WholeZone };
        var profile = CreateProfile(zone) with {
            OcrSettings = new OcrSettings { Engine = "Tesseract", ExperimentalMultilingual = true, OrientationMode = OcrOrientationMode.Horizontal },
            TranslatorSettings = new TranslatorSettings { Provider = "GoogleWeb", SourceLanguage = "en", TargetLanguage = "ru", AutoDetectSourceLanguage = true } };
        var requests = new List<TranslateRequest>();
        var translator = new FakeTranslatorProvider("GoogleWeb", translateAsync: (request, _) => {
            requests.Add(request);
            return Task.FromResult(new TranslateResponse(request.Texts.Select(t => request.SourceLanguage + ":" + t), TranslatedAt, "GoogleWeb")); });
        var engine = new FakeOcrEngine { EngineId = "Tesseract", BlocksFactory = _ => new[] {
            new OcrTextBlock("Hello", independent ? new BoundingBox(1, 1, 6, 4) : new BoundingBox(1, 1, 20, 4)),
            new OcrTextBlock("こんにちは", independent ? new BoundingBox(9, 1, 6, 4) : new BoundingBox(1, 6, 20, 4)),
            new OcrTextBlock("你好", independent ? new BoundingBox(17, 1, 6, 4) : new BoundingBox(1, 11, 20, 4)) } };
        var bounds = new BoundingBox(18, 7, 30, 18);
        var frames = new FakeCaptureFrameSource { PixelFrames = new[] { CreateCandidatePilotPixels(zone, 1, (bounds, (byte)10)) } };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test", new[] { new TextCandidate(bounds, 0.95) }));
        var service = CreateService(frames, engine, translator, new FakeOverlayService(), candidateDetector: detector);
        var options = candidates ? TranslationPipelineRunOptions.Default : TranslationPipelineRunOptions.LegacyFullPage;
        var result = await service.RunAsync(profile, zone, runOptions: options);
        if (independent)
        {
            Assert.Equal(2, requests.Count);
            Assert.Equal(new[] { "Hello", "你好" }, requests.Single(r => r.SourceLanguage == "auto").Texts);
            Assert.Equal(new[] { "こんにちは" }, requests.Single(r => r.SourceLanguage == "ja").Texts);
            Assert.Equal(new[] { "auto:Hello", "ja:こんにちは", "auto:你好" }, result.TranslateResponse!.TranslatedTexts);
        }
        else
        {
            Assert.Single(requests);
            Assert.Equal(new[] { "Hello こんにちは你好" }, requests[0].Texts);
            Assert.Equal(new[] { "auto:Hello こんにちは你好" }, result.TranslateResponse!.TranslatedTexts);
        }
        Assert.Equal(3, result.SourceOcrResult.TextBlocks.Count);
        Assert.Equal(independent ? 3 : 1, result.TranslationInputBlockCount);
        await service.RunAsync(profile, zone, runOptions: options);
        Assert.Equal(independent ? 2 : 1, requests.Count);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(true, true, null)]
    public async Task MultilingualExperiment_UsesCombinedModelsAndAutoSourceWithoutChangingProfile(bool enabled, bool candidates, bool? autoSource)
    {
        var zone = CreateZone() with { OcrLanguage = string.Empty };
        var profile = CreateProfile(zone) with
        {
            OcrSettings = new OcrSettings { Engine = "Tesseract", OrientationMode = OcrOrientationMode.Horizontal, ExperimentalMultilingual = enabled, MultilingualLanguages = "chi_tra+eng" },
            TranslatorSettings = new TranslatorSettings { Provider = "GoogleWeb", SourceLanguage = "ja", TargetLanguage = "ru", AutoDetectSourceLanguage = autoSource },
        };
        var engine = new FakeOcrEngine { EngineId = "Tesseract", BlocksFactory = _ => new[] { new OcrTextBlock("Hello world", new BoundingBox(0, 0, 20, 10)) } };
        var translator = new FakeTranslatorProvider("GoogleWeb");
        var bounds = new BoundingBox(18, 7, 30, 18);
        var frames = new FakeCaptureFrameSource { PixelFrames = new[] { CreateCandidatePilotPixels(zone, 1, (bounds, (byte)10)) } };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test", new[] { new TextCandidate(bounds, 0.95) }));
        var service = CreateService(frames, engine, translator, new FakeOverlayService(), candidateDetector: detector);
        await service.RunAsync(profile, zone, runOptions: candidates ? TranslationPipelineRunOptions.Default : TranslationPipelineRunOptions.LegacyFullPage);
        Assert.Equal(enabled ? "chi_tra+eng" : "ja", Assert.Single(engine.Requests).Language);
        Assert.Equal((autoSource ?? enabled) ? "auto" : "ja", translator.Request!.SourceLanguage);
        Assert.Equal("ja", profile.TranslatorSettings.SourceLanguage);
        Assert.Equal(string.Empty, zone.OcrLanguage);
    }

    private static readonly DateTimeOffset FrameTime = new(2026, 6, 19, 12, 0, 1, TimeSpan.Zero);
    private static readonly DateTimeOffset OcrTime = new(2026, 6, 19, 12, 0, 2, TimeSpan.Zero);
    private static readonly DateTimeOffset TranslatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BingBudget_CancelsAtDeadlineButPreservesCallerCancellation(bool callerCancels)
    {
        var clock = new DeadlineClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<TranslateResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken providerToken = default;
        var translator = new FakeTranslatorProvider("BingWeb", translateAsync: (_, token) =>
        {
            providerToken = token;
            entered.SetResult();
            return release.Task; // Deliberately ignores cancellation: never publish a late result.
        });
        var zone = CreateZone();
        var profile = CreateProfile(zone) with { TranslatorSettings = new TranslatorSettings { Provider = "BingWeb", SourceLanguage = "en", TargetLanguage = "ru" } };
        var overlay = new FakeOverlayService();
        var service = CreateService(new FakeCaptureFrameSource(), new FakeOcrEngine
        {
            BlocksFactory = _ => new[] { new OcrTextBlock("Hello", new BoundingBox(1, 1, 20, 8)) },
        }, translator, overlay, timeProvider: clock);
        using var cancellation = new CancellationTokenSource();
        var pending = service.RunAsync(profile, zone, runOptions: TranslationPipelineRunOptions.LegacyFullPage,
            cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(pending.IsCompleted);
        if (callerCancels) cancellation.Cancel();
        else clock.Advance(TimeSpan.FromSeconds(1));
        if (callerCancels) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        else
        {
            var error = await Assert.ThrowsAsync<TranslationPipelineException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(TranslationPipelineStage.Translation, error.Stage);
            Assert.Equal(TranslatorProviderFailureKind.Timeout, Assert.IsType<TranslatorProviderException>(error.InnerException).FailureKind);
        }
        Assert.True(providerToken.IsCancellationRequested);
        release.SetResult(new TranslateResponse(new[] { "Late result" }, TranslatedAt, "BingWeb"));
        Assert.DoesNotContain("Show:1", overlay.Events);
    }

    [Fact]
    public async Task BingBudget_QueueDeadlineAndLiveProgressDoNotWaitForTranslationCompletion()
    {
        var clock = new DeadlineClock();
        var zone = CreateZone("progress", "Progress", new AbsoluteRectangle(0, 0, 240, 140));
        var bounds = new[] { new BoundingBox(8, 8, 40, 16), new BoundingBox(170, 90, 40, 16) };
        var pixels = CreateCandidatePilotPixels(zone, 1, (bounds[0], (byte)10), (bounds[1], (byte)20));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<TranslateResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        TranslationProviderRequestDiagnostics? runningDiagnostics = null;
        var translator = new FakeTranslatorProvider("BingWeb", translateAsync: (request, _) =>
        {
            runningDiagnostics = request.Diagnostics;
            request.Diagnostics!.MarkProviderInvocationStarted("BingWeb", clock.GetUtcNow());
            entered.TrySetResult();
            return release.Task;
        });
        var service = CreateService(new FakeCaptureFrameSource { PixelFrames = Enumerable.Repeat(pixels, 10).ToArray() },
            new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId, BlocksFactory = request => new[]
            { new OcrTextBlock("Text " + request.Frame.PixelData.Span[0], new BoundingBox(1, 1, 25, 10)) } },
            translator, new FakeOverlayService(), candidateDetector: new FakeCandidateDetector(_ =>
                TextCandidateDetectionResult.Available("test", bounds.Select(b => new TextCandidate(b, 0.95)))), timeProvider: clock);
        var profile = CreateProfile(zone) with { TranslatorSettings = new TranslatorSettings { Provider = "BingWeb", SourceLanguage = "en", TargetLanguage = "ru" } };
        using var session = service.CreateLiveSession(profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true, candidateTranslationMaxParallelism: 1));
        var first = await session.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, first.CandidateLifecycleEvents.Count(e => e.Kind == LiveCandidateLifecycleEventKind.CandidateOcrCompleted));
        Assert.Equal(2, first.PendingTranslations.Count);
        Assert.Empty(first.BatchResult.ZoneResults);
        clock.Advance(TimeSpan.FromSeconds(5));
        var waiting = await session.PublishCompletedWorkAsync();
        Assert.Contains(waiting.CandidateLifecycleEvents, e => e.Kind == LiveCandidateLifecycleEventKind.CandidateTranslationQueueWaitWarning);
        Assert.Contains(waiting.CandidateLifecycleEvents, e => e.Kind == LiveCandidateLifecycleEventKind.CandidateTranslationCredentialsWaitWarning);
        Assert.Empty((await session.PublishCompletedWorkAsync()).CandidateLifecycleEvents);
        runningDiagnostics!.MarkNetworkRequestStarted(TranslationProviderNetworkRequestKind.Translation, clock.GetUtcNow());
        Assert.Contains((await session.PublishCompletedWorkAsync()).CandidateLifecycleEvents,
            e => e.Kind == LiveCandidateLifecycleEventKind.CandidateTranslationResponseWaitWarning);
        clock.Advance(TimeSpan.FromSeconds(25));
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        // Both independent continuations have their own completion signals.
        var completed = await session.PublishCompletedWorkAsync();
        while (completed.BatchResult.ZoneFailures.Count < 2)
        {
            await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(5));
            completed = await session.PublishCompletedWorkAsync();
        }
        Assert.Equal(2, completed.BatchResult.ZoneFailures.Count);
        Assert.Empty(completed.PendingTranslations);
        Assert.Equal(1, translator.CallCount); // The queued candidate never reached the provider.
        Assert.All(completed.BatchResult.ZoneFailures, failure => Assert.Equal(TranslationPipelineStage.Translation, failure.Stage));
        release.SetResult(new TranslateResponse(new[] { "Late result" }, TranslatedAt, "BingWeb"));
        Assert.Empty((await session.PublishCompletedWorkAsync()).BatchResult.OverlaySnapshot.TextItems);
    }

    private sealed class DeadlineClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        private readonly List<DeadlineTimer> timers = new();
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new DeadlineTimer(this, callback, state);
            timer.Change(dueTime, period);
            timers.Add(timer);
            return timer;
        }
        public void Advance(TimeSpan elapsed)
        {
            now += elapsed;
            foreach (var timer in timers.ToArray()) timer.FireIfDue();
        }
        private sealed class DeadlineTimer(DeadlineClock clock, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset due;
            private bool disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : clock.now + dueTime;
                return !disposed;
            }
            public void FireIfDue()
            {
                if (disposed || due > clock.now) return;
                due = DateTimeOffset.MaxValue;
                callback(state);
            }
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    [Theory]
    [InlineData(true, TranslatorProviderFailureKind.Dns, 0)]
    [InlineData(false, TranslatorProviderFailureKind.Dns, 0)]
    [InlineData(true, TranslatorProviderFailureKind.Connection, 0)]
    [InlineData(true, TranslatorProviderFailureKind.Network, 0)]
    [InlineData(true, TranslatorProviderFailureKind.Timeout, 0)]
    [InlineData(true, TranslatorProviderFailureKind.Throttled, 30)]
    [InlineData(true, TranslatorProviderFailureKind.UserVerificationRequired, -1)]
    [InlineData(true, TranslatorProviderFailureKind.Configuration, -1)]
    [InlineData(true, TranslatorProviderFailureKind.Tls, -1)]
    public async Task LiveSession_StableText_RecoversOnlyFromTransientTranslationFailures(
        bool candidates, TranslatorProviderFailureKind kind, int pause)
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var bounds = new BoundingBox(8, 8, 30, 12);
        var pixels = CreateCandidatePilotPixels(zone, frameMarker: 1, (bounds, (byte)10));
        var clock = new ManualTimeProvider(FrameTime);
        var offline = true;
        var translator = new FakeTranslatorProvider("Google", translateAsync: (request, _) => offline
            ? Task.FromException<TranslateResponse>(new TranslatorProviderException("Google", kind, "Offline",
                retryAfter: pause > 0 ? TimeSpan.FromSeconds(pause) : null,
                nextRetryAt: pause > 0 ? clock.GetUtcNow().AddSeconds(pause + 5) : null))
            : Task.FromResult(new TranslateResponse(request.Texts.Select(text => "Translated " + text), TranslatedAt, "Google")));
        var service = CreateService(new FakeCaptureFrameSource { PixelFrames = Enumerable.Repeat(pixels, 100).ToArray() },
            new FakeOcrEngine { EngineId = candidates ? OcrSettings.TesseractEngineId : OcrSettings.WindowsEngineId, BlocksFactory = _ => new[] { new OcrTextBlock("Hello", new BoundingBox(1, 1, 20, 8)) } }, translator, new FakeOverlayService(),
            candidateDetector: new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
                "test", new[] { new TextCandidate(bounds, 0.9) })), timeProvider: clock);
        using var session = service.CreateLiveSession(CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: candidates));
        await session.RefreshAsync();
        await session.RefreshAsync();
        var initial = await session.RefreshAsync();
        Assert.True(translator.CallCount == 1, string.Join(";", initial.BatchResult.ZoneFailures.Select(f => f.Exception.ToString())));
        if (pause < 0)
        {
            offline = false;
            clock.Advance(TimeSpan.FromMinutes(2));
            await session.RefreshAsync();
            Assert.Equal(1, translator.CallCount);
            return;
        }
        foreach (var delay in new[] { 5, 10, 20, 20 })
        {
            var calls = translator.CallCount;
            clock.Advance(TimeSpan.FromSeconds(pause > 0 ? pause + 5 : delay) - TimeSpan.FromMilliseconds(1));
            await session.RefreshAsync();
            Assert.Equal(calls, translator.CallCount);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await session.RefreshAsync();
            Assert.Equal(calls + 1, translator.CallCount);
        }
        offline = false;
        clock.Advance(TimeSpan.FromSeconds(pause > 0 ? pause + 5 : 20));
        var recovered = await session.RefreshAsync();
        Assert.Equal(6, translator.CallCount);
        Assert.Empty(recovered.BatchResult.ZoneFailures);
        Assert.NotEmpty(recovered.BatchResult.ZoneResults);
        clock.Advance(TimeSpan.FromMinutes(1));
        await session.RefreshAsync();
        Assert.Equal(6, translator.CallCount);
    }
    [Fact]
    public async Task RunAsync_SingleJapaneseBlock_NormalizesProviderInputAndPreservesRawText()
    {
        var zone = CreateZone() with { OcrLanguage = "ja" };
        var engine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[] { new OcrTextBlock("日 本 語", new BoundingBox(4, 5, 24, 10)) },
        };
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(new FakeCaptureFrameSource(), engine, translator, new FakeOverlayService());
        var result = await service.RunAsync(CreateProfile(zone), zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);
        Assert.Equal(new[] { "日本語" }, translator.Request!.Texts);
        Assert.Equal("日 本 語", Assert.Single(result.SourceOcrResult.TextBlocks).Text);
        Assert.Equal(1, result.TranslationInputBlockCount);
        Assert.Equal(1, result.TranslatedBlockCount);
    }

    [Theory]
    [InlineData("|", false)]
    [InlineData("~", false)]
    [InlineData(", !", false)]
    [InlineData("…?!", false)]
    [InlineData("40", true)]
    [InlineData("0%", true)]
    [InlineData("A!", true)]
    [InlineData("가", true)]
    [InlineData("+", true)]
    public async Task RunAsync_PunctuationNoise_PreservesRawOcr(string text, bool translate)
    {
        var zone = CreateZone();
        var engine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[] { new OcrTextBlock(text, new BoundingBox(4, 5, 24, 10)) },
        };
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(new FakeCaptureFrameSource(), engine, translator, new FakeOverlayService());
        var result = await service.RunAsync(CreateProfile(zone), zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);
        Assert.Equal(1, result.RecognizedBlockCount);
        Assert.Equal(translate ? 1 : 0, translator.CallCount);
        Assert.Equal(translate ? 1 : 0, result.TranslationInputBlockCount);
        Assert.Equal(translate ? 1 : 0, result.OverlaySnapshot.TextItems.Count);
    }

    [Fact]
    public async Task LiveSession_PunctuationNoise_DoesNotStartEmptyOcrWatchdog()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var bounds = new BoundingBox(8, 8, 30, 12);
        var pixels = CreateCandidatePilotPixels(zone, frameMarker: 1, (bounds, (byte)10));
        var engine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[] { new OcrTextBlock("|", new BoundingBox(0, 0, 10, 10)) },
        };
        var translator = new FakeTranslatorProvider("Google");
        var clock = new ManualTimeProvider(FrameTime);
        var service = CreateService(
            new FakeCaptureFrameSource { PixelFrames = Enumerable.Repeat(pixels, 3).ToArray() },
            engine, translator, new FakeOverlayService(),
            candidateDetector: new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
                "test-detector", new[] { new TextCandidate(bounds, 0.95) })),
            timeProvider: clock);
        var options = new TranslationPipelineRunOptions(
            enableCandidateDetectorPilot: true, minimumCandidateGroupingObservations: 1)
        {
            MinimumCandidateGroupingDuration = TimeSpan.Zero,
        };
        using var session = service.CreateLiveSession(CreateProfile(zone), options);
        await session.RefreshAsync();
        clock.Advance(TimeSpan.FromSeconds(25));
        var update = await session.RefreshAsync();
        Assert.Single(engine.Requests);
        Assert.Equal(0, translator.CallCount);
        Assert.DoesNotContain(update.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateEmptyOcrRetryScheduled);
    }

    [Fact]
    public void RunOptions_WhenMinimumCandidateGroupingDurationIsNegative_RejectsIt()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TranslationPipelineRunOptions
            {
                MinimumCandidateGroupingDuration = TimeSpan.FromMilliseconds(-1),
            });

        Assert.Equal("MinimumCandidateGroupingDuration", exception.ParamName);
    }

    [Fact]
    public async Task RunAsync_WhenTextIsRecognized_TranslatesAndShowsOverlay()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var frameSource = new FakeCaptureFrameSource();
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google", new[] { "Привет" });
        var overlay = new FakeOverlayService();
        var service = CreateService(frameSource, ocrEngine, translator, overlay);

        var result = await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal(new[] { new CaptureRegion(10, 20, 100, 40) }, frameSource.CapturedRegions);
        var request = Assert.Single(ocrEngine.Requests);
        Assert.Equal("en", request.Language);
        Assert.Equal(zone.Id, request.ZoneId);
        Assert.Equal(profile.OcrSettings.OrientationMode, request.OrientationMode);
        Assert.Equal(new[] { "Hello" }, translator.Request?.Texts);
        Assert.Equal("en", translator.Request?.SourceLanguage);
        Assert.Equal("ru", translator.Request?.TargetLanguage);
        Assert.Equal("SECRET_ACCESS_TOKEN", translator.Request?.Credentials.AccessToken);
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
        Assert.True(overlay.IsVisible);
        Assert.Equal(profile.OverlaySettings, result.OverlaySnapshot.OverlaySettings);

        var item = Assert.Single(result.OverlaySnapshot.TextItems);
        Assert.Equal("Привет", item.Text);
        Assert.Equal(14, item.X);
        Assert.Equal(25, item.Y);
        Assert.Equal(24, item.Width);
        Assert.Equal(10, item.Height);
        Assert.Equal(1, result.RecognizedBlockCount);
        Assert.Equal(1, result.TranslatedBlockCount);
    }

    [Fact]
    public async Task RunAsync_WhenZoneHasExplicitOcrLanguage_PassesItToOcrAndKeepsTranslatorSource()
    {
        var zone = CreateZone() with
        {
            OcrLanguage = "jpn_vert",
        };
        var profile = CreateProfile(zone) with
        {
            OcrSettings = new OcrSettings
            {
                Engine = OcrSettings.TesseractEngineId,
                OrientationMode = OcrOrientationMode.Vertical,
            },
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "ja",
                TargetLanguage = "en",
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Column text", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            new FakeCaptureFrameSource(),
            ocrEngine,
            translator,
            new FakeOverlayService());

        await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        var request = Assert.Single(ocrEngine.Requests);
        Assert.Equal(OcrSettings.TesseractEngineId, request.EngineId);
        Assert.Equal("jpn_vert", request.Language);
        Assert.Equal(OcrOrientationMode.Vertical, request.OrientationMode);
        Assert.Equal("ja", translator.Request?.SourceLanguage);
    }

    [Fact]
    public async Task RunAsync_WhenProfileUsesTesseractLanguageTagForTranslation_NormalizesItBeforeCacheAndProvider()
    {
        var zone = CreateZone() with { OcrLanguage = "tha" };
        var profile = CreateProfile(zone) with
        {
            OcrSettings = new OcrSettings { Engine = OcrSettings.TesseractEngineId },
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "tha",
                TargetLanguage = "ru",
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[] { new OcrTextBlock("สวัสดี", new BoundingBox(0, 0, 20, 10)) },
        };
        var translator = new FakeTranslatorProvider("Google", new[] { "Привет" });
        var service = CreateService(new FakeCaptureFrameSource(), ocrEngine, translator, new FakeOverlayService());

        await service.RunAsync(profile, zone, runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal("tha", Assert.Single(ocrEngine.Requests).Language);
        Assert.Equal("th", translator.Request?.SourceLanguage);
    }

    [Fact]
    public async Task RunAsync_WhenLegacyProfileUsesGenericChineseTranslatorTag_NormalizesItForProvider()
    {
        var zone = CreateZone() with { OcrLanguage = "chi_sim" };
        var profile = CreateProfile(zone) with
        {
            OcrSettings = new OcrSettings { Engine = OcrSettings.TesseractEngineId },
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "zh",
                TargetLanguage = "ru",
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[] { new OcrTextBlock("你好", new BoundingBox(0, 0, 20, 10)) },
        };
        var translator = new FakeTranslatorProvider("Google", new[] { "Привет" });
        var service = CreateService(new FakeCaptureFrameSource(), ocrEngine, translator, new FakeOverlayService());

        await service.RunAsync(profile, zone, runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal("chi_sim", Assert.Single(ocrEngine.Requests).Language);
        Assert.Equal("zh-CN", translator.Request?.SourceLanguage);
    }

    [Fact]
    public async Task RunAsync_WhenOcrFindsNoText_SkipsTranslationAndShowsEmptyOverlay()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var translator = new FakeTranslatorProvider("Google", new[] { "unused" });
        var overlay = new FakeOverlayService();
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine(),
            translator,
            overlay,
            credentialStorage: new FakeCredentialStorage());

        var result = await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Null(translator.Request);
        Assert.Null(result.TranslateResponse);
        Assert.Empty(result.OverlaySnapshot.TextItems);
        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public async Task RunAsync_WhenExperimentalWebProviderIsSelected_DoesNotRequireStoredCredentials()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "GoogleWeb",
                SourceLanguage = "en",
                TargetLanguage = "ru",
            },
        };
        var translator = new FakeTranslatorProvider("GoogleWeb", new[] { "Привет" });
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
                },
            },
            translator,
            new FakeOverlayService(),
            credentialStorage: new FakeCredentialStorage());

        var result = await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal("experimental-web-provider", translator.Request?.Credentials.AccessToken);
        Assert.Equal("GoogleWeb", translator.Request?.Credentials.ProjectId);
        Assert.Equal(new Uri("https://translate.googleapis.com"), translator.Request?.Credentials.Endpoint);
        Assert.Equal("Привет", Assert.Single(result.OverlaySnapshot.TextItems).Text);
    }

    [Fact]
    public async Task RunAsync_WhenTranslationIsCached_SkipsTranslatorProviderOnRepeatedRun()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var translator = new FakeTranslatorProvider("Google", new[] { "Привет" });
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
                },
            },
            translator,
            new FakeOverlayService());

        var first = await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);
        var second = await service.RunAsync(
            profile,
            zone,
            first.OverlaySnapshot,
            TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal(1, translator.CallCount);
        Assert.Equal(1, second.CacheResult?.MemoryHitCount);
        Assert.Equal(0, second.CacheResult?.MissCount);
        Assert.Equal(new[] { "Привет" }, second.TranslateResponse?.TranslatedTexts);
    }

    [Fact]
    public async Task RunAsync_WhenZoneUsesWholeZoneGrouping_TranslatesJoinedTextAndShowsSingleOverlayItem()
    {
        var zone = CreateZone("zone-a", "Dialog", new AbsoluteRectangle(10, 20, 200, 100)) with
        {
            TranslationGroupingMode = TranslationGroupingMode.WholeZone,
        };
        var profile = CreateProfile(zone) with
        {
            OcrSettings = new OcrSettings
            {
                OrientationMode = OcrOrientationMode.Horizontal,
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("that I ate was very", new BoundingBox(0, 14, 120, 10)),
                new OcrTextBlock("apple", new BoundingBox(0, 0, 40, 10)),
                new OcrTextBlock("tasty", new BoundingBox(0, 28, 50, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google", new[] { "apple that I ate was very tasty translated" });
        var overlay = new FakeOverlayService();
        var service = CreateService(new FakeCaptureFrameSource(), ocrEngine, translator, overlay);

        var first = await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);
        var second = await service.RunAsync(
            profile,
            zone,
            first.OverlaySnapshot,
            TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal(new[] { "apple that I ate was very tasty" }, translator.Request?.Texts);
        Assert.Equal(1, translator.CallCount);
        Assert.Equal(3, first.RecognizedBlockCount);
        Assert.Equal(1, first.TranslatedBlockCount);
        Assert.Equal(1, second.CacheResult?.MemoryHitCount);
        Assert.Equal(0, second.CacheResult?.MissCount);

        var item = Assert.Single(first.OverlaySnapshot.TextItems);
        Assert.Equal("apple that I ate was very tasty translated", item.Text);
        Assert.Equal(10, item.X);
        Assert.Equal(20, item.Y);
        Assert.Equal(120, item.Width);
        Assert.Equal(38, item.Height);
        Assert.Same(second.OverlaySnapshot, overlay.CurrentSnapshot);
    }

    [Fact]
    public async Task RunAsync_WhenZoneUsesNearbyGrouping_TranslatesNearbyClustersAndShowsSeparateOverlayItems()
    {
        var zone = CreateZone("zone-a", "Comic page", new AbsoluteRectangle(10, 20, 400, 400)) with
        {
            TranslationGroupingMode = TranslationGroupingMode.NearbyBlocks,
            TextGrouping = new OcrZoneTextGroupingSettings
            {
                MergeDistancePercent = 5,
            },
        };
        var profile = CreateProfile(zone) with
        {
            OcrSettings = new OcrSettings
            {
                OrientationMode = OcrOrientationMode.Horizontal,
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("NO SEE.", new BoundingBox(120, 114, 70, 10)),
                new OcrTextBlock("YO!", new BoundingBox(300, 20, 32, 14)),
                new OcrTextBlock("LONG TIME", new BoundingBox(100, 100, 90, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google", new[] { "Они!", "Давно не виделись." });
        var overlay = new FakeOverlayService();
        var service = CreateService(new FakeCaptureFrameSource(), ocrEngine, translator, overlay);

        var result = await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal(new[] { "YO!", "LONG TIME NO SEE." }, translator.Request?.Texts);
        Assert.Equal(3, result.RecognizedBlockCount);
        Assert.Equal(2, result.TranslatedBlockCount);
        Assert.Equal(new[] { "Они!", "Давно не виделись." }, result.OverlaySnapshot.TextItems.Select(item => item.Text));
        Assert.Equal(new[] { 310, 110 }, result.OverlaySnapshot.TextItems.Select(item => item.X));
        Assert.Equal(new[] { 40, 112 }, result.OverlaySnapshot.TextItems.Select(item => item.Y));
        Assert.Equal(new[] { 32, 90 }, result.OverlaySnapshot.TextItems.Select(item => item.Width));
        Assert.Equal(new[] { 14, 24 }, result.OverlaySnapshot.TextItems.Select(item => item.Height));
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
    }

    [Fact]
    public async Task RunAsync_WhenNearbyGroupingHasStaggeredWords_TranslatesRowsInReadingOrder()
    {
        var zone = CreateZone("zone-a", "Comic bubble", new AbsoluteRectangle(10, 20, 400, 200)) with
        {
            TranslationGroupingMode = TranslationGroupingMode.NearbyBlocks,
            TextGrouping = new OcrZoneTextGroupingSettings
            {
                MergeDistancePercent = 5,
            },
        };
        var profile = CreateProfile(zone) with
        {
            OcrSettings = new OcrSettings
            {
                OrientationMode = OcrOrientationMode.Horizontal,
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("ripe", new BoundingBox(80, 2, 40, 12)),
                new OcrTextBlock("Apple", new BoundingBox(10, 6, 60, 12)),
                new OcrTextBlock("juicy", new BoundingBox(70, 22, 50, 12)),
                new OcrTextBlock("tasty", new BoundingBox(10, 26, 50, 12)),
                new OcrTextBlock("picked", new BoundingBox(80, 42, 60, 12)),
                new OcrTextBlock("yesterday", new BoundingBox(10, 46, 60, 12)),
            },
        };
        var translator = new FakeTranslatorProvider("Google", new[] { "Translated grouped bubble" });
        var service = CreateService(new FakeCaptureFrameSource(), ocrEngine, translator, new FakeOverlayService());

        var result = await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal(new[] { "Apple ripe tasty juicy yesterday picked" }, translator.Request?.Texts);
        Assert.Equal(6, result.RecognizedBlockCount);
        Assert.Equal(1, result.TranslatedBlockCount);
        Assert.Equal("Translated grouped bubble", Assert.Single(result.OverlaySnapshot.TextItems).Text);
    }

    [Fact]
    public async Task RunAsync_WhenFrameIsEffectivelyUnchanged_ReusesPreviousPipelineResult()
    {
        var zone = CreateZone();
        var firstPixels = CreatePixels(zone, 42);
        var secondPixels = firstPixels.ToArray();
        secondPixels[0] = 43;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[] { firstPixels, secondPixels },
            CapturedAtFrames = new[] { FrameTime, FrameTime.AddMilliseconds(20) },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google", new[] { "Salut" });
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            optimizationOptions: new TranslationPipelineOptimizationOptions(
                frameDifferenceThreshold: 0.001d,
                debounceInterval: TimeSpan.FromMilliseconds(250)));

        var first = await service.RunAsync(
            CreateProfile(zone),
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);
        var second = await service.RunAsync(
            CreateProfile(zone),
            zone,
            first.OverlaySnapshot,
            TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal(2, frameSource.CapturedRegions.Count);
        Assert.Single(ocrEngine.Requests);
        Assert.Equal(1, translator.CallCount);
        Assert.True(second.Optimization.OcrSkipped);
        Assert.True(second.Optimization.TranslationSkipped);
        Assert.True(second.Optimization.Debounced);
        Assert.True(second.Optimization.FrameDifferenceRatio <= 0.001d);
        Assert.Equal(TimeSpan.Zero, second.Timings.OcrElapsed);
        Assert.Equal(TimeSpan.Zero, second.Timings.TranslationElapsed);
        Assert.Equal(new[] { "Salut" }, second.TranslateResponse?.TranslatedTexts);
    }

    [Fact]
    public async Task RunAsync_WhenFrameChangesBeyondThreshold_RunsOcrAndTranslationAgain()
    {
        var zone = CreateZone();
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreatePixels(zone, 42),
                CreatePixels(zone, 200),
            },
            CapturedAtFrames = new[] { FrameTime, FrameTime.AddMilliseconds(20) },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 42 ? "Hello" : "World",
                    new BoundingBox(4, 5, 24, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            optimizationOptions: new TranslationPipelineOptimizationOptions(
                frameDifferenceThreshold: 0.001d,
                debounceInterval: TimeSpan.FromMilliseconds(250)));

        var first = await service.RunAsync(
            CreateProfile(zone),
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);
        var second = await service.RunAsync(
            CreateProfile(zone),
            zone,
            first.OverlaySnapshot,
            TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal(2, ocrEngine.Requests.Count);
        Assert.Equal(2, translator.CallCount);
        Assert.False(second.Optimization.OcrSkipped);
        Assert.False(second.Optimization.TranslationSkipped);
        Assert.NotNull(second.Optimization.FrameDifferenceRatio);
        Assert.True(second.Optimization.FrameDifferenceRatio > 0.001d);
        Assert.Equal(new[] { "Translated World" }, second.TranslateResponse?.TranslatedTexts);
    }

    [Fact]
    public async Task RunAsync_WhenStableTextIsRequired_WaitsBeforeTranslation()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var frameSource = new FakeCaptureFrameSource
        {
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime.AddMilliseconds(500),
                FrameTime.AddMilliseconds(1100),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            RecognizedAtFrames = new[]
            {
                OcrTime,
                OcrTime.AddMilliseconds(500),
                OcrTime.AddMilliseconds(1100),
            },
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            optimizationOptions: new TranslationPipelineOptimizationOptions());
        var runOptions = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.FromSeconds(1),
            enableCandidateDetectorPilot: false);

        var first = await service.RunAsync(profile, zone, runOptions: runOptions);
        var second = await service.RunAsync(profile, zone, first.OverlaySnapshot, runOptions);
        var third = await service.RunAsync(profile, zone, second.OverlaySnapshot, runOptions);

        Assert.Equal(3, frameSource.CapturedRegions.Count);
        Assert.Equal(3, ocrEngine.Requests.Count);
        Assert.Null(first.TranslateResponse);
        Assert.Null(second.TranslateResponse);
        Assert.True(first.Optimization.TranslationSkipped);
        Assert.True(second.Optimization.TranslationSkipped);
        Assert.Equal(1, first.TranslationInputBlockCount);
        Assert.True(first.TextStability.IsRequired);
        Assert.False(first.TextStability.IsStable);
        Assert.Equal(1, first.TextStability.ObservationCount);
        Assert.Equal(1, first.TextStability.RequiredObservationCount);
        Assert.Equal(OcrTime, first.TextStability.FirstObservedAt);
        Assert.Equal(OcrTime.AddMilliseconds(500), second.TextStability.LastObservedAt);
        Assert.Equal(TimeSpan.FromMilliseconds(500), second.TextStability.ObservedDuration);
        Assert.Empty(first.OverlaySnapshot.TextItems);
        Assert.Empty(second.OverlaySnapshot.TextItems);
        Assert.Equal(1, translator.CallCount);
        Assert.Equal(1, third.TranslationInputBlockCount);
        Assert.True(third.TextStability.IsStable);
        Assert.Equal(3, third.TextStability.ObservationCount);
        Assert.Equal(TimeSpan.FromMilliseconds(1100), third.TextStability.ObservedDuration);
        Assert.Equal(new[] { "Translated Hello" }, third.TranslateResponse?.TranslatedTexts);
        Assert.Equal("Translated Hello", Assert.Single(third.OverlaySnapshot.TextItems).Text);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenStableTextIsPending_CanPreservePreviousOverlay()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var previousSnapshot = new OverlaySnapshot(
            new[] { new OverlayTextItem("Previous translation", 1, 2, 30, 12) },
            FrameTime);
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google");
        var overlay = new FakeOverlayService();
        var service = CreateService(
            new FakeCaptureFrameSource(),
            ocrEngine,
            translator,
            overlay,
            optimizationOptions: new TranslationPipelineOptimizationOptions());
        var runOptions = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.FromSeconds(1),
            preservePreviousOverlayWhileWaitingForStableText: true,
            enableCandidateDetectorPilot: false);

        var result = await service.RunAllZonesAsync(profile, previousSnapshot, runOptions);

        Assert.Same(previousSnapshot, result.OverlaySnapshot);
        Assert.Same(previousSnapshot, overlay.CurrentSnapshot);
        Assert.Equal(1, result.RecognizedBlockCount);
        Assert.Equal(0, result.TranslatedBlockCount);
        Assert.Equal(1, result.SkippedTranslationCount);
        Assert.Equal(0, translator.CallCount);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenLiveOcrFindsNoText_PreservesPreviousOverlay()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var previousSnapshot = new OverlaySnapshot(
            new[] { new OverlayTextItem("Previous translation", 1, 2, 30, 12) },
            FrameTime);
        var translator = new FakeTranslatorProvider("Google");
        var overlay = new FakeOverlayService();
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine(),
            translator,
            overlay,
            optimizationOptions: new TranslationPipelineOptimizationOptions());
        var runOptions = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.FromMilliseconds(300),
            preservePreviousOverlayWhileWaitingForStableText: true,
            enableCandidateDetectorPilot: false);

        var result = await service.RunAllZonesAsync(profile, previousSnapshot, runOptions);

        Assert.Same(previousSnapshot, result.OverlaySnapshot);
        Assert.Same(previousSnapshot, overlay.CurrentSnapshot);
        Assert.Equal(1, result.SucceededZoneCount);
        Assert.Equal(0, result.RecognizedBlockCount);
        Assert.Equal(0, result.TranslatedBlockCount);
        Assert.Equal(0, translator.CallCount);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenAllZonesFailInLiveMode_PreservesPreviousOverlay()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var previousSnapshot = new OverlaySnapshot(
            new[] { new OverlayTextItem("Previous translation", 1, 2, 30, 12) },
            FrameTime);
        var overlay = new FakeOverlayService();
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
                },
            },
            new FakeTranslatorProvider("Google"),
            overlay,
            new FakeCredentialStorage(),
            new TranslationPipelineOptimizationOptions());
        var runOptions = new TranslationPipelineRunOptions(
            preservePreviousOverlayWhileWaitingForStableText: true,
            enableCandidateDetectorPilot: false);

        var result = await service.RunAllZonesAsync(profile, previousSnapshot, runOptions);

        Assert.Same(previousSnapshot, result.OverlaySnapshot);
        Assert.Same(previousSnapshot, overlay.CurrentSnapshot);
        Assert.Empty(result.ZoneResults);
        Assert.Single(result.ZoneFailures);
    }

    [Fact]
    public async Task RunAsync_WhenCjkOcrWhitespaceChanges_TreatsTextAsStable()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "zh-CN",
                TargetLanguage = "ru",
            },
        };
        var recognizedTexts = new[] { "你 好", "你好" };
        var recognizedTextIndex = 0;
        var ocrEngine = new FakeOcrEngine
        {
            RecognizedAtFrames = new[]
            {
                OcrTime,
                OcrTime.AddMilliseconds(500),
            },
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock(recognizedTexts[recognizedTextIndex++], new BoundingBox(4, 5, 24, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            new FakeCaptureFrameSource(),
            ocrEngine,
            translator,
            new FakeOverlayService(),
            optimizationOptions: new TranslationPipelineOptimizationOptions());
        var runOptions = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.FromMilliseconds(300),
            enableCandidateDetectorPilot: false);

        var first = await service.RunAsync(profile, zone, runOptions: runOptions);
        var second = await service.RunAsync(profile, zone, first.OverlaySnapshot, runOptions);

        Assert.Null(first.TranslateResponse);
        Assert.True(first.Optimization.TranslationSkipped);
        Assert.Equal(1, translator.CallCount);
        Assert.Equal(new[] { "你好" }, translator.Request?.Texts);
        Assert.Equal("Translated 你好", Assert.Single(second.OverlaySnapshot.TextItems).Text);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenRestorePreviousOverlayAfterCapture_RestoresOverlayDuringOcrAndTranslation()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var previousSnapshot = new OverlaySnapshot(
            new[] { new OverlayTextItem("Previous translation", 1, 2, 30, 12) },
            FrameTime);
        var overlay = new FakeOverlayService();
        overlay.Show(previousSnapshot);
        var frameSource = new FakeCaptureFrameSource
        {
            OnCapture = () => Assert.False(overlay.IsVisible),
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ =>
            {
                Assert.True(overlay.IsVisible);
                Assert.Same(previousSnapshot, overlay.CurrentSnapshot);

                return new[]
                {
                    new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
                };
            },
        };
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google", new[] { "New translation" }),
            overlay);
        var runOptions = new TranslationPipelineRunOptions(
            restorePreviousOverlayAfterCapture: true,
            enableCandidateDetectorPilot: false);

        var result = await service.RunAllZonesAsync(profile, previousSnapshot, runOptions);

        Assert.Equal(new[] { "Show:1", "Hide", "Show:1", "Show:1" }, overlay.Events);
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
        Assert.Equal("New translation", Assert.Single(result.OverlaySnapshot.TextItems).Text);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenOverlayIsExcludedFromCapture_KeepsOverlayVisibleDuringCapture()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var previousSnapshot = new OverlaySnapshot(
            new[] { new OverlayTextItem("Previous translation", 1, 2, 30, 12) },
            FrameTime);
        var overlay = new FakeOverlayService
        {
            IsExcludedFromCapture = true,
        };
        overlay.Show(previousSnapshot);
        var frameSource = new FakeCaptureFrameSource
        {
            OnCapture = () =>
            {
                Assert.True(overlay.IsVisible);
                Assert.Same(previousSnapshot, overlay.CurrentSnapshot);
            },
        };
        var service = CreateService(
            frameSource,
            new FakeOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
                },
            },
            new FakeTranslatorProvider("Google", new[] { "New translation" }),
            overlay);
        var runOptions = new TranslationPipelineRunOptions(
            restorePreviousOverlayAfterCapture: true,
            enableCandidateDetectorPilot: false);

        var result = await service.RunAllZonesAsync(profile, previousSnapshot, runOptions);

        Assert.Equal(new[] { "Show:1", "Show:1" }, overlay.Events);
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
        Assert.Equal("New translation", Assert.Single(result.OverlaySnapshot.TextItems).Text);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenProfileHasMultipleZones_ProcessesEachZoneAndShowsCombinedOverlay()
    {
        var firstZone = CreateZone("zone-a", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var secondZone = CreateZone("zone-b", "Choice", new AbsoluteRectangle(200, 120, 80, 30));
        var profile = CreateProfile(firstZone, secondZone);
        var frameSource = new FakeCaptureFrameSource();
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = request => new[]
            {
                new OcrTextBlock($"Text {request.ZoneId}", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            overlay);

        var result = await service.RunAllZonesAsync(
            profile,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal(
            new[]
            {
                new CaptureRegion(10, 20, 100, 40),
                new CaptureRegion(200, 120, 80, 30),
            },
            frameSource.CapturedRegions);
        Assert.Equal(new[] { "zone-a", "zone-b" }, ocrEngine.Requests.Select(request => request.ZoneId));
        Assert.Equal(2, result.ZoneResults.Count);
        Assert.Empty(result.ZoneFailures);
        Assert.Equal(2, result.RecognizedBlockCount);
        Assert.Equal(2, result.TranslatedBlockCount);
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
        Assert.True(overlay.IsVisible);
        Assert.Equal(profile.OverlaySettings, result.OverlaySnapshot.OverlaySettings);
        Assert.Equal(
            new[] { "Translated Text zone-a", "Translated Text zone-b" },
            result.OverlaySnapshot.TextItems.Select(item => item.Text));
        Assert.Equal(new[] { 10, 200 }, result.OverlaySnapshot.TextItems.Select(item => item.X));
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenLaterZoneCompletesFirst_PublishesReadyZoneBeforeSlowZoneCompletes()
    {
        var slowZone = CreateZone("zone-slow", "Complex", new AbsoluteRectangle(10, 20, 100, 40));
        var readyZone = CreateZone("zone-ready", "Simple", new AbsoluteRectangle(200, 120, 80, 30));
        var profile = CreateProfile(slowZone, readyZone);
        var slowTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlowTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    string.Equals(request.ZoneId, slowZone.Id, StringComparison.Ordinal)
                        ? "Complex text"
                        : "Simple text",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Complex text", StringComparison.Ordinal))
                {
                    slowTranslationStarted.TrySetResult(true);
                    await releaseSlowTranslation.Task.WaitAsync(cancellationToken);
                    return new TranslateResponse(new[] { "Translated Complex text" }, TranslatedAt);
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt.AddMilliseconds(10));
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(new FakeCaptureFrameSource(), ocrEngine, translator, overlay);

        var runTask = service.RunAllZonesAsync(
            profile,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);
        await slowTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await WaitForConditionAsync(
            () => overlay.CurrentSnapshot?.TextItems.Count == 1
                && string.Equals(overlay.CurrentSnapshot.TextItems[0].Text, "Translated Simple text", StringComparison.Ordinal));

        Assert.False(runTask.IsCompleted);
        Assert.Equal(new[] { "Show:1" }, overlay.Events);
        Assert.Equal("Translated Simple text", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);

        releaseSlowTranslation.SetResult(true);
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Empty(result.ZoneFailures);
        Assert.Equal(new[] { "Show:1", "Show:2" }, overlay.Events);
        Assert.Equal(
            new[] { "Translated Complex text", "Translated Simple text" },
            result.OverlaySnapshot.TextItems.Select(item => item.Text));
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
    }

    [Fact]
    public async Task RunAllZonesAsync_DefaultCandidatePipeline_PublishesReadyCandidateBeforeSlowSibling()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var slowCandidateBounds = new BoundingBox(8, 8, 30, 12);
        var readyCandidateBounds = new BoundingBox(55, 8, 30, 12);
        var slowTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlowTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(
                    zone,
                    frameMarker: 1,
                    (slowCandidateBounds, (byte)10),
                    (readyCandidateBounds, (byte)20)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 10 ? "Slow candidate" : "Ready candidate",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[]
            {
                new TextCandidate(slowCandidateBounds, 0.95),
                new TextCandidate(readyCandidateBounds, 0.90),
            }));
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Slow candidate", StringComparison.Ordinal))
                {
                    slowTranslationStarted.TrySetResult(true);
                    await releaseSlowTranslation.Task.WaitAsync(cancellationToken);
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);

        var runTask = service.RunAllZonesAsync(profile);
        await slowTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await WaitForConditionAsync(
            () => overlay.CurrentSnapshot?.TextItems.Count == 1
                && string.Equals(overlay.CurrentSnapshot.TextItems[0].Text, "Translated Ready candidate", StringComparison.Ordinal));

        Assert.False(runTask.IsCompleted);
        Assert.Equal(new[] { "Show:1" }, overlay.Events);

        releaseSlowTranslation.SetResult(true);
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Empty(result.ZoneFailures);
        Assert.Equal(new[] { "Show:1", "Show:2" }, overlay.Events);
        Assert.Equal(
            new[] { "Translated Slow candidate", "Translated Ready candidate" },
            result.OverlaySnapshot.TextItems.Select(item => item.Text));
    }

    [Fact]
    public async Task RunAllZonesAsync_DefaultCandidatePipeline_WhenDetectorIsUnavailable_ReportsDegradedOcrFailure()
    {
        var zone = CreateZone();
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Legacy full-zone text", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var service = CreateService(
            new FakeCaptureFrameSource(),
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            new FakeOverlayService());

        var result = await service.RunAllZonesAsync(CreateProfile(zone));

        Assert.Empty(ocrEngine.Requests);
        var failure = Assert.Single(result.ZoneFailures);
        Assert.Equal(zone.Id, failure.ZoneId);
        Assert.Equal(TranslationPipelineStage.Ocr, failure.Stage);
        Assert.Contains("Candidate-region detector is unavailable", failure.Message, StringComparison.Ordinal);
        Assert.Empty(result.OverlaySnapshot.TextItems);
    }

    [Theory]
    [InlineData("YandexWeb")]
    [InlineData("BingWeb")]
    public async Task LiveSession_WhenExplicitWebProviderIsConfigured_UsesItForRealCandidateWorkWithoutPrewarm(
        string providerId)
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = providerId,
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var detectorCallCount = 0;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, 2, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, 3, (candidateBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[] { new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)) },
        };
        var detector = new FakeCandidateDetector(_ =>
        {
            Interlocked.Increment(ref detectorCallCount);
            return TextCandidateDetectionResult.Available(
                "test-detector",
                new[] { new TextCandidate(candidateBounds, 0.95) });
        });
        var translator = new FakeTranslatorProvider(providerId);
        var overlay = new FakeOverlayService();
        var service = CreateService(frameSource, ocrEngine, translator, overlay, candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true));

        var live = await session.RefreshAsync();
        await WaitForConditionAsync(
            () => overlay.CurrentSnapshot?.TextItems.SingleOrDefault()?.Text == "Translated Candidate");

        Assert.Equal(CandidatePipelineReadinessStatus.Ready, live.CandidateReadiness.Status);
        Assert.Equal(1, detectorCallCount);
        Assert.Equal(1, translator.CallCount);
        Assert.Equal(new[] { "Show:1" }, overlay.Events);
    }

    [Fact]
    public async Task LiveSession_WhenCandidateOcrFails_RecordsImmediateAndRootCauseDiagnostics()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = Enumerable.Range(1, 8)
                .Select(frameMarker => CreateCandidatePilotPixels(
                    zone,
                    (byte)frameMarker,
                    (candidateBounds, (byte)10)))
                .ToArray(),
        };
        var rootCause = new DllNotFoundException("x64 native\tload failed");
        var ocrFailure = new OcrEngineException("Tesseract failed\nfor the candidate crop", rootCause);
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            FailureFactory = _ => ocrFailure,
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        var lifecycleEvents = new List<LiveCandidateLifecycleEvent>();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var update = await session.RefreshAsync();
            lifecycleEvents.AddRange(update.CandidateLifecycleEvents);
            if (lifecycleEvents.Any(entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkFailed))
            {
                break;
            }

            await Task.Yield();
        }

        var failure = Assert.Single(
            lifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkFailed);
        Assert.Equal(TranslationPipelineStage.Ocr, failure.FailureStage);
        Assert.Equal(typeof(OcrEngineException).FullName, failure.FailureExceptionType);
        Assert.Equal("Tesseract failed for the candidate crop", failure.FailureExceptionMessage);
        Assert.Equal(typeof(DllNotFoundException).FullName, failure.FailureRootCauseType);
        Assert.Equal("x64 native load failed", failure.FailureRootCauseMessage);
        Assert.Equal(0, translator.CallCount);
    }

    [Fact]
    public async Task LiveSession_WhenCandidateCaptureIsLost_RemovesPublishedOverlayWithoutBlockingFutureRefreshes()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "GoogleWeb",
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, 2, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, 3, (candidateBounds, (byte)10)),
            },
            FailureFactory = (_, captureIndex) => captureIndex == 3
                ? new CaptureFrameSourceException("Capture source unavailable.")
                : null,
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[] { new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)) },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("GoogleWeb"),
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true));

        await session.RefreshAsync();
        await session.RefreshAsync();
        var published = await session.RefreshAsync();
        Assert.Equal(CandidatePipelineReadinessStatus.Ready, published.CandidateReadiness.Status);
        Assert.Single(published.BatchResult.OverlaySnapshot.TextItems);

        var afterCaptureLoss = await session.RefreshAsync();

        Assert.Equal(CandidatePipelineReadinessStatus.Ready, afterCaptureLoss.CandidateReadiness.Status);
        Assert.Empty(afterCaptureLoss.BatchResult.OverlaySnapshot.TextItems);
        Assert.Equal(new[] { "Show:1", "Show:0" }, overlay.Events);
    }

    [Fact]
    public async Task LiveSession_WhenDetectorRecoveryInvalidatesOldCandidate_DoesNotPublishLateOldResult()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "GoogleWeb",
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = Enumerable.Range(1, 6)
                .Select(frameMarker => CreateCandidatePilotPixels(
                    zone,
                    (byte)frameMarker,
                    (candidateBounds, (byte)10)))
                .ToArray(),
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[] { new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)) },
        };
        var detectorCallCount = 0;
        var detector = new FakeCandidateDetector(_ => Interlocked.Increment(ref detectorCallCount) == 3
            ? TextCandidateDetectionResult.Unavailable("test-detector", "Worker lost")
            : TextCandidateDetectionResult.Available(
                "test-detector",
                new[] { new TextCandidate(candidateBounds, 0.95) }));
        var oldCandidateTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldCandidateTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var translator = new FakeTranslatorProvider(
            "GoogleWeb",
            translateAsync: async (request, _) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Candidate", StringComparison.Ordinal))
                {
                    oldCandidateTranslationStarted.TrySetResult(true);
                    await releaseOldCandidateTranslation.Task;
                }

                return new TranslateResponse(
                    new[] { $"Translated {text}" },
                    TranslatedAt,
                    providerId: "GoogleWeb");
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(frameSource, ocrEngine, translator, overlay, candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true));

        await session.RefreshAsync();
        await session.RefreshAsync();
        var detectorUnavailable = await session.RefreshAsync();
        await oldCandidateTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(CandidatePipelineReadinessStatus.Ready, detectorUnavailable.CandidateReadiness.Status);
        Assert.Contains(detectorUnavailable.CancelledZoneIds, id => id.Contains(":candidate:", StringComparison.Ordinal));
        Assert.Empty(detectorUnavailable.BatchResult.OverlaySnapshot.TextItems);

        releaseOldCandidateTranslation.TrySetResult(true);
        var recovered = await session.RefreshAsync();
        Assert.Equal(CandidatePipelineReadinessStatus.Ready, recovered.CandidateReadiness.Status);
        for (var attempt = 0; attempt < 20 && overlay.CurrentSnapshot?.TextItems.Count != 1; attempt++)
        {
            await Task.Delay(10);
            await session.RefreshAsync();
        }

        Assert.Single(overlay.CurrentSnapshot!.TextItems);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenCandidatePilotUsesMultipleRegions_BoundsCacheMissTranslationsToThree()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var bounds = new[]
        {
            new BoundingBox(4, 4, 18, 12),
            new BoundingBox(28, 4, 18, 12),
            new BoundingBox(52, 4, 18, 12),
            new BoundingBox(76, 4, 18, 12),
        };
        var releaseTranslations = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var threeTranslationsStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeTranslations = 0;
        var maxActiveTranslations = 0;
        var startedTranslations = 0;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(
                    zone,
                    frameMarker: 1,
                    (bounds[0], (byte)10),
                    (bounds[1], (byte)20),
                    (bounds[2], (byte)30),
                    (bounds[3], (byte)40)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock($"Candidate {request.Frame.PixelData.Span[0]}", new BoundingBox(0, 0, 12, 10)),
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            bounds.Select(candidateBounds => new TextCandidate(candidateBounds, 0.95))));
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var active = Interlocked.Increment(ref activeTranslations);
                int observed;
                do
                {
                    observed = maxActiveTranslations;
                    if (observed >= active)
                    {
                        break;
                    }
                }
                while (Interlocked.CompareExchange(ref maxActiveTranslations, active, observed) != observed);

                if (Interlocked.Increment(ref startedTranslations) == 3)
                {
                    threeTranslationsStarted.TrySetResult(true);
                }

                try
                {
                    await releaseTranslations.Task.WaitAsync(cancellationToken);
                    return new TranslateResponse(
                        request.Texts.Select(text => $"Translated {text}"),
                        TranslatedAt);
                }
                finally
                {
                    Interlocked.Decrement(ref activeTranslations);
                }
            });
        var service = CreateService(frameSource, ocrEngine, translator, new FakeOverlayService(), candidateDetector: detector);

        var runTask = service.RunAllZonesAsync(
            profile,
            runOptions: new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true));
        await threeTranslationsStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(3, Volatile.Read(ref maxActiveTranslations));
        Assert.Equal(3, translator.CallCount);

        releaseTranslations.TrySetResult(true);
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(4, translator.CallCount);
        Assert.Equal(4, result.TranslatedBlockCount);
        Assert.Equal(3, Volatile.Read(ref maxActiveTranslations));
        Assert.Empty(result.ZoneFailures);
    }

    [Fact]
    public async Task RunAllZonesAsync_CandidatePipelineDoesNotRequireLivePreflight()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine(),
            new FakeTranslatorProvider("GoogleWeb"),
            new FakeOverlayService());

        var result = await service.RunAllZonesAsync(
            CreateProfile(zone),
            runOptions: new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true));

        var failure = Assert.Single(result.ZoneFailures);
        Assert.Equal(TranslationPipelineStage.Ocr, failure.Stage);
    }

    [Fact]
    public async Task RunAllZonesAsync_VerticalJapaneseCandidatePipeline_RejectsWideRepeatedOcrResult()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 8, 80, 10);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
            },
        };
        var repeatedBlocks = Enumerable.Range(0, 10)
            .Select(index => new OcrTextBlock("字幕", new BoundingBox(index * 2, 0, 2, 8)))
            .ToArray();
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => repeatedBlocks,
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            candidateDetector: detector,
            candidateRegionOcrOptions: new TextCandidateRegionOcrOptions
            {
                EnableCjkTargetPostFilter = true,
            });

        var result = await service.RunAllZonesAsync(
            profile,
            runOptions: new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));

        Assert.Single(ocrEngine.Requests);
        Assert.Equal(OcrOrientationMode.Vertical, ocrEngine.Requests[0].OrientationMode);
        Assert.Equal(0, translator.CallCount);
        Assert.Equal(0, result.RecognizedBlockCount);
        Assert.Equal(0, result.TranslatedBlockCount);
        Assert.Empty(result.OverlaySnapshot.TextItems);
        Assert.Empty(result.ZoneFailures);
    }

    [Fact]
    public async Task LiveSession_VerticalJapaneseCandidatePipeline_RejectsWideRepeatedOcrResult()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 8, 80, 10);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
            },
        };
        var repeatedBlocks = Enumerable.Range(0, 10)
            .Select(index => new OcrTextBlock("字幕", new BoundingBox(index * 2, 0, 2, 8)))
            .ToArray();
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => repeatedBlocks,
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            candidateDetector: detector,
            candidateRegionOcrOptions: new TextCandidateRegionOcrOptions
            {
                EnableCjkTargetPostFilter = true,
            });

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        var update = await session.RefreshAsync();
        if (!update.CandidateLifecycleEvents.Any(
                entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCompleted))
        {
            await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
            update = await session.PublishCompletedWorkAsync();
        }

        Assert.Single(ocrEngine.Requests);
        Assert.Equal(0, translator.CallCount);
        Assert.Empty(update.BatchResult.OverlaySnapshot.TextItems);
        Assert.Contains(
            update.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCompleted
                && entry.RecognizedBlockCount == 0
                && entry.TranslationInputBlockCount == 0
                && entry.TranslatedBlockCount == 0);
    }

    [Fact]
    public async Task LiveSession_VerticalJapaneseCandidate_WhenYandexRepeatsTranslation_PublishesCollapsedText()
    {
        const string sourceText = "\u3053\u306e\u30b2\u30fc\u30e0\u304c\u5927\u597d\u304d";
        const string repeatedTranslation = "I love this game. I love this game. I love this game. I love this game. I love this game.";
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "YandexWeb",
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 5, 12, 30);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock(sourceText, new BoundingBox(0, 0, 12, 30)),
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var translator = new FakeTranslatorProvider(
            "YandexWeb",
            translatedTexts: new[] { repeatedTranslation });
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector,
            candidateRegionOcrOptions: new TextCandidateRegionOcrOptions
            {
                EnableCjkTargetPostFilter = true,
            });

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        var update = await session.RefreshAsync();
        if (!update.CandidateLifecycleEvents.Any(
                entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCompleted))
        {
            await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
            update = await session.PublishCompletedWorkAsync();
        }

        Assert.Equal(1, translator.CallCount);
        Assert.Equal(
            new[] { "I love this game." },
            update.BatchResult.OverlaySnapshot.TextItems.Select(item => item.Text));
        Assert.Contains(
            update.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCompleted
                && entry.TranslationOutputSanitizedCount == 1);
    }

    [Fact]
    public async Task LiveSession_VerticalJapaneseCandidate_OrdersOcrColumnsRightToLeftBeforeTranslation()
    {
        const string expectedSourceText = "\u3053\u306e\u30b2\u30fc\u30e0\u304c\u5927\u597d\u304d";
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 80));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 5, 36, 60);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("\u5927\u597d\u304d", new BoundingBox(2, 4, 8, 30)),
                new OcrTextBlock("\u3053\u306e", new BoundingBox(26, 4, 8, 30)),
                new OcrTextBlock("\u30b2\u30fc\u30e0\u304c", new BoundingBox(14, 4, 8, 30)),
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) },
            new TextCandidateDetectionDiagnostics(
                TextCandidateDetectorPreset.Standard,
                TextCandidateDetectorPreset.Standard,
                0.30,
                0.60,
                1.20,
                1,
                0.95,
                0.95,
                0.95)));
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            candidateDetector: detector,
            candidateRegionOcrOptions: new TextCandidateRegionOcrOptions
            {
                EnableCjkTargetPostFilter = true,
            });

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        var update = await session.RefreshAsync();
        if (!update.CandidateLifecycleEvents.Any(
                entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCompleted))
        {
            await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
            update = await session.PublishCompletedWorkAsync();
        }

        Assert.NotNull(translator.Request);
        Assert.Equal(new[] { expectedSourceText }, translator.Request.Texts);
        Assert.Equal(
            new[] { $"Translated {expectedSourceText}" },
            update.BatchResult.OverlaySnapshot.TextItems.Select(item => item.Text));
        var completed = Assert.Single(
            update.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCompleted);
        Assert.Equal(3, completed.RecognizedBlockCount);
        Assert.Equal(1, completed.TranslationInputBlockCount);
        Assert.Equal(1, completed.TranslatedBlockCount);
        Assert.Equal(WritingSystemGroupingProfile.CjkVertical, completed.WritingSystemGroupingProfile);
        Assert.Equal(OcrOrientationMode.Vertical, completed.OcrOrientationMode);
        Assert.Equal(0.95, completed.CandidateConfidence);
        var detectionCompleted = Assert.Single(
            update.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateDetectionCompleted);
        Assert.Equal(TextCandidateDetectorPreset.Standard, detectionCompleted.RequestedDetectorPreset);
        Assert.Equal(TextCandidateDetectorPreset.Standard, detectionCompleted.EffectiveDetectorPreset);
        Assert.Equal(0.60, detectionCompleted.DetectorBoxThreshold);
        Assert.Equal(1, detectionCompleted.RawDetectorCandidateCount);
        Assert.Equal(0.95, detectionCompleted.AverageDetectorConfidence);
        Assert.Equal(
            new[]
            {
                new BoundingBox(2, 4, 8, 30),
                new BoundingBox(26, 4, 8, 30),
                new BoundingBox(14, 4, 8, 30),
            },
            completed.OrderedOcrBlockBounds);
        Assert.Equal(
            new[]
            {
                new BoundingBox(26, 4, 8, 30),
                new BoundingBox(14, 4, 8, 30),
                new BoundingBox(2, 4, 8, 30),
            },
            completed.OrderedGroupedMemberBounds);
        Assert.Equal(3, completed.OrderedOcrBlockBoundsCount);
        Assert.Equal(3, completed.OrderedGroupedMemberBoundsCount);
        Assert.NotNull(completed.OrderedOcrBlockBoundsFingerprint);
        Assert.NotNull(completed.OrderedGroupedMemberBoundsFingerprint);
        Assert.NotEqual(
            completed.OrderedOcrBlockBoundsFingerprint,
            completed.OrderedGroupedMemberBoundsFingerprint);
    }

    [Fact]
    public void LiveCandidateLifecycleEvent_OrderedGeometryDiagnosticsAreBoundedAndFingerprintTheFullSequence()
    {
        var bounds = Enumerable.Range(0, 130)
            .Select(index => new BoundingBox(index, index + 1, 8, 30))
            .ToArray();

        var lifecycleEvent = new LiveCandidateLifecycleEvent(
            sequence: 1,
            refreshSequence: 1,
            occurredAt: DateTimeOffset.UnixEpoch,
            kind: LiveCandidateLifecycleEventKind.CandidateWorkCompleted,
            orderedOcrBlockBounds: bounds,
            orderedGroupedMemberBounds: bounds.Reverse(),
            writingSystemGroupingProfile: WritingSystemGroupingProfile.CjkVertical,
            ocrOrientationMode: OcrOrientationMode.Vertical);

        Assert.Equal(130, lifecycleEvent.OrderedOcrBlockBoundsCount);
        Assert.Equal(128, lifecycleEvent.OrderedOcrBlockBounds.Count);
        Assert.Equal(130, lifecycleEvent.OrderedGroupedMemberBoundsCount);
        Assert.Equal(128, lifecycleEvent.OrderedGroupedMemberBounds.Count);
        Assert.NotEqual(
            lifecycleEvent.OrderedOcrBlockBoundsFingerprint,
            lifecycleEvent.OrderedGroupedMemberBoundsFingerprint);
    }

    [Theory]
    [InlineData("ja", "jpn_vert", true)]
    [InlineData("en", "eng", false)]
    public async Task LiveSession_TextThatGrowsByPrefix_AppliesAdditionalQuietWindowOnlyToVerticalCjk(
        string sourceLanguage,
        string ocrLanguage,
        bool expectGrowthGuard)
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 80)) with
        {
            OcrLanguage = ocrLanguage,
        };
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = sourceLanguage,
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 5, 36, 60);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (candidateBounds, (byte)20)),
                CreateCandidatePilotPixels(zone, frameMarker: 3, (candidateBounds, (byte)20)),
                CreateCandidatePilotPixels(zone, frameMarker: 4, (candidateBounds, (byte)20)),
            },
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime.AddMilliseconds(100),
                FrameTime.AddMilliseconds(399),
                FrameTime.AddMilliseconds(400),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            RecognizedAtFrames = new[]
            {
                OcrTime,
                OcrTime.AddMilliseconds(100),
                OcrTime.AddMilliseconds(399),
                OcrTime.AddMilliseconds(400),
            },
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 10 ? "Partial" : "Partial complete",
                    new BoundingBox(0, 0, 12, 30)),
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            candidateDetector: detector);
        var options = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.Zero,
            enableCandidateDetectorPilot: true,
            minimumCandidateGroupingObservations: 1,
            minimumStableTextObservations: 1)
        {
            MinimumCandidateGroupingDuration = TimeSpan.Zero,
        };

        using var session = service.CreateLiveSession(profile, options);
        await RefreshAndPublishAsync();
        Assert.Equal(1, translator.CallCount);

        var growing = await RefreshAndPublishAsync();
        Assert.Equal(expectGrowthGuard ? 1 : 2, translator.CallCount);
        if (!expectGrowthGuard)
        {
            Assert.DoesNotContain(
                growing.CandidateLifecycleEvents,
                entry => entry.TextStability?.TypewriterGrowthGuardApplied == true);
            Assert.Equal(new[] { "Partial complete" }, translator.Request!.Texts);
            return;
        }

        Assert.Contains(
            growing.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkDeferredForStability
                && entry.TextStability?.TypewriterGrowthGuardApplied == true
                && entry.TextStability.RequiredDuration == TimeSpan.FromMilliseconds(300));

        await RefreshAndPublishAsync();
        Assert.Equal(1, translator.CallCount);

        await RefreshAndPublishAsync();
        Assert.Equal(2, translator.CallCount);
        Assert.Equal(new[] { "Partial complete" }, translator.Request!.Texts);

        async Task<LiveTranslationPipelineUpdate> RefreshAndPublishAsync()
        {
            var update = await session.RefreshAsync();
            if (update.CandidateLifecycleEvents.Any(entry =>
                    entry.Kind is LiveCandidateLifecycleEventKind.CandidateWorkCompleted
                        or LiveCandidateLifecycleEventKind.CandidateWorkDeferredForStability))
            {
                return update;
            }

            await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
            return await session.PublishCompletedWorkAsync();
        }
    }

    [Fact]
    public void LiveCandidateLifecycleEvent_LocalTextDiagnosticsAreBoundedAndSingleLine()
    {
        var texts = Enumerable.Range(0, 18)
            .Select(index => $"Entry {index}\r\n{new string('x', 600)}")
            .ToArray();
        texts[0] = $"{new string('x', 511)}\U0001F600tail";

        var lifecycleEvent = new LiveCandidateLifecycleEvent(
            sequence: 1,
            refreshSequence: 1,
            occurredAt: DateTimeOffset.UnixEpoch,
            kind: LiveCandidateLifecycleEventKind.CandidateWorkCompleted,
            ocrTexts: texts,
            translationInputTexts: texts,
            translatedTexts: texts);

        Assert.Equal(18, lifecycleEvent.OcrTextCount);
        Assert.Equal(16, lifecycleEvent.OcrTexts.Count);
        Assert.Equal(18, lifecycleEvent.TranslationInputTextCount);
        Assert.Equal(16, lifecycleEvent.TranslationInputTexts.Count);
        Assert.Equal(18, lifecycleEvent.TranslatedTextCount);
        Assert.Equal(16, lifecycleEvent.TranslatedTexts.Count);
        Assert.All(
            lifecycleEvent.OcrTexts
                .Concat(lifecycleEvent.TranslationInputTexts)
                .Concat(lifecycleEvent.TranslatedTexts),
            text =>
            {
                Assert.DoesNotContain('\r', text);
                Assert.DoesNotContain('\n', text);
                Assert.True(text.Length <= 512);
                Assert.False(char.IsHighSurrogate(text[^1]));
            });
    }

    [Fact]
    public async Task LiveSession_CandidateWorkStarted_IsRecordedBeforeOcrBegins()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        DateTimeOffset? ocrStartedAt = null;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            OnRecognize = () => ocrStartedAt = DateTimeOffset.UtcNow,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            new FakeOverlayService(),
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));

        var update = await session.RefreshAsync();

        var startedEvent = Assert.Single(
            update.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkStarted);
        Assert.NotNull(ocrStartedAt);
        Assert.True(startedEvent.OccurredAt <= ocrStartedAt.Value);
    }

    [Fact]
    public async Task LiveSession_WhenCandidateCompletesBetweenDetectorPolls_PublishesFromCompletionSignal()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var translationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                translationStarted.TrySetResult(true);
                await releaseTranslation.Task.WaitAsync(cancellationToken);
                return new TranslateResponse(new[] { $"Translated {Assert.Single(request.Texts)}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateCandidateLiveService(zone, candidateBounds, translator, overlay);

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        var refreshUpdate = await session.RefreshAsync();
        await translationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(refreshUpdate.OverlayChanged);

        releaseTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var completionUpdate = await session.PublishCompletedWorkAsync();

        Assert.True(completionUpdate.OverlayChanged);
        Assert.Equal(
            new[] { "Translated Candidate" },
            completionUpdate.BatchResult.OverlaySnapshot.TextItems.Select(item => item.Text));
        var completedEvent = Assert.Single(
            completionUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCompleted);
        Assert.Equal(new[] { "Candidate" }, completedEvent.OcrTexts);
        Assert.Equal(new[] { "Candidate" }, completedEvent.TranslationInputTexts);
        Assert.Equal(new[] { "Translated Candidate" }, completedEvent.TranslatedTexts);
        Assert.Contains(
            completionUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.OverlaySnapshotPublished);
    }

    [Fact]
    public async Task LiveSession_WhenRevisionChangesBeforeCompletedWorkIsPublished_DoesNotPublishStaleResult()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var oldTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseNewTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (candidateBounds, (byte)30)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 10 ? "Old candidate" : "New candidate",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Old candidate", StringComparison.Ordinal))
                {
                    oldTranslationStarted.TrySetResult(true);
                    await releaseOldTranslation.Task;
                }
                else
                {
                    await releaseNewTranslation.Task.WaitAsync(cancellationToken);
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);
        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        await oldTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var revisionUpdate = await session.RefreshAsync();
        Assert.Contains(
            revisionUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateSourceChanged
                && entry.CandidateRevision == 2);

        releaseOldTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var staleCompletionUpdate = await session.PublishCompletedWorkAsync();

        Assert.False(staleCompletionUpdate.OverlayChanged);
        Assert.Empty(staleCompletionUpdate.BatchResult.OverlaySnapshot.TextItems);
        Assert.DoesNotContain(
            overlay.Events,
            entry => string.Equals(entry, "Show:1", StringComparison.Ordinal));

        releaseNewTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var currentCompletionUpdate = await session.PublishCompletedWorkAsync();
        Assert.Equal(
            new[] { "Translated New candidate" },
            currentCompletionUpdate.BatchResult.OverlaySnapshot.TextItems.Select(item => item.Text));
    }

    [Fact]
    public async Task LiveSession_WhenPublishedCandidateSourceChanges_KeepsOverlayUntilReplacementCompletes()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var oldTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseNewTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (candidateBounds, (byte)30)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 10 ? "Old candidate" : "New candidate",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Old candidate", StringComparison.Ordinal))
                {
                    oldTranslationStarted.TrySetResult(true);
                    await releaseOldTranslation.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    newTranslationStarted.TrySetResult(true);
                    await releaseNewTranslation.Task.WaitAsync(cancellationToken);
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);
        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        await oldTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        releaseOldTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var oldCompletionUpdate = await session.PublishCompletedWorkAsync();
        Assert.Equal(
            new[] { "Translated Old candidate" },
            oldCompletionUpdate.BatchResult.OverlaySnapshot.TextItems.Select(item => item.Text));

        var revisionUpdate = await session.RefreshAsync();
        await newTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Contains(
            revisionUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateSourceChanged
                && entry.CandidateRevision == 2);
        Assert.Empty(revisionUpdate.BatchResult.OverlaySnapshot.TextItems);
        Assert.False(revisionUpdate.OverlayChanged);
        Assert.Equal(
            new[] { "Translated Old candidate" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
        Assert.DoesNotContain(overlay.Events, entry => string.Equals(entry, "Show:0", StringComparison.Ordinal));

        releaseNewTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var newCompletionUpdate = await session.PublishCompletedWorkAsync();

        Assert.True(newCompletionUpdate.OverlayChanged);
        Assert.Equal(
            new[] { "Translated New candidate" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
        Assert.Equal(new[] { "Show:1", "Show:1" }, overlay.Events);
    }

    [Fact]
    public async Task LiveSession_WhenBingReplacementTimesOut_KeepsPublishedOverlayAndReportsFailure()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var nextRetryAt = new DateTimeOffset(2026, 9, 2, 18, 30, 0, TimeSpan.Zero);
        var oldTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReplacementFailure = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (candidateBounds, (byte)30)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 10 ? "Old candidate" : "Replacement candidate",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider(
            "BingWeb",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                var diagnostics = Assert.IsType<TranslationProviderRequestDiagnostics>(request.Diagnostics);
                var requestStartedAt = DateTimeOffset.UtcNow;
                diagnostics.MarkProviderInvocationStarted("BingWeb", requestStartedAt);
                var networkAttemptId = diagnostics.MarkNetworkRequestStarted(
                    TranslationProviderNetworkRequestKind.Translation,
                    requestStartedAt);
                if (string.Equals(text, "Old candidate", StringComparison.Ordinal))
                {
                    oldTranslationStarted.TrySetResult(true);
                    await releaseOldTranslation.Task.WaitAsync(cancellationToken);
                    diagnostics.MarkNetworkRequestCompleted(
                        networkAttemptId,
                        TranslationProviderNetworkRequestOutcome.Succeeded,
                        DateTimeOffset.UtcNow,
                        HttpStatusCode.OK);
                    diagnostics.MarkProviderInvocationCompleted(
                        TranslationProviderInvocationOutcome.Succeeded,
                        DateTimeOffset.UtcNow);
                    return new TranslateResponse(new[] { "Translated Old candidate" }, TranslatedAt, "BingWeb");
                }

                replacementTranslationStarted.TrySetResult(true);
                await releaseReplacementFailure.Task.WaitAsync(cancellationToken);
                diagnostics.MarkNetworkRequestCompleted(
                    networkAttemptId,
                    TranslationProviderNetworkRequestOutcome.Timeout,
                    DateTimeOffset.UtcNow);
                diagnostics.MarkProviderInvocationCompleted(
                    TranslationProviderInvocationOutcome.Failed,
                    DateTimeOffset.UtcNow,
                    TranslatorProviderFailureKind.Timeout);
                throw new TranslatorProviderException(
                    "BingWeb",
                    TranslatorProviderFailureKind.Timeout,
                    "BingWeb did not respond within 15 seconds.",
                    retryAfter: TimeSpan.FromSeconds(60),
                    consecutiveFailureCount: 2,
                    nextRetryAt: nextRetryAt);
            });
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "BingWeb",
                SourceLanguage = "en",
                TargetLanguage = "ru",
            },
        };

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        await oldTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        releaseOldTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await session.PublishCompletedWorkAsync();
        Assert.Equal("Translated Old candidate", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);

        var replacementUpdate = await session.RefreshAsync();
        await replacementTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(replacementUpdate.OverlayChanged);
        releaseReplacementFailure.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var failureUpdate = await session.PublishCompletedWorkAsync();

        var failure = Assert.Single(failureUpdate.BatchResult.ZoneFailures);
        Assert.Equal(TranslationPipelineStage.Translation, failure.Stage);
        Assert.IsType<TranslatorProviderException>(failure.Exception.InnerException);
        Assert.False(failureUpdate.OverlayChanged);
        Assert.Equal("Translated Old candidate", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);
        Assert.DoesNotContain(overlay.Events, entry => string.Equals(entry, "Show:0", StringComparison.Ordinal));
        var lifecycleFailure = Assert.Single(
            failureUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkFailed);
        Assert.Equal("BingWeb", lifecycleFailure.FailureProviderId);
        Assert.Equal(TranslatorProviderFailureKind.Timeout, lifecycleFailure.FailureProviderKind);
        Assert.Null(lifecycleFailure.FailureProviderHttpStatusCode);
        Assert.True(lifecycleFailure.FailureProviderPaused);
        Assert.Equal(TimeSpan.FromSeconds(60), lifecycleFailure.FailureProviderRetryAfter);
        Assert.Equal(nextRetryAt, lifecycleFailure.FailureProviderNextRetryAt);
        Assert.Equal(2, lifecycleFailure.FailureProviderConsecutiveFailureCount);
        var providerAttempt = Assert.Single(
            failureUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateProviderRequestObserved);
        Assert.True(providerAttempt.ProviderNetworkRequestSent);
        Assert.Equal(TranslationProviderNetworkRequestKind.Translation, providerAttempt.ProviderNetworkRequestKind);
        Assert.Equal(TranslationProviderNetworkRequestOutcome.Timeout, providerAttempt.ProviderNetworkRequestOutcome);
        Assert.Null(providerAttempt.ProviderNetworkHttpStatusCode);
        Assert.Equal(new[] { "Replacement candidate" }, providerAttempt.TranslationInputTexts);
    }

    [Fact]
    public async Task LiveSession_WhenDeferredReplacementDisappears_PublishesConfirmedEmptyOverlay()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var oldTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementTranslationCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (candidateBounds, (byte)30)),
                CreateCandidatePilotPixels(zone, frameMarker: 3),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 10 ? "Old candidate" : "Replacement candidate",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Old candidate", StringComparison.Ordinal))
                {
                    oldTranslationStarted.TrySetResult(true);
                    await releaseOldTranslation.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    replacementTranslationStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        replacementTranslationCancelled.TrySetResult(true);
                        throw;
                    }
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var detectorCallCount = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            Interlocked.Increment(ref detectorCallCount) <= 2
                ? new[] { new TextCandidate(candidateBounds, 0.95) }
                : Array.Empty<TextCandidate>()));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        await oldTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        releaseOldTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await session.PublishCompletedWorkAsync();

        var replacementUpdate = await session.RefreshAsync();
        await replacementTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(replacementUpdate.OverlayChanged);
        Assert.Equal(
            new[] { "Translated Old candidate" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));

        var disappearanceUpdate = await session.RefreshAsync();
        await replacementTranslationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(disappearanceUpdate.OverlayChanged);
        Assert.Empty(disappearanceUpdate.BatchResult.OverlaySnapshot.TextItems);
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
        Assert.Equal(new[] { "Show:1", "Show:0" }, overlay.Events);
    }

    [Fact]
    public async Task LiveSession_WhenPublishedCandidateHasOneEmptyDetectorFrame_KeepsOverlayUntilItReturns()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2),
                CreateCandidatePilotPixels(zone, frameMarker: 3, (candidateBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detectorCallCount = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            Interlocked.Increment(ref detectorCallCount) == 2
                ? Array.Empty<TextCandidate>()
                : new[] { new TextCandidate(candidateBounds, 0.95) }));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true,
                minimumCandidateOverlayVisibleDuration: TimeSpan.FromSeconds(2)));

        var publishedUpdate = await session.RefreshAsync();
        var dropoutUpdate = await session.RefreshAsync();
        var recoveredUpdate = await session.RefreshAsync();

        Assert.True(publishedUpdate.OverlayChanged);
        Assert.False(dropoutUpdate.OverlayChanged);
        Assert.Empty(dropoutUpdate.BatchResult.OverlaySnapshot.TextItems);
        Assert.Equal("Translated Candidate", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);
        Assert.True(recoveredUpdate.OverlayChanged);
        Assert.DoesNotContain(overlay.Events, entry => string.Equals(entry, "Show:0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LiveSession_WhenEmptyDetectorFramesOutlastReadabilityGrace_RemovesPublishedOverlay()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2),
                CreateCandidatePilotPixels(zone, frameMarker: 3),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detectorCallCount = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            Interlocked.Increment(ref detectorCallCount) == 1
                ? new[] { new TextCandidate(candidateBounds, 0.95) }
                : Array.Empty<TextCandidate>()));
        var overlay = new FakeOverlayService();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            overlay,
            candidateDetector: detector,
            timeProvider: timeProvider);

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true,
                minimumCandidateOverlayVisibleDuration: TimeSpan.FromSeconds(2)));

        await session.RefreshAsync();
        var withinGrace = await session.RefreshAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var afterGrace = await session.RefreshAsync();

        Assert.False(withinGrace.OverlayChanged);
        Assert.True(afterGrace.OverlayChanged);
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
        Assert.Equal(new[] { "Show:1", "Show:0" }, overlay.Events);
    }

    [Fact]
    public async Task LiveSession_WhenCandidateReturnsWithDifferentSourceDuringReadabilityGrace_RemovesStaleOverlayImmediately()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 3),
                CreateCandidatePilotPixels(zone, frameMarker: 4, (candidateBounds, (byte)20)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detectorCallCount = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            Interlocked.Increment(ref detectorCallCount) == 3
                ? Array.Empty<TextCandidate>()
                : new[] { new TextCandidate(candidateBounds, 0.95) }));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(
                requireStableTextBeforeTranslation: true,
                stableTextInterval: TimeSpan.Zero,
                enableCandidateDetectorPilot: true,
                minimumCandidateOverlayVisibleDuration: TimeSpan.FromSeconds(2)));

        await session.RefreshAsync();
        var published = await session.RefreshAsync();
        var dropout = await session.RefreshAsync();
        var changedSource = await session.RefreshAsync();

        Assert.True(published.OverlayChanged);
        Assert.False(dropout.OverlayChanged);
        Assert.True(changedSource.OverlayChanged);
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
        Assert.Equal(new[] { "Show:1", "Show:0" }, overlay.Events);
    }

    [Fact]
    public async Task LiveSession_WhenCandidateDisappearsBeforeLateCompletion_DoesNotRepublishIt()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var translationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, _) =>
            {
                translationStarted.TrySetResult(true);
                await releaseTranslation.Task;
                return new TranslateResponse(new[] { $"Translated {Assert.Single(request.Texts)}" }, TranslatedAt);
            });
        var detectorCallCount = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            Interlocked.Increment(ref detectorCallCount) == 1
                ? new[] { new TextCandidate(candidateBounds, 0.95) }
                : Array.Empty<TextCandidate>()));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        await translationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var disappearanceUpdate = await session.RefreshAsync();
        Assert.Contains(
            disappearanceUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateRemoved
                && entry.CancellationReason == LiveCandidateCancellationReason.CandidateDisappeared);

        releaseTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var completionUpdate = await session.PublishCompletedWorkAsync();

        Assert.False(completionUpdate.OverlayChanged);
        Assert.Empty(completionUpdate.BatchResult.OverlaySnapshot.TextItems);
        Assert.DoesNotContain(overlay.Events, entry => string.Equals(entry, "Show:1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LiveSession_WhenStoppedWhileWaitingForCompletion_CancelsTheWaitAndPublishesNothing()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var translationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, _) =>
            {
                translationStarted.TrySetResult(true);
                await releaseTranslation.Task;
                return new TranslateResponse(new[] { $"Translated {Assert.Single(request.Texts)}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateCandidateLiveService(zone, candidateBounds, translator, overlay);
        using var sessionCancellation = new CancellationTokenSource();
        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true),
            sessionCancellation.Token);
        await session.RefreshAsync();
        await translationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var completionWait = session.WaitForWorkCompletionAsync();
        sessionCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completionWait);
        Assert.Null(overlay.CurrentSnapshot);
        releaseTranslation.TrySetResult(true);
    }

    [Fact]
    public async Task LiveSession_WhenConcurrentCandidatesComplete_PublishesEachWithoutWaitingForItsSibling()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var firstBounds = new BoundingBox(8, 8, 30, 12);
        var secondBounds = new BoundingBox(55, 8, 30, 12);
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(
                    zone,
                    frameMarker: 1,
                    (firstBounds, (byte)10),
                    (secondBounds, (byte)20)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 10 ? "First" : "Second",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "First", StringComparison.Ordinal))
                {
                    firstStarted.TrySetResult(true);
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    secondStarted.TrySetResult(true);
                    await releaseSecond.Task.WaitAsync(cancellationToken);
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[]
            {
                new TextCandidate(firstBounds, 0.95),
                new TextCandidate(secondBounds, 0.90),
            }));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        await Task.WhenAll(
            firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1)),
            secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(1)));

        releaseSecond.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var secondCompletion = await session.PublishCompletedWorkAsync();
        Assert.Equal(
            new[] { "Translated Second" },
            secondCompletion.BatchResult.OverlaySnapshot.TextItems.Select(item => item.Text));

        releaseFirst.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var firstCompletion = await session.PublishCompletedWorkAsync();
        Assert.Equal(
            new[] { "Translated First", "Translated Second" },
            firstCompletion.BatchResult.OverlaySnapshot.TextItems.Select(item => item.Text));
    }

    [Fact]
    public void LiveSession_CandidateReconciliation_DoesNotCreateAFullZoneFingerprintCopy()
    {
        var source = File.ReadAllText(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.Application",
                "Pipeline",
                "TranslationPipelineService.cs"));
        var methodStart = source.IndexOf(
            "private async Task<bool> ReconcileCandidateRegionsAsync",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private static LiveCandidateState? FindMatchingCandidateState",
            methodStart,
            StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);
        var candidateReconciliation = source[methodStart..methodEnd];
        Assert.DoesNotContain(
            "FrameFingerprint.FromFrame(capturedZone.Frame)",
            candidateReconciliation,
            StringComparison.Ordinal);
        Assert.Contains(
            "FrameFingerprint.FromFrame(region.Frame)",
            candidateReconciliation,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveSession_WhenOneZoneIdentityChanges_CancelsOnlyThatZoneAndKeepsReadySiblingOverlay()
    {
        var slowZone = CreateZone("zone-slow", "Complex", new AbsoluteRectangle(10, 20, 100, 40));
        var readyZone = CreateZone("zone-ready", "Simple", new AbsoluteRectangle(200, 120, 80, 30));
        var profile = CreateProfile(slowZone, readyZone);
        var oldSlowTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldSlowTranslationCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreatePixels(slowZone, 10),
                CreatePixels(readyZone, 20),
                CreatePixels(slowZone, 30),
                CreatePixels(readyZone, 20),
            },
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime,
                FrameTime.AddMilliseconds(250),
                FrameTime.AddMilliseconds(250),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = request =>
            {
                var marker = request.Frame.PixelData.Span[0];
                var text = string.Equals(request.ZoneId, slowZone.Id, StringComparison.Ordinal)
                    ? marker == 10 ? "Slow old" : "Slow new"
                    : "Ready";
                return new[] { new OcrTextBlock(text, new BoundingBox(0, 0, 20, 10)) };
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Slow old", StringComparison.Ordinal))
                {
                    oldSlowTranslationStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        oldSlowTranslationCancelled.TrySetResult(true);
                        throw;
                    }
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(frameSource, ocrEngine, translator, overlay);

        using var session = service.CreateLiveSession(
            profile,
            TranslationPipelineRunOptions.LegacyFullPage);
        var firstUpdate = await session.RefreshAsync();
        await oldSlowTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(firstUpdate.OverlayChanged);
        Assert.Equal(new[] { "Translated Ready" }, overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));

        var secondUpdate = await session.RefreshAsync();
        await oldSlowTranslationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(new[] { slowZone.Id }, secondUpdate.CancelledZoneIds);
        Assert.Equal(
            new[] { "Translated Slow new", "Translated Ready" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
        Assert.DoesNotContain(
            overlay.CurrentSnapshot.TextItems,
            item => string.Equals(item.Text, "Translated Slow old", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LiveSession_WhenOneZoneTextDisappears_CancelsThatZoneAndRetainsReadySiblingOverlay()
    {
        var slowZone = CreateZone("zone-slow", "Complex", new AbsoluteRectangle(10, 20, 100, 40));
        var readyZone = CreateZone("zone-ready", "Simple", new AbsoluteRectangle(200, 120, 80, 30));
        var profile = CreateProfile(slowZone, readyZone);
        var oldSlowTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldSlowTranslationCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreatePixels(slowZone, 10),
                CreatePixels(readyZone, 20),
                CreatePixels(slowZone, 0),
                CreatePixels(readyZone, 20),
            },
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime,
                FrameTime.AddMilliseconds(250),
                FrameTime.AddMilliseconds(250),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = request =>
            {
                var marker = request.Frame.PixelData.Span[0];
                if (string.Equals(request.ZoneId, slowZone.Id, StringComparison.Ordinal) && marker == 0)
                {
                    return Array.Empty<OcrTextBlock>();
                }

                var text = string.Equals(request.ZoneId, slowZone.Id, StringComparison.Ordinal)
                    ? "Slow old"
                    : "Ready";
                return new[] { new OcrTextBlock(text, new BoundingBox(0, 0, 20, 10)) };
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Slow old", StringComparison.Ordinal))
                {
                    oldSlowTranslationStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        oldSlowTranslationCancelled.TrySetResult(true);
                        throw;
                    }
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(frameSource, ocrEngine, translator, overlay);

        using var session = service.CreateLiveSession(
            profile,
            TranslationPipelineRunOptions.LegacyFullPage);
        await session.RefreshAsync();
        await oldSlowTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var secondUpdate = await session.RefreshAsync();
        await oldSlowTranslationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(new[] { slowZone.Id }, secondUpdate.CancelledZoneIds);
        Assert.Equal(new[] { "Translated Ready" }, overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
    }

    [Fact]
    public async Task LiveSession_WhenOneOfTwoPendingZonesChanges_CancelsOnlyChangedZone()
    {
        var changedZone = CreateZone("zone-changed", "Changing", new AbsoluteRectangle(10, 20, 100, 40));
        var unchangedZone = CreateZone("zone-unchanged", "Stable", new AbsoluteRectangle(200, 120, 80, 30));
        var profile = CreateProfile(changedZone, unchangedZone);
        var oldChangedTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unchangedTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldChangedTranslationCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unchangedTranslationCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUnchangedTranslation = new TaskCompletionSource<bool>();
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreatePixels(changedZone, 10),
                CreatePixels(unchangedZone, 20),
                CreatePixels(changedZone, 30),
                CreatePixels(unchangedZone, 20),
            },
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime,
                FrameTime.AddMilliseconds(250),
                FrameTime.AddMilliseconds(250),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = request =>
            {
                var marker = request.Frame.PixelData.Span[0];
                var text = string.Equals(request.ZoneId, changedZone.Id, StringComparison.Ordinal)
                    ? marker == 10 ? "Changed old" : "Changed new"
                    : "Unchanged";
                return new[] { new OcrTextBlock(text, new BoundingBox(0, 0, 20, 10)) };
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Changed old", StringComparison.Ordinal))
                {
                    oldChangedTranslationStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        oldChangedTranslationCancelled.TrySetResult(true);
                        throw;
                    }
                }

                if (string.Equals(text, "Unchanged", StringComparison.Ordinal))
                {
                    unchangedTranslationStarted.TrySetResult(true);
                    try
                    {
                        await releaseUnchangedTranslation.Task.WaitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        unchangedTranslationCancelled.TrySetResult(true);
                        throw;
                    }
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(frameSource, ocrEngine, translator, overlay);

        using var session = service.CreateLiveSession(
            profile,
            TranslationPipelineRunOptions.LegacyFullPage);
        await session.RefreshAsync();
        await Task.WhenAll(
            oldChangedTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1)),
            unchangedTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1)));

        var secondUpdate = await session.RefreshAsync();
        await oldChangedTranslationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(new[] { changedZone.Id }, secondUpdate.CancelledZoneIds);
        Assert.False(unchangedTranslationCancelled.Task.IsCompleted);
        Assert.Equal(new[] { "Translated Changed new" }, overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
    }

    [Fact]
    public async Task LiveSession_WhenCandidateDisappears_CancelsOnlyThatCandidateAndKeepsReadySiblingOverlay()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var slowCandidateBounds = new BoundingBox(8, 8, 30, 12);
        var readyCandidateBounds = new BoundingBox(55, 8, 30, 12);
        var slowTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowTranslationCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(
                    zone,
                    frameMarker: 1,
                    (slowCandidateBounds, (byte)10),
                    (readyCandidateBounds, (byte)20)),
                CreateCandidatePilotPixels(
                    zone,
                    frameMarker: 2,
                    (readyCandidateBounds, (byte)20)),
            },
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime.AddMilliseconds(250),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request =>
            {
                var text = request.Frame.PixelData.Span[0] == 10 ? "Slow candidate" : "Ready candidate";
                return new[] { new OcrTextBlock(text, new BoundingBox(0, 0, 20, 10)) };
            },
        };
        var detector = new FakeCandidateDetector(request =>
        {
            var candidates = request.Frame.PixelData.Span[0] == 1
                ? new[]
                {
                    new TextCandidate(slowCandidateBounds, 0.95),
                    new TextCandidate(readyCandidateBounds, 0.90),
                }
                : new[] { new TextCandidate(readyCandidateBounds, 0.90) };
            return TextCandidateDetectionResult.Available("test-detector", candidates);
        });
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Slow candidate", StringComparison.Ordinal))
                {
                    slowTranslationStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        slowTranslationCancelled.TrySetResult(true);
                        throw;
                    }
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        var firstUpdate = await session.RefreshAsync();
        await slowTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(firstUpdate.OverlayChanged);
        Assert.Equal(
            new[] { "Translated Ready candidate" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));

        var secondUpdate = await session.RefreshAsync();
        await slowTranslationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            new[] { $"{zone.Id}:candidate:{slowCandidateBounds.X}:{slowCandidateBounds.Y}:{slowCandidateBounds.Width}:{slowCandidateBounds.Height}" },
            secondUpdate.CancelledZoneIds);
        Assert.Contains(
            firstUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateDiscovered
                && entry.CandidateBounds == slowCandidateBounds
                && entry.SourceCandidateBounds.SequenceEqual(new[] { slowCandidateBounds }));
        Assert.Contains(
            secondUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCancelled
                && entry.CandidateBounds == slowCandidateBounds
                && entry.CancellationReason == LiveCandidateCancellationReason.CandidateDisappeared);
        Assert.Contains(
            secondUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateRemoved
                && entry.CandidateBounds == slowCandidateBounds
                && entry.CancellationReason == LiveCandidateCancellationReason.CandidateDisappeared);
        Assert.Contains(
            firstUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.OverlaySnapshotPublished
                && entry.OverlayTextItemCount == 1);
        Assert.Equal(
            new[] { "Translated Ready candidate" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
        Assert.DoesNotContain(
            overlay.CurrentSnapshot.TextItems,
            item => string.Equals(item.Text, "Translated Slow candidate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LiveSession_WhenCandidateIdentityChanges_CancelsOnlyChangedCandidateAndPublishesReplacement()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var changingCandidateBounds = new BoundingBox(8, 8, 30, 12);
        var readyCandidateBounds = new BoundingBox(55, 8, 30, 12);
        var replacementMemberBounds = new[]
        {
            new BoundingBox(8, 8, 14, 12),
            new BoundingBox(24, 8, 14, 12),
        };
        var detectorRequestCount = 0;
        var oldTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldTranslationCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(
                    zone,
                    frameMarker: 1,
                    (changingCandidateBounds, (byte)10),
                    (readyCandidateBounds, (byte)20)),
                CreateCandidatePilotPixels(
                    zone,
                    frameMarker: 2,
                    (changingCandidateBounds, (byte)30),
                    (readyCandidateBounds, (byte)20)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request =>
            {
                var text = request.Frame.PixelData.Span[0] switch
                {
                    10 => "Old candidate",
                    30 => "Replacement candidate",
                    _ => "Ready candidate",
                };
                return new[] { new OcrTextBlock(text, new BoundingBox(0, 0, 20, 10)) };
            },
        };
        var detector = new FakeCandidateDetector(_ =>
        {
            var changingSourceMembers = Interlocked.Increment(ref detectorRequestCount) == 1
                ? new[] { changingCandidateBounds }
                : replacementMemberBounds;
            return TextCandidateDetectionResult.Available(
                "test-detector",
                new[]
                {
                    new TextCandidate(changingCandidateBounds, 0.95)
                    {
                        SourceCandidateBounds = changingSourceMembers,
                    },
                    new TextCandidate(readyCandidateBounds, 0.90),
                });
        });
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Old candidate", StringComparison.Ordinal))
                {
                    oldTranslationStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        oldTranslationCancelled.TrySetResult(true);
                        throw;
                    }
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        await oldTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var secondUpdate = await session.RefreshAsync();
        await oldTranslationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            new[] { $"{zone.Id}:candidate:{changingCandidateBounds.X}:{changingCandidateBounds.Y}:{changingCandidateBounds.Width}:{changingCandidateBounds.Height}" },
            secondUpdate.CancelledZoneIds);
        Assert.Contains(
            secondUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGroupingChanged
                && entry.CandidateBounds == changingCandidateBounds
                && entry.SourceCandidateBounds.SequenceEqual(replacementMemberBounds));
        Assert.Equal(
            new[] { "Translated Replacement candidate", "Translated Ready candidate" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
    }

    [Fact]
    public async Task LiveSession_WhenStableTextIsRequired_WaitsForASecondMatchingGroupingBeforeTranslation()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var partialBounds = new BoundingBox(8, 8, 20, 12);
        var finalBounds = new BoundingBox(8, 8, 50, 12);
        var detectorCallCount = 0;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (partialBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (finalBounds, (byte)20)),
                CreateCandidatePilotPixels(zone, frameMarker: 3, (finalBounds, (byte)20)),
            },
        };
        var detector = new FakeCandidateDetector(_ =>
        {
            var bounds = Interlocked.Increment(ref detectorCallCount) == 1
                ? partialBounds
                : finalBounds;
            return TextCandidateDetectionResult.Available(
                "test-detector",
                new[] { new TextCandidate(bounds, 0.95) });
        });
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 20 ? "Final group" : "Partial group",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google");
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(
                requireStableTextBeforeTranslation: true,
                stableTextInterval: TimeSpan.Zero,
                enableCandidateDetectorPilot: true));

        var firstUpdate = await session.RefreshAsync();
        var secondUpdate = await session.RefreshAsync();

        Assert.Equal(0, translator.CallCount);
        Assert.Contains(
            firstUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGroupingAwaitingConfirmation
                && entry.CandidateBounds == partialBounds);
        Assert.Contains(
            secondUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGroupingAwaitingConfirmation
                && entry.CandidateBounds == finalBounds);

        await session.RefreshAsync();
        await WaitForConditionAsync(
            () => translator.CallCount == 1 && overlay.CurrentSnapshot?.TextItems.Count == 1);

        Assert.Equal(new[] { "Final group" }, translator.Request!.Texts);
        Assert.Equal(
            new[] { "Translated Final group" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
    }

    [Fact]
    public async Task LiveSession_WhenGroupingObservationsArriveFasterThanMinimumDuration_DelaysTranslation()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var candidateBounds = new BoundingBox(8, 8, 50, 12);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = Enumerable.Range(1, 4)
                .Select(frameMarker => CreateCandidatePilotPixels(
                    zone,
                    (byte)frameMarker,
                    (candidateBounds, (byte)20)))
                .ToArray(),
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime.AddMilliseconds(50),
                FrameTime.AddMilliseconds(199),
                FrameTime.AddMilliseconds(200),
            },
        };
        var detector = new FakeCandidateDetector(_ =>
            TextCandidateDetectionResult.Available(
                "test-detector",
                new[] { new TextCandidate(candidateBounds, 0.95) }));
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Stable candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            candidateDetector: detector);
        var options = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.Zero,
            enableCandidateDetectorPilot: true,
            minimumCandidateGroupingObservations: 2,
            minimumStableTextObservations: 1)
        {
            MinimumCandidateGroupingDuration = TimeSpan.FromMilliseconds(200),
        };

        using var session = service.CreateLiveSession(profile, options);
        await session.RefreshAsync();
        await session.RefreshAsync();
        var justBeforeMinimumDuration = await session.RefreshAsync();

        Assert.Equal(0, translator.CallCount);
        Assert.Contains(
            justBeforeMinimumDuration.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGroupingAwaitingConfirmation
                && entry.GroupingObservationCount == 3
                && entry.GroupingObservedDuration == TimeSpan.FromMilliseconds(199)
                && entry.RequiredGroupingDuration == TimeSpan.FromMilliseconds(200));

        var minimumDurationReached = await session.RefreshAsync();
        await WaitForConditionAsync(() => translator.CallCount == 1);

        Assert.Contains(
            minimumDurationReached.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkStarted
                && entry.GroupingObservationCount == 4
                && entry.GroupingObservedDuration == TimeSpan.FromMilliseconds(200)
                && entry.RequiredGroupingDuration == TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task LiveSession_WhenGroupingRevisionChanges_ResetsMinimumGroupingDuration()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var candidateBounds = new BoundingBox(8, 8, 50, 12);
        var revisedMembers = new[]
        {
            new BoundingBox(8, 8, 24, 12),
            new BoundingBox(34, 8, 24, 12),
        };
        var detectorCallCount = 0;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = Enumerable.Range(1, 5)
                .Select(frameMarker => CreateCandidatePilotPixels(
                    zone,
                    (byte)frameMarker,
                    (candidateBounds, (byte)20)))
                .ToArray(),
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime.AddMilliseconds(150),
                FrameTime.AddMilliseconds(200),
                FrameTime.AddMilliseconds(350),
                FrameTime.AddMilliseconds(400),
            },
        };
        var detector = new FakeCandidateDetector(_ =>
        {
            var candidate = new TextCandidate(candidateBounds, 0.95);
            if (Interlocked.Increment(ref detectorCallCount) >= 3)
            {
                candidate = new TextCandidate(candidateBounds, 0.95)
                {
                    SourceCandidateBounds = revisedMembers,
                };
            }

            return TextCandidateDetectionResult.Available("test-detector", new[] { candidate });
        });
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            new FakeOcrEngine
            {
                EngineId = OcrSettings.TesseractEngineId,
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Revised candidate", new BoundingBox(0, 0, 20, 10)),
                },
            },
            translator,
            new FakeOverlayService(),
            candidateDetector: detector);
        var options = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.Zero,
            enableCandidateDetectorPilot: true,
            minimumCandidateGroupingObservations: 2,
            minimumStableTextObservations: 1)
        {
            MinimumCandidateGroupingDuration = TimeSpan.FromMilliseconds(200),
        };

        using var session = service.CreateLiveSession(profile, options);
        await session.RefreshAsync();
        await session.RefreshAsync();
        var revisedGrouping = await session.RefreshAsync();
        var revisedBeforeMinimumDuration = await session.RefreshAsync();

        Assert.Equal(0, translator.CallCount);
        Assert.Contains(
            revisedGrouping.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGroupingChanged
                && entry.CandidateRevision == 2
                && entry.GroupingObservationCount == 1
                && entry.GroupingObservedDuration == TimeSpan.Zero);
        Assert.Contains(
            revisedBeforeMinimumDuration.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGroupingAwaitingConfirmation
                && entry.CandidateRevision == 2
                && entry.GroupingObservationCount == 2
                && entry.GroupingObservedDuration == TimeSpan.FromMilliseconds(150));

        var revisedMinimumDurationReached = await session.RefreshAsync();
        await WaitForConditionAsync(() => translator.CallCount == 1);

        Assert.Contains(
            revisedMinimumDurationReached.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkStarted
                && entry.CandidateRevision == 2
                && entry.GroupingObservationCount == 3
                && entry.GroupingObservedDuration == TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task LiveSession_WhenGroupingRevisionChanges_ResetsMatchingOcrObservationCount()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var candidateBounds = new BoundingBox(8, 8, 50, 12);
        var revisedMembers = new[]
        {
            new BoundingBox(8, 8, 24, 12),
            new BoundingBox(34, 8, 24, 12),
        };
        var detectorCallCount = 0;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = Enumerable.Range(1, 6)
                .Select(frameMarker => CreateCandidatePilotPixels(
                    zone,
                    (byte)frameMarker,
                    (candidateBounds, (byte)20)))
                .ToArray(),
        };
        var detector = new FakeCandidateDetector(_ =>
        {
            var candidate = new TextCandidate(candidateBounds, 0.95);
            if (Interlocked.Increment(ref detectorCallCount) >= 3)
            {
                candidate = new TextCandidate(candidateBounds, 0.95)
                {
                    SourceCandidateBounds = revisedMembers,
                };
            }

            return TextCandidateDetectionResult.Available("test-detector", new[] { candidate });
        });
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Same OCR text", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google");
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);
        var options = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.Zero,
            enableCandidateDetectorPilot: true,
            minimumCandidateGroupingObservations: 2,
            minimumStableTextObservations: 2);

        using var session = service.CreateLiveSession(profile, options);
        await session.RefreshAsync();
        var firstOcrObservation = await session.RefreshAsync();
        var revisedGrouping = await session.RefreshAsync();
        var revisedFirstOcrObservation = await session.RefreshAsync();

        Assert.Equal(0, translator.CallCount);
        Assert.Contains(
            firstOcrObservation.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkDeferredForStability
                && entry.TextStability?.ObservationCount == 1);
        Assert.Contains(
            revisedGrouping.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGroupingChanged
                && entry.CandidateRevision == 2);
        Assert.Contains(
            revisedFirstOcrObservation.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkDeferredForStability
                && entry.CandidateRevision == 2
                && entry.TextStability?.ObservationCount == 1);

        await session.RefreshAsync();
        await WaitForConditionAsync(() => translator.CallCount == 1);
        Assert.Single(overlay.CurrentSnapshot!.TextItems);
    }

    [Fact]
    public async Task LiveSession_WhenCandidateBoundsJitterWithinBound_KeepsTheCandidateIdentityAndDoesNotRetranslate()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var originalBounds = new BoundingBox(5, 8, 90, 12);
        var jitteredBounds = new BoundingBox(7, 8, 90, 12);
        var detectorRequestCount = 0;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (originalBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (jitteredBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Stable candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detector = new FakeCandidateDetector(_ =>
        {
            var bounds = Interlocked.Increment(ref detectorRequestCount) == 1
                ? originalBounds
                : jitteredBounds;
            return TextCandidateDetectionResult.Available(
                "test-detector",
                new[] { new TextCandidate(bounds, 0.95) });
        });
        var translator = new FakeTranslatorProvider("Google");
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        var firstUpdate = await session.RefreshAsync();
        var secondUpdate = await session.RefreshAsync();

        var originalCandidateId = $"{zone.Id}:candidate:{originalBounds.X}:{originalBounds.Y}:{originalBounds.Width}:{originalBounds.Height}";
        Assert.True(firstUpdate.OverlayChanged);
        Assert.False(secondUpdate.OverlayChanged);
        Assert.Equal(1, translator.CallCount);
        Assert.Contains(
            secondUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGeometryJitterMatched
                && entry.CandidateId == originalCandidateId
                && entry.CandidateBounds == jitteredBounds);
        Assert.DoesNotContain(
            secondUpdate.CandidateLifecycleEvents.Where(entry => entry.RefreshSequence == 2),
            entry => entry.Kind is LiveCandidateLifecycleEventKind.CandidateDiscovered
                or LiveCandidateLifecycleEventKind.CandidateRemoved);
        Assert.Equal(
            new[] { "Translated Stable candidate" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(1, true, false)]
    [InlineData(1, false, true)]
    public async Task LiveSession_InternalLineJitter_PreservesOverlayOnlyForUnchangedSource(
        int offset, bool textChanges, bool memberCountChanges)
    {
        var zone = CreateZone("panel", "Panel", new AbsoluteRectangle(0, 0, 160, 100));
        var bounds = new BoundingBox(20, 10, 80, 50);
        var original = new[] { new BoundingBox(30, 10, 60, 20), new BoundingBox(20, 40, 80, 20) };
        var revised = memberCountChanges ? original.Take(1).ToArray() : new[]
        { new BoundingBox(30 - offset, 10, 60 + offset, 20), original[1] };
        int observation = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test", new[]
        {
            new TextCandidate(bounds, 0.95)
            { SourceCandidateBounds = Interlocked.Increment(ref observation) % 2 == 1 ? original : revised },
        }));
        var frames = Enumerable.Range(0, 6).Select(index => CreateCandidatePilotPixels(zone,
            (byte)(index + 1), (bounds, (byte)(textChanges && index > 0 ? 30 : 10)))).ToArray();
        // The panel changes pixels outside the candidate; the text crop stays unchanged.
        for (int index = 1; index < frames.Length; index += 2)
            Array.Fill(frames[index], (byte)90, frames[index].Length - 160 * 4 * 5, 160 * 4 * 5);
        var engine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId, BlocksFactory = request => new[]
        { new OcrTextBlock(request.Frame.PixelData.Span[0] == 10 ? "Original text" : "Changed text", new BoundingBox(0, 0, 40, 12)) } };
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(new FakeCaptureFrameSource { PixelFrames = frames }, engine, translator,
            new FakeOverlayService(), candidateDetector: detector);
        using var session = service.CreateLiveSession(CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        var first = await session.RefreshAsync();
        Assert.True(first.OverlayChanged);
        var second = await session.RefreshAsync();
        if (offset == 1 && !textChanges && !memberCountChanges)
        {
            for (int index = 1; index < 6; index++)
            {
                var update = index == 1 ? second : await session.RefreshAsync();
                Assert.False(update.OverlayChanged);
                Assert.Contains(update.CandidateLifecycleEvents, e => e.Kind == LiveCandidateLifecycleEventKind.CandidateGeometryJitterMatched);
                Assert.DoesNotContain(update.CandidateLifecycleEvents, e => e.Kind is LiveCandidateLifecycleEventKind.CandidateGroupingChanged
                    or LiveCandidateLifecycleEventKind.CandidateSourceChanged or LiveCandidateLifecycleEventKind.CandidateWorkStarted);
            }
            Assert.Single(engine.Requests);
            Assert.Equal(1, translator.CallCount);
        }
        else
        {
            Assert.Contains(second.CandidateLifecycleEvents, e => e.Kind == LiveCandidateLifecycleEventKind.CandidateGroupingChanged);
            Assert.True(engine.Requests.Count > 1);
            if (textChanges) Assert.Equal(2, translator.CallCount);
        }
    }

    [Fact]
    public async Task LiveSession_WhenCandidateBoundsMoveBeyondJitterBound_TreatsTheCandidateAsNew()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var originalBounds = new BoundingBox(5, 8, 90, 12);
        var movedBounds = new BoundingBox(10, 8, 90, 12);
        var detectorRequestCount = 0;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (originalBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (movedBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Moved candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detector = new FakeCandidateDetector(_ =>
        {
            var bounds = Interlocked.Increment(ref detectorRequestCount) == 1
                ? originalBounds
                : movedBounds;
            return TextCandidateDetectionResult.Available(
                "test-detector",
                new[] { new TextCandidate(bounds, 0.95) });
        });
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            new FakeOverlayService(),
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        var secondUpdate = await session.RefreshAsync();

        Assert.Contains(
            secondUpdate.CandidateLifecycleEvents.Where(entry => entry.RefreshSequence == 2),
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateDiscovered
                && entry.CandidateBounds == movedBounds);
        Assert.Contains(
            secondUpdate.CandidateLifecycleEvents.Where(entry => entry.RefreshSequence == 2),
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateRemoved
                && entry.CandidateBounds == originalBounds);
        Assert.DoesNotContain(
            secondUpdate.CandidateLifecycleEvents.Where(entry => entry.RefreshSequence == 2),
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGeometryJitterMatched);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenOneZoneFails_ContinuesWithSuccessfulZonesAndReportsFailure()
    {
        var firstZone = CreateZone("zone-a", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var secondZone = CreateZone("zone-b", "Choice", new AbsoluteRectangle(200, 120, 80, 30));
        var profile = CreateProfile(firstZone, secondZone);
        var ocrFailure = new OcrEngineException("OCR unavailable for this zone.");
        var ocrEngine = new FakeOcrEngine
        {
            FailureFactory = request => string.Equals(request.ZoneId, secondZone.Id, StringComparison.Ordinal)
                ? ocrFailure
                : null,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Hello", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var overlay = new FakeOverlayService();
        var service = CreateService(
            new FakeCaptureFrameSource(),
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            overlay);

        var result = await service.RunAllZonesAsync(
            profile,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        var zoneResult = Assert.Single(result.ZoneResults);
        Assert.Equal(firstZone.Id, zoneResult.ZoneId);
        var failure = Assert.Single(result.ZoneFailures);
        Assert.Equal(secondZone.Id, failure.ZoneId);
        Assert.Equal(secondZone.Name, failure.ZoneName);
        Assert.Equal(TranslationPipelineStage.Ocr, failure.Stage);
        Assert.Same(ocrFailure, failure.Exception.InnerException);
        Assert.Equal("Translated Hello", Assert.Single(result.OverlaySnapshot.TextItems).Text);
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
        Assert.True(result.HasFailures);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenCredentialsFailAfterOcr_ReportsPartialOcrForEachZone()
    {
        var firstZone = CreateZone("zone-a", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var secondZone = CreateZone("zone-b", "Choice", new AbsoluteRectangle(200, 120, 80, 30));
        var profile = CreateProfile(firstZone, secondZone);
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = request => new[]
            {
                new OcrTextBlock($"Text {request.ZoneId}", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var overlay = new FakeOverlayService();
        var service = CreateService(
            new FakeCaptureFrameSource(),
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            overlay,
            new FakeCredentialStorage());

        var result = await service.RunAllZonesAsync(
            profile,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Empty(result.ZoneResults);
        Assert.Equal(new[] { "zone-a", "zone-b" }, ocrEngine.Requests.Select(request => request.ZoneId));
        Assert.Equal(new[] { "zone-a", "zone-b" }, result.ZoneFailures.Select(failure => failure.ZoneId));
        Assert.All(result.ZoneFailures, failure =>
        {
            Assert.Equal(TranslationPipelineStage.Credentials, failure.Stage);
            Assert.NotNull(failure.CapturedFrame);
            var sourceResult = Assert.IsType<OcrResult>(failure.SourceOcrResult);
            Assert.Single(sourceResult.TextBlocks);
            Assert.Equal(1, failure.RecognizedBlockCount);
        });
        Assert.Equal(2, result.RecognizedBlockCount);
        Assert.Empty(result.OverlaySnapshot.TextItems);
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public async Task RunAsync_WhenCaptureFails_WrapsStageFailure()
    {
        var zone = CreateZone();
        var expected = new CaptureFrameSourceException("Capture source unavailable.");
        var service = CreateService(
            new FakeCaptureFrameSource { Failure = expected },
            new FakeOcrEngine(),
            new FakeTranslatorProvider("Google"),
            new FakeOverlayService());

        var exception = await Assert.ThrowsAsync<TranslationPipelineException>(
            () => service.RunAsync(
                CreateProfile(zone),
                zone,
                runOptions: TranslationPipelineRunOptions.LegacyFullPage));

        Assert.Equal(TranslationPipelineStage.Capture, exception.Stage);
        Assert.Same(expected, exception.InnerException);
        Assert.Equal("Translation pipeline failed during Capture.", exception.Message);
    }

    [Fact]
    public async Task RunAsync_WhenTranslatorReturnsWrongItemCount_FailsTranslationStage()
    {
        var zone = CreateZone();
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("First", new BoundingBox(0, 0, 20, 10)),
                    new OcrTextBlock("Second", new BoundingBox(24, 0, 20, 10)),
                },
            },
            new FakeTranslatorProvider("Google", new[] { "Only one" }),
            new FakeOverlayService());

        var exception = await Assert.ThrowsAsync<TranslationPipelineException>(
            () => service.RunAsync(
                CreateProfile(zone),
                zone,
                runOptions: TranslationPipelineRunOptions.LegacyFullPage));

        Assert.Equal(TranslationPipelineStage.Cache, exception.Stage);
        Assert.Contains("Cache", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_DefaultCandidatePipeline_WhenDetectorIsUnavailable_DoesNotFallbackToFullZoneOcr()
    {
        var zone = CreateZone();
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Legacy full-zone text", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var service = CreateService(
            new FakeCaptureFrameSource(),
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            new FakeOverlayService());

        var result = await service.RunAsync(CreateProfile(zone), zone);

        Assert.Empty(ocrEngine.Requests);
        Assert.Equal(0, result.RecognizedBlockCount);
        Assert.Equal(0, result.TranslatedBlockCount);
        Assert.Empty(result.OverlaySnapshot.TextItems);
    }

    [Fact]
    public async Task LiveSession_StableCandidateWithEmptyOcr_RetriesAfterFiveSecondsWithoutClearingOtherOverlay()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 60));
        var emptyCandidateBounds = new BoundingBox(8, 8, 30, 12);
        var translatedCandidateBounds = new BoundingBox(8, 40, 30, 12);
        var stablePixels = CreateCandidatePilotPixels(
            zone,
            frameMarker: 1,
            (emptyCandidateBounds, (byte)10),
            (translatedCandidateBounds, (byte)20));
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = Enumerable.Repeat(stablePixels, 3).ToArray(),
        };
        var emptyCandidateAttempt = 0;
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request =>
            {
                var marker = request.Frame.PixelData.Span[0];
                if (marker == 10 && Interlocked.Increment(ref emptyCandidateAttempt) == 1)
                {
                    return Array.Empty<OcrTextBlock>();
                }

                var text = marker == 10 ? "Recovered" : "Candidate";
                return new[] { new OcrTextBlock(text, new BoundingBox(0, 0, 20, 10)) };
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[]
            {
                new TextCandidate(emptyCandidateBounds, 0.95),
                new TextCandidate(translatedCandidateBounds, 0.95),
            }));
        var translator = new FakeTranslatorProvider("Google");
        var overlay = new FakeOverlayService();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector,
            timeProvider: timeProvider);
        var options = new TranslationPipelineRunOptions(
            enableCandidateDetectorPilot: true,
            minimumCandidateGroupingObservations: 1)
        {
            MinimumCandidateGroupingDuration = TimeSpan.Zero,
        };

        using var session = service.CreateLiveSession(CreateProfile(zone), options);
        var initial = await session.RefreshAsync();

        Assert.Equal(2, ocrEngine.Requests.Count);
        Assert.Equal(1, translator.CallCount);
        Assert.Equal("Translated Candidate", Assert.Single(initial.BatchResult.OverlaySnapshot.TextItems).Text);

        timeProvider.Advance(TimeSpan.FromMilliseconds(4_999));
        var beforeRetry = await session.RefreshAsync();

        Assert.Equal(2, ocrEngine.Requests.Count);
        Assert.DoesNotContain(
            beforeRetry.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateEmptyOcrRetryScheduled);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        var retried = await session.RefreshAsync();

        Assert.Equal(3, ocrEngine.Requests.Count);
        Assert.Equal(2, emptyCandidateAttempt);
        Assert.Equal(2, translator.CallCount);
        Assert.Equal(2, retried.BatchResult.OverlaySnapshot.TextItems.Count);
        var retryEvent = Assert.Single(
            retried.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateEmptyOcrRetryScheduled);
        Assert.Equal(1, retryEvent.EmptyOcrRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(5), retryEvent.EmptyOcrRetryDelay);
        Assert.False(retryEvent.EmptyOcrGroupingReset);
        Assert.DoesNotContain(overlay.Events, entry => string.Equals(entry, "Show:0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LiveSession_RepeatedEmptyOcr_UsesBoundedBackoffAndReconfirmsGroupingFromThirdRetry()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var stablePixels = CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10));
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = Enumerable.Repeat(stablePixels, 8).ToArray(),
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => Array.Empty<OcrTextBlock>(),
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            new FakeOverlayService(),
            candidateDetector: detector,
            timeProvider: timeProvider);
        var options = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.Zero,
            enableCandidateDetectorPilot: true,
            minimumCandidateGroupingObservations: 2,
            minimumStableTextObservations: 1)
        {
            MinimumCandidateGroupingDuration = TimeSpan.Zero,
        };

        using var session = service.CreateLiveSession(CreateProfile(zone), options);
        await session.RefreshAsync();
        await session.RefreshAsync();
        Assert.Single(ocrEngine.Requests);

        timeProvider.Advance(TimeSpan.FromSeconds(5));
        var firstRetry = await session.RefreshAsync();
        Assert.Equal(2, ocrEngine.Requests.Count);
        AssertEmptyOcrRetry(firstRetry, expectedCount: 1, expectedDelaySeconds: 5, expectedGroupingReset: false);

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        var secondRetry = await session.RefreshAsync();
        Assert.Equal(3, ocrEngine.Requests.Count);
        AssertEmptyOcrRetry(secondRetry, expectedCount: 2, expectedDelaySeconds: 10, expectedGroupingReset: false);

        timeProvider.Advance(TimeSpan.FromSeconds(20));
        var thirdRetry = await session.RefreshAsync();
        Assert.Equal(3, ocrEngine.Requests.Count);
        AssertEmptyOcrRetry(thirdRetry, expectedCount: 3, expectedDelaySeconds: 20, expectedGroupingReset: true);

        await session.RefreshAsync();
        Assert.Equal(4, ocrEngine.Requests.Count);

        timeProvider.Advance(TimeSpan.FromSeconds(20));
        var cappedRetry = await session.RefreshAsync();
        Assert.Equal(4, ocrEngine.Requests.Count);
        AssertEmptyOcrRetry(cappedRetry, expectedCount: 4, expectedDelaySeconds: 20, expectedGroupingReset: true);

        static void AssertEmptyOcrRetry(
            LiveTranslationPipelineUpdate update,
            int expectedCount,
            int expectedDelaySeconds,
            bool expectedGroupingReset)
        {
            var retryEvent = Assert.Single(
                update.CandidateLifecycleEvents,
                entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateEmptyOcrRetryScheduled);
            Assert.Equal(expectedCount, retryEvent.EmptyOcrRetryCount);
            Assert.Equal(TimeSpan.FromSeconds(expectedDelaySeconds), retryEvent.EmptyOcrRetryDelay);
            Assert.Equal(expectedGroupingReset, retryEvent.EmptyOcrGroupingReset);
        }
    }

    [Fact]
    public async Task LiveSession_NonEmptyDeferredOcr_ResetsEmptyRetryBackoff()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var stablePixels = CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10));
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = Enumerable.Repeat(stablePixels, 4).ToArray(),
        };
        var recognitionAttempt = 0;
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => Interlocked.Increment(ref recognitionAttempt) switch
            {
                1 or 3 => Array.Empty<OcrTextBlock>(),
                _ => new[] { new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)) },
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            new FakeOverlayService(),
            candidateDetector: detector,
            timeProvider: timeProvider);
        var options = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.FromSeconds(1),
            enableCandidateDetectorPilot: true,
            minimumCandidateGroupingObservations: 1,
            minimumStableTextObservations: 2)
        {
            MinimumCandidateGroupingDuration = TimeSpan.Zero,
        };

        using var session = service.CreateLiveSession(CreateProfile(zone), options);
        await session.RefreshAsync();
        Assert.Single(ocrEngine.Requests);

        timeProvider.Advance(TimeSpan.FromSeconds(5));
        var nonEmptyDeferred = await session.RefreshAsync();
        Assert.Equal(2, ocrEngine.Requests.Count);
        Assert.Contains(
            nonEmptyDeferred.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkDeferredForStability
                && entry.RecognizedBlockCount == 1);

        await session.RefreshAsync();
        Assert.Equal(3, ocrEngine.Requests.Count);

        timeProvider.Advance(TimeSpan.FromSeconds(5));
        var retriedAfterReset = await session.RefreshAsync();
        var retryEvent = Assert.Single(
            retriedAfterReset.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateEmptyOcrRetryScheduled);
        Assert.Equal(1, retryEvent.EmptyOcrRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(5), retryEvent.EmptyOcrRetryDelay);
    }

    [Fact]
    public async Task RunAsync_DefaultCandidatePipeline_UsesTesseractForTheBoundedCandidateCrop()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var candidateBounds = new BoundingBox(18, 7, 30, 18);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, 1, (candidateBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Candidate text", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            new FakeOverlayService(),
            candidateDetector: detector);

        var result = await service.RunAsync(profile, zone);

        var request = Assert.Single(ocrEngine.Requests);
        Assert.Equal(OcrSettings.TesseractEngineId, request.EngineId);
        Assert.Equal(ContentLayoutMode.DialogComic, request.ContentLayoutMode);
        Assert.Equal(
            new CaptureRegion(28, 27, candidateBounds.Width, candidateBounds.Height),
            request.Frame.Region);
        Assert.Equal("Translated Candidate text", Assert.Single(result.OverlaySnapshot.TextItems).Text);
    }

    [Theory]
    [InlineData(WatermarkFilterMode.Off, false)]
    [InlineData(WatermarkFilterMode.Strong, true)]
    public async Task Watermark_LiveAndOneShotPreserveRawOcrAndExcludeOnlyLogo(WatermarkFilterMode mode, bool excluded)
    {
        var zone=CreateZone("watermark", "Watermark", new AbsoluteRectangle(0,0,320,320));
        var logo=WatermarkFilterTests.Logo();
        var pixels=WatermarkFilterTests.Page(0).PixelData.ToArray();
        for(var y=0;y<100;y++) logo.Frame.PixelData.Span.Slice(y*480,480).CopyTo(pixels.AsSpan(y*1280,480));
        var bubble=new BoundingBox(170,190,120,90);
        var detector=new FakeCandidateDetector(_=>TextCandidateDetectionResult.Available("test",new[] {
            logo.Candidate,new TextCandidate(bubble,.95) }));
        var engine=new FakeOcrEngine { EngineId=OcrSettings.TesseractEngineId, BlocksFactory=r=>new[] {
            new OcrTextBlock(r.Frame.Height==100?"synthetic emblem":"Useful dialogue 42",new BoundingBox(0,0,60,15)) } };
        var translator=new FakeTranslatorProvider("Google");
        var clock=new ManualTimeProvider(FrameTime);
        var service=CreateService(new FakeCaptureFrameSource { PixelFrames=Enumerable.Repeat(pixels,30).ToArray() },
            engine,translator,new FakeOverlayService(),candidateDetector:detector,timeProvider:clock);
        var profile=CreateProfile(zone) with { OcrSettings=new() { OrientationMode=OcrOrientationMode.Horizontal,WatermarkFilter=mode } };
        using(var session=service.CreateLiveSession(profile,new TranslationPipelineRunOptions()))
        {
            var first=await session.RefreshAsync();
            Assert.Equal(2,first.BatchResult.ZoneResults.Count);
            Assert.Equal(excluded?1:2,first.BatchResult.ZoneResults.Sum(r=>r.TranslatedBlockCount));
            Assert.All(first.BatchResult.ZoneResults,r=>Assert.Single(r.SourceOcrResult.TextBlocks));
            if(excluded) Assert.Contains(first.CandidateLifecycleEvents,e=>e.WatermarkDecision?.Excluded==true);
            clock.Advance(TimeSpan.FromSeconds(60));
            for(var i=0;i<5;i++)
            {
                var next=await session.RefreshAsync();
                Assert.DoesNotContain(next.CandidateLifecycleEvents,e=>e.Kind==LiveCandidateLifecycleEventKind.CandidateEmptyOcrRetryScheduled);
            }
            Assert.Equal(2,engine.Requests.Count);
        }
        var batch=await service.RunAllZonesAsync(profile);
        Assert.Equal(excluded?1:2,batch.ZoneResults.Sum(r=>r.TranslatedBlockCount));
        var single=await service.RunAsync(profile,zone);
        Assert.Equal(2,single.RecognizedBlockCount);
        Assert.Equal(excluded?1:2,single.TranslationInputBlockCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Watermark_CautiousReevaluatesUnchangedLogoOnThirdPage(bool optimize)
    {
        var zone=CreateZone("repeat", "Repeat", new AbsoluteRectangle(0,0,320,320));
        var logo=WatermarkFilterTests.Logo();
        var frames=new List<byte[]>();
        foreach(var page in new[]{0,0,0,1,1,1,0,0,2,2,2})
        {
            var pixels=WatermarkFilterTests.Page(page).PixelData.ToArray();
            for(var y=0;y<100;y++) logo.Frame.PixelData.Span.Slice(y*480,480).CopyTo(pixels.AsSpan(y*1280,480));
            frames.Add(pixels);
        }
        var detector=new FakeCandidateDetector(_=>TextCandidateDetectionResult.Available("test",new[]{logo.Candidate}));
        var engine=new FakeOcrEngine { EngineId=OcrSettings.TesseractEngineId,
            BlocksFactory=_=>new[]{new OcrTextBlock("Synthetic emblem",new BoundingBox(0,0,60,15))} };
        var service=CreateService(new FakeCaptureFrameSource { PixelFrames=frames },engine,
            new FakeTranslatorProvider("Google"),new FakeOverlayService(),candidateDetector:detector,
            optimizationOptions:new TranslationPipelineOptimizationOptions(isEnabled:optimize));
        var profile=CreateProfile(zone) with { OcrSettings=new() { WatermarkFilter=WatermarkFilterMode.Cautious,OrientationMode=OcrOrientationMode.Horizontal } };
        using var session=service.CreateLiveSession(profile,new TranslationPipelineRunOptions());
        for(var i=0;i<frames.Count;i++)
        {
            var update=await session.RefreshAsync();
            var result=Assert.Single(update.BatchResult.ZoneResults);
            Assert.Equal(i>=9,result.SourceOcrResult.WatermarkDecision!.Excluded);
            Assert.Equal(i>=9?0:1,result.TranslatedBlockCount);
        }
        Assert.Equal(2,engine.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Watermark_ContextChangeReevaluatesIdenticalOcrCrop(bool optimize)
    {
        var zone=CreateZone("context", "Context", new AbsoluteRectangle(0,0,320,320));
        var badge=WatermarkContextAnalyzerTests.Page(40,50,100,100,0);
        var plain=WatermarkContextAnalyzerTests.Page(40,50,100,100,4).PixelData.ToArray();
        var bounds=new BoundingBox(48,124,84,18);
        for(var y=bounds.Y;y<bounds.Bottom;y++)
            badge.PixelData.Span.Slice(y*1280+bounds.X*4,bounds.Width*4).CopyTo(plain.AsSpan(y*1280+bounds.X*4));
        var frames=new[]{badge.PixelData.ToArray(),badge.PixelData.ToArray(),plain,plain,badge.PixelData.ToArray()};
        var detector=new FakeCandidateDetector(_=>TextCandidateDetectionResult.Available("test",new[]{new TextCandidate(bounds,.95)}));
        var engine=new FakeOcrEngine { EngineId=OcrSettings.TesseractEngineId,
            BlocksFactory=_=>new[]{new OcrTextBlock("Same OCR text",new BoundingBox(0,0,60,15))} };
        var service=CreateService(new FakeCaptureFrameSource { PixelFrames=frames },engine,
            new FakeTranslatorProvider("Google"),new FakeOverlayService(),candidateDetector:detector,
            optimizationOptions:new TranslationPipelineOptimizationOptions(isEnabled:optimize));
        var profile=CreateProfile(zone) with { OcrSettings=new() { WatermarkFilter=WatermarkFilterMode.Strong,OrientationMode=OcrOrientationMode.Horizontal } };
        using var session=service.CreateLiveSession(profile,new TranslationPipelineRunOptions());
        for(var i=0;i<frames.Length;i++)
        {
            var update=await session.RefreshAsync();
            var result=Assert.Single(update.BatchResult.ZoneResults);
            Assert.Equal(i<2 || i==4,result.SourceOcrResult.WatermarkDecision!.Excluded);
            Assert.Equal(i<2 || i==4?0:1,result.TranslatedBlockCount);
            Assert.Single(result.SourceOcrResult.TextBlocks);
            if(i==2 || i==4) Assert.Contains(update.CandidateLifecycleEvents,e=>e.Kind==LiveCandidateLifecycleEventKind.CandidateWatermarkEvidenceChanged);
        }
        Assert.Equal(3,engine.Requests.Count);
    }

    private static TranslationPipelineService CreateService(
        FakeCaptureFrameSource frameSource,
        IOcrEngine ocrEngine,
        FakeTranslatorProvider translator,
        FakeOverlayService overlay,
        FakeCredentialStorage? credentialStorage = null,
        TranslationPipelineOptimizationOptions? optimizationOptions = null,
        ITextCandidateDetector? candidateDetector = null,
        TextCandidateRegionOcrOptions? candidateRegionOcrOptions = null,
        TimeProvider? timeProvider = null)
    {
        frameSource.Clock = timeProvider ?? TimeProvider.System;
        var ocrService = new OcrService(ocrEngine);
        return new TranslationPipelineService(
            new CaptureService(frameSource),
            ocrService,
            new TranslatorManager(new ITranslatorProvider[] { translator }),
            new TranslatorCredentialService(credentialStorage ?? FakeCredentialStorage.WithGoogleCredentials()),
            new TranslationCacheService(new FakeTranslationCacheRepository(), new TranslationCacheOptions()),
            new OverlayPositioningService(),
            overlay,
            optimizationOptions ?? TranslationPipelineOptimizationOptions.Disabled,
            candidateDetector is null
                ? null
                : new TextCandidateRegionOcrService(candidateDetector, ocrService, candidateRegionOcrOptions),
            timeProvider);
    }

    private static TranslationPipelineService CreateCandidateLiveService(
        OcrZone zone,
        BoundingBox candidateBounds,
        FakeTranslatorProvider translator,
        FakeOverlayService overlay)
    {
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));

        return CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);
    }

    private static GameProfile CreateProfile(params OcrZone[] zones)
    {
        return new GameProfile
        {
            Id = "profile-a",
            Name = "Pipeline profile",
            OcrZones = zones,
            OcrSettings = new OcrSettings
            {
                OrientationMode = OcrOrientationMode.Vertical,
            },
            OverlaySettings = new OverlaySettings
            {
                MaskMode = OverlayMaskMode.Darken,
                MaskColor = "#101010",
                Opacity = 0.75,
                Padding = 6,
            },
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "en",
                TargetLanguage = "ru",
            },
        };
    }

    private static OcrZone CreateZone()
    {
        return CreateZone("zone-a", "Subtitles", new AbsoluteRectangle(10, 20, 100, 40));
    }

    private static OcrZone CreateZone(string id, string name, AbsoluteRectangle absoluteBounds)
    {
        return new OcrZone
        {
            Id = id,
            Name = name,
            AbsoluteBounds = absoluteBounds,
            RelativeBounds = new RelativeRectangle(0.1, 0.2, 0.3, 0.1),
        };
    }

    private static byte[] CreatePixels(OcrZone zone, byte value)
    {
        var stride = checked(zone.AbsoluteBounds.Width * 4);
        return Enumerable.Repeat(value, checked(stride * zone.AbsoluteBounds.Height)).ToArray();
    }

    private static byte[] CreateCandidatePilotPixels(
        OcrZone zone,
        byte frameMarker,
        params (BoundingBox Bounds, byte Value)[] candidateMarkers)
    {
        var width = zone.AbsoluteBounds.Width;
        var height = zone.AbsoluteBounds.Height;
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        pixels[0] = frameMarker;

        foreach (var (bounds, value) in candidateMarkers)
        {
            for (var row = bounds.Y; row < bounds.Bottom; row++)
            {
                var offset = checked(row * stride + bounds.X * 4);
                pixels.AsSpan(offset, checked(bounds.Width * 4)).Fill(value);
            }
        }

        return pixels;
    }

    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= timeoutAt)
            {
                throw new TimeoutException("Condition was not met before the test timeout.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }

    private sealed class FakeCaptureFrameSource : IRecentCaptureFrameSource
    {
        public Task? CaptureGate { get; set; }
        public TimeProvider Clock { get; set; } = TimeProvider.System;
        public TimeSpan? SourceTimestampOverride { get; set; }
        private readonly Guid sourceId = Guid.NewGuid();
        public Task<CapturedFrame> CaptureRecentAsync(CaptureRegion region, TimeSpan maximumSourceAge,
            CancellationToken cancellationToken = default) => CaptureAsync(region, cancellationToken);
        private int captureCount;
        private readonly object syncRoot = new();

        public List<CaptureRegion> CapturedRegions { get; } = new();

        public Exception? Failure { get; init; }

        public Func<CaptureRegion, int, Exception?>? FailureFactory { get; init; }

        public IReadOnlyList<byte[]> PixelFrames { get; init; } = Array.Empty<byte[]>();

        public IReadOnlyList<DateTimeOffset> CapturedAtFrames { get; init; } = Array.Empty<DateTimeOffset>();

        public Action? OnCapture { get; init; }

        public Task<CapturedFrame> CaptureAsync(CaptureRegion region, CancellationToken cancellationToken = default)
        {
            var frame = CaptureCore(region, cancellationToken);
            return CaptureGate is { } gate ? DeliverAfterGate(frame, gate) : frame;
        }

        private static async Task<CapturedFrame> DeliverAfterGate(Task<CapturedFrame> frame, Task gate)
        {
            await gate; // Deliberately ignores cancellation to exercise late source completion.
            return await frame;
        }

        private Task<CapturedFrame> CaptureCore(CaptureRegion region, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int captureIndex;
            lock (syncRoot)
            {
                captureIndex = captureCount++;
                CapturedRegions.Add(region);
            }

            var failure = Failure ?? FailureFactory?.Invoke(region, captureIndex);
            if (failure is not null)
            {
                return Task.FromException<CapturedFrame>(failure);
            }

            OnCapture?.Invoke();
            var stride = checked(region.Width * 4);
            var byteCount = checked(stride * region.Height);
            var pixels = captureIndex < PixelFrames.Count
                ? PixelFrames[captureIndex].ToArray()
                : Enumerable.Repeat((byte)42, byteCount).ToArray();
            if (pixels.Length != byteCount)
            {
                throw new InvalidOperationException("Fake capture frame pixel data length does not match the requested region.");
            }

            var capturedAt = captureIndex < CapturedAtFrames.Count
                ? CapturedAtFrames[captureIndex]
                : FrameTime.AddMilliseconds(captureIndex * 16);

            return Task.FromResult(
                new CapturedFrame(
                    region,
                    region.Width,
                    region.Height,
                    stride,
                    "Bgra32",
                    pixels,
                    capturedAt,
                    new CaptureFrameOrigin(sourceId, SourceTimestampOverride ?? TimeSpan.FromSeconds(Clock.GetTimestamp() / (double)Clock.TimestampFrequency), Clock.GetUtcNow()),
                    isCachedReuse: SourceTimestampOverride is not null));
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private long timestamp;
        public override long GetTimestamp() => timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            utcNow += duration;
            timestamp += duration.Ticks;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CandidateOcr_PreservesDetectorLinesThroughBothPipelineRoutes(bool live)
    {
        var zone = CreateZone("line-zone", "Lines", new AbsoluteRectangle(0, 0, 200, 100));
        var profile = CreateProfile(zone);
        var engine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[] { new OcrTextBlock("Synthetic line", request.DetectorLineBounds[0]) },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available("test", new[]
        {
            new TextCandidate(new BoundingBox(20, 10, 100, 20), 0.95),
            new TextCandidate(new BoundingBox(20, 35, 100, 20), 0.95),
        }));
        var service = CreateService(new FakeCaptureFrameSource(), engine,
            new FakeTranslatorProvider("Google"), new FakeOverlayService(), candidateDetector: detector);
        var options = new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true,
            minimumCandidateGroupingObservations: 1) { MinimumCandidateGroupingDuration = TimeSpan.Zero };
        if (live)
        {
            using var session = service.CreateLiveSession(profile, options);
            await session.RefreshAsync();
        }
        else
        {
            await service.RunAllZonesAsync(profile, runOptions: options);
        }
        var request = Assert.Single(engine.Requests);
        Assert.Equal(new[] { new BoundingBox(0, 0, 100, 20), new BoundingBox(0, 25, 100, 20) }, request.DetectorLineBounds);
    }

    private sealed class FakeOcrEngine : IOcrEngine
    {
        private int recognitionCount;
        private readonly object syncRoot = new();

        public string EngineId { get; init; } = OcrSettings.WindowsEngineId;

        public List<OcrRequest> Requests { get; } = new();

        public Func<OcrRequest, IReadOnlyList<OcrTextBlock>>? BlocksFactory { get; init; }

        public Func<OcrRequest, Exception?>? FailureFactory { get; init; }

        public Action? OnRecognize { get; init; }

        public IReadOnlyList<DateTimeOffset> RecognizedAtFrames { get; init; } = Array.Empty<DateTimeOffset>();

        public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int recognitionIndex;
            lock (syncRoot)
            {
                recognitionIndex = recognitionCount++;
                Requests.Add(request);
            }

            OnRecognize?.Invoke();

            var failure = FailureFactory?.Invoke(request);
            if (failure is not null)
            {
                return Task.FromException<OcrResult>(failure);
            }

            var recognizedAt = recognitionIndex < RecognizedAtFrames.Count
                ? RecognizedAtFrames[recognitionIndex]
                : OcrTime;

            return Task.FromResult(
                new OcrResult(
                    request,
                    BlocksFactory?.Invoke(request) ?? Array.Empty<OcrTextBlock>(),
                    recognizedAt));
        }
    }

    private sealed class FakeCandidateDetector : ITextCandidateDetector
    {
        private readonly Func<TextCandidateDetectionRequest, TextCandidateDetectionResult> detect;

        public FakeCandidateDetector(Func<TextCandidateDetectionRequest, TextCandidateDetectionResult> detect)
        {
            this.detect = detect;
        }

        public Task<TextCandidateDetectionResult> DetectAsync(
            TextCandidateDetectionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(detect(request));
        }
    }

    private sealed class FakeTranslatorProvider : ITranslatorProvider
    {
        private readonly IReadOnlyList<string>? translatedTexts;
        private readonly Func<TranslateRequest, CancellationToken, Task<TranslateResponse>>? translateAsync;
        private int callCount;

        public FakeTranslatorProvider(
            string providerId,
            IReadOnlyList<string>? translatedTexts = null,
            Func<TranslateRequest, CancellationToken, Task<TranslateResponse>>? translateAsync = null)
        {
            ProviderId = providerId;
            this.translatedTexts = translatedTexts;
            this.translateAsync = translateAsync;
        }

        public string ProviderId { get; }

        public int CallCount => callCount;

        public TranslateRequest? Request { get; private set; }

        public Task<TranslateResponse> TranslateAsync(
            TranslateRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref callCount);
            Request = request;
            if (translateAsync is not null)
            {
                return translateAsync(request, cancellationToken);
            }

            return Task.FromResult(
                new TranslateResponse(
                    translatedTexts ?? request.Texts.Select(text => $"Translated {text}"),
                    TranslatedAt,
                    ProviderId));
        }
    }

    private sealed class FakeCredentialStorage : ICredentialStorage
    {
        private readonly Dictionary<string, TranslatorCredentialRecord> records = new(StringComparer.OrdinalIgnoreCase);

        public static FakeCredentialStorage WithGoogleCredentials()
        {
            var storage = new FakeCredentialStorage();
            storage.records["Google"] = new TranslatorCredentialRecord(
                "Google",
                "SECRET_ACCESS_TOKEN",
                "project-a",
                "global",
                new Uri("https://translation.test"));

            return storage;
        }

        public Task SaveAsync(
            TranslatorCredentialRecord credential,
            CancellationToken cancellationToken = default)
        {
            records[credential.Provider] = credential;
            return Task.CompletedTask;
        }

        public Task<TranslatorCredentialRecord?> ReadAsync(
            string provider,
            CancellationToken cancellationToken = default)
        {
            records.TryGetValue(provider, out var credential);
            return Task.FromResult(credential);
        }

        public Task DeleteAsync(string provider, CancellationToken cancellationToken = default)
        {
            records.Remove(provider);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOverlayService : IOverlayService
    {
        public List<OverlaySnapshot> Snapshots { get; } = new();
        public bool IsVisible { get; private set; }

        public bool IsExcludedFromCapture { get; set; }

        public OverlaySnapshot? CurrentSnapshot { get; private set; }

        public List<string> Events { get; } = new();

        public void Show(OverlaySnapshot snapshot)
        {
            Snapshots.Add(snapshot);
            CurrentSnapshot = snapshot;
            IsVisible = true;
            Events.Add($"Show:{snapshot.TextItems.Count}");
        }

        public void Hide()
        {
            IsVisible = false;
            Events.Add("Hide");
        }
    }

    private sealed class FakeTranslationCacheRepository : ITranslationCacheRepository
    {
        private readonly Dictionary<TranslationCacheKey, TranslationCacheEntry> entries = new();
        private readonly object syncRoot = new();

        public Task<TranslationCacheEntry?> GetAsync(
            TranslationCacheKey key,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            lock (syncRoot)
            {
                entries.TryGetValue(key, out var entry);
                if (entry?.IsExpired(now) == true)
                {
                    return Task.FromResult<TranslationCacheEntry?>(null);
                }

                return Task.FromResult(entry);
            }
        }

        public Task SaveAsync(
            TranslationCacheEntry entry,
            CancellationToken cancellationToken = default)
        {
            lock (syncRoot)
            {
                entries[entry.Key] = entry;
            }

            return Task.CompletedTask;
        }

        public Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = entries.Count;
            entries.Clear();
            return Task.FromResult(count);
        }

        public Task<int> DeleteExpiredAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            lock (syncRoot)
            {
                var expiredKeys = entries
                    .Where(pair => pair.Value.IsExpired(now))
                    .Select(pair => pair.Key)
                    .ToArray();
                foreach (var key in expiredKeys)
                {
                    entries.Remove(key);
                }

                return Task.FromResult(expiredKeys.Length);
            }
        }
    }
}
