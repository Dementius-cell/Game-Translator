using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Pipeline;

public sealed partial class TranslationPipelineService
{
    public sealed partial class LiveTranslationSession
    {
        private readonly MenuOcrScheduler<MenuOcrInput> menuScheduler = new();
        private readonly List<MenuOcrWork> menuOcrWork = new();
        private bool refreshPublishedMenuProgress;
        private bool refreshPendingOverlayChange;
        private bool captureOverlayHidden;
        private bool captureOverlayChanged;
        private bool deferMenuStartsForCapture;

        // The refresh owns stateAuthority throughout. Service completions cooperatively
        // while the external stage waits; do not introduce a second state writer.
        private async Task<T> AwaitWithMenuProgressAsync<T>(Task<T> operation, CancellationToken cancellationToken)
        {
            if (!runOptions.EnableCandidateDetectorPilot
                || !profile.OcrZones.Any(zone => zone.ContentLayoutMode == ContentLayoutMode.StaticMenu))
                return await operation;

            while (!operation.IsCompleted && !cancellationToken.IsCancellationRequested)
            {
                ThrowIfDisposed();
                ResetWorkCompletionSignalForCollection();
                var changed = await CollectCompletedWorkAsync();
                changed |= refreshPendingOverlayChange;
                refreshPendingOverlayChange = false;
                if (captureOverlayHidden)
                {
                    captureOverlayChanged |= changed;
                }
                else if (changed || hasDeferredOverlayPublication)
                {
                    refreshPublishedMenuProgress |= PublishOverlaySnapshotIfReady(CreateBatchResult(), changed);
                }
                Task completion;
                lock (workCompletionSignalSyncRoot) completion = workCompletionSignal.Task;
                // The timer also services start intervals and expiry when no work completes.
                using var timerCancellation = new CancellationTokenSource();
                var tick = Task.Delay(TimeSpan.FromMilliseconds(100), service.timeProvider, timerCancellation.Token);
                await Task.WhenAny(operation, completion, tick);
                await timerCancellation.CancelAsync();
            }
            var result = await operation;
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            return result;
        }

        private TimeSpan MenuNow => TimeSpan.FromSeconds(
            service.timeProvider.GetTimestamp() / (double)service.timeProvider.TimestampFrequency);

        private void RefreshPendingMenuPixels(LiveCapturedZone captured)
        {
            if (!runOptions.EnableCandidateDetectorPilot
                || captured.Zone.ContentLayoutMode != ContentLayoutMode.StaticMenu
                || profile.OcrSettings.WatermarkFilter != WatermarkFilterMode.Off
                || captured.Frame.Origin is not { } origin) return;
            var state = zoneStates[captured.Zone.Id];
            var regions = state.CandidateStates.Values.Select(c => c.Region).ToArray();
            var request = CreateOcrRequest(profile, captured.Zone, captured.Frame);
            var neighbors = regions.Select(r => r.Candidate).ToArray();
            foreach (var candidate in state.CandidateStates.Values)
            {
                if (candidate.Menu is null || candidate.Region.Frame.Origin is not { } geometryOrigin
                    || geometryOrigin.SourceId != origin.SourceId
                    || origin.SourceTimestamp <= geometryOrigin.SourceTimestamp
                    || MenuNow - geometryOrigin.FreshnessTimestamp > TimeSpan.FromSeconds(2)) continue;
                var bounds = candidate.Region.Candidate.Bounds;
                if (bounds.Right > captured.Frame.Width || bounds.Bottom > captured.Frame.Height) continue;
                var region = TextCandidateRegionOcrService.CreateRegion(request, candidate.Region.Candidate, neighbors);
                var frame = region.Frame;
                var core = CreateMenuCore(region, captured.Frame, neighbors);
                menuScheduler.Offer(candidate.Id, new MenuOcrInput(candidate, candidate.Zone, region,
                    captured.CaptureElapsed, CreateCandidateOverlayPlacementConstraints(state.Zone, regions, candidate.Region), core),
                    origin, !candidate.SourceIdentity.Matches(frame), MenuNow);
            }
            // OCR may overlap detection, but its result cannot renew text authority until
            // the detector validates this source frame (or a newer compatible one).
            PumpMenuOcr();
        }

        private bool ReconcileMenuRegions(LiveZoneState state, LiveCapturedZone captured,
            IReadOnlyList<TextCandidateRegion> regions, ICollection<string> cancelledIds)
        {
            var changed = false;
            // Match all pairs before mutating tracks. Both directions must be unambiguous.
            var previous = state.CandidateStates.Values.ToArray();
            var inkEvidence = new Dictionary<TextCandidateRegion, MenuFixedInkEvidence?>();
            var coreEvidence = new Dictionary<TextCandidateRegion, MenuGlyphCoreEvidence?>();
            var neighbors = regions.Select(r => r.Candidate).ToArray();
            MenuGlyphCoreEvidence? GetCore(TextCandidateRegion region)
            {
                if (profile.OcrSettings.OrientationMode != OcrOrientationMode.Horizontal
                    || profile.OcrSettings.WatermarkFilter != WatermarkFilterMode.Off) return null;
                if (!coreEvidence.TryGetValue(region, out var core))
                    coreEvidence[region] = core = CreateMenuCore(region, captured.Frame, neighbors);
                return core;
            }
            MenuFixedInkEvidence? GetInk(TextCandidateRegion region)
            {
                if (profile.OcrSettings.OrientationMode != OcrOrientationMode.Horizontal
                    || profile.OcrSettings.WatermarkFilter != WatermarkFilterMode.Off) return null;
                if (!inkEvidence.TryGetValue(region, out var ink))
                    inkEvidence[region] = ink = MenuFixedInkEvidence.TryCreate(region);
                return ink;
            }
            var matches = MenuGeometryMatcher.MatchUnique(previous, regions,
                (c, r) => MenuGeometryMatches(c, r, GetInk, GetCore)
                    && c.Region.Frame.Origin?.SourceId == r.Frame.Origin?.SourceId
                    && WatermarkFilter.CreateEvidenceKey(profile.OcrSettings.WatermarkFilter, c.Region.WatermarkEvidence)
                       == WatermarkFilter.CreateEvidenceKey(profile.OcrSettings.WatermarkFilter, r.WatermarkEvidence));
            var assigned = matches.ToDictionary(pair => regions[pair.Key], pair => previous[pair.Value]);
            foreach (var old in state.CandidateStates.Values.Where(c => !assigned.Values.Contains(c)).ToArray())
                changed |= CancelAndRemoveCandidate(state, old, cancelledIds, LiveCandidateCancellationReason.CandidateDisappeared);

            foreach (var region in regions)
            {
                var isNew = !assigned.TryGetValue(region, out var candidate);
                if (isNew)
                {
                    var id = CreateCandidateId(state.Zone, region.Candidate.Bounds);
                    candidate = new LiveCandidateState(id, state.Zone.Id, CreateCandidateZone(state.Zone, region.Candidate.Bounds),
                        region, FrameFingerprint.FromFrame(region.Frame), CreateCandidateGeometrySignature(region.Candidate), region.Frame.Origin?.CapturedAt ?? captured.Frame.CapturedAt)
                    {
                        Menu = new MenuCandidateState(region, new MenuTextAuthority(
                            runOptions.RequireStableTextBeforeTranslation ? runOptions.MinimumStableTextObservations : 2,
                            runOptions.RequireStableTextBeforeTranslation ? runOptions.StableTextInterval : TimeSpan.FromMilliseconds(200)), GetInk(region), GetCore(region)),
                    };
                    state.CandidateStates.Add(id, candidate);
                    candidate.Menu.Generation = menuScheduler.Register(id);
                    RecordCandidateLifecycleEvent(LiveCandidateLifecycleEventKind.CandidateDiscovered, candidate,
                        frameCapturedAt: captured.Frame.CapturedAt);
                }
                var current = candidate!;
                var previousDetectedOrigin = current.Region.Frame.Origin;
                var pixelsChanged = !current.SourceIdentity.Matches(region.Frame);
                current.Region = region;
                current.Menu!.CurrentCore = GetCore(region);
                current.Zone = CreateCandidateZone(state.Zone, region.Candidate.Bounds) with { Id = current.Zone.Id };
                current.GeometrySignature = CreateCandidateGeometrySignature(region.Candidate);
                current.SourceIdentity = FrameFingerprint.FromFrame(region.Frame);
                menuScheduler.Offer(current.Id, new MenuOcrInput(current, current.Zone, region,
                    captured.CaptureElapsed, CreateCandidateOverlayPlacementConstraints(state.Zone, regions, region), current.Menu.CurrentCore),
                    region.Frame.Origin, pixelsChanged, MenuNow);
                if (!isNew && region.Frame.Origin is { } detectedOrigin
                    && (previousDetectedOrigin is null || detectedOrigin.SourceTimestamp > previousDetectedOrigin.SourceTimestamp))
                    ObserveMatchingCandidateGrouping(current, region.Frame.Origin!.CapturedAt);
            }
            PumpMenuOcr();
            return changed;
        }

        private MenuGlyphCoreEvidence? CreateMenuCore(TextCandidateRegion region, CapturedFrame source,
            IReadOnlyList<TextCandidate> neighbors) =>
            profile.OcrSettings.OrientationMode == OcrOrientationMode.Horizontal
                && profile.OcrSettings.WatermarkFilter == WatermarkFilterMode.Off
                ? MenuGlyphCoreEvidence.TryCreateWithSource(region, source, neighbors) : null;

        private static bool MenuGeometryMatches(LiveCandidateState candidate, TextCandidateRegion current,
            Func<TextCandidateRegion, MenuFixedInkEvidence?>? getInk = null,
            Func<TextCandidateRegion, MenuGlyphCoreEvidence?>? getCore = null)
        {
            if (candidate.Menu is not { } menu) return false;
            return MenuGeometryMatcher.Matches(menu.AnchorBounds, menu.AnchorMembers,
                current.Candidate.Bounds, current.Candidate.SourceCandidateBounds)
                || (menu.AnchorInk is not null && MenuFixedInkEvidence.Matches(menu.AnchorBounds, menu.AnchorInk,
                    current.Candidate.Bounds, getInk is null ? MenuFixedInkEvidence.TryCreate(current) : getInk(current)))
                || (menu.AnchorCore is not null && MenuGlyphCoreEvidence.Matches(menu.AnchorBounds, menu.AnchorCore,
                    current.Candidate.Bounds, getCore is null ? MenuGlyphCoreEvidence.TryCreate(current) : getCore(current)));
        }

        private void PumpMenuOcr()
        {
            if (deferMenuStartsForCapture) return;
            while (menuScheduler.TryStartNext(MenuNow, input => IsCandidateGroupingConfirmed(input.Candidate)) is { } lease)
            {
                var input = lease.Work;
                var candidate = input.Candidate;
                candidate.Menu!.Periodic = menuScheduler.IsPeriodic(candidate.Id);
                RecordMenuDecision(candidate, "ocr-started", input.Region.Frame);
                var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationSource.Token, lease.CancellationToken);
                var task = service.RunCapturedZoneAsync(CreateCandidateProfile(input.Zone), input.Zone, input.Region.Frame,
                    input.CaptureElapsed, null, CreateCandidateRunOptions(), token.Token, input.Constraints,
                    candidateRecognitionContext: new CandidateRecognitionContext(input.Region.Candidate,
                        zoneStates[candidate.SourceZoneId].Zone.AbsoluteBounds.Height, WatermarkEvidence: input.Region.WatermarkEvidence,
                        RecognitionContextFrame: input.Region.RecognitionContextFrame),
                    recognitionOnly: true);
                menuOcrWork.Add(new MenuOcrWork(lease, token, task));
                candidate.WorkAttempt++;
                RecordCandidateLifecycleEvent(LiveCandidateLifecycleEventKind.CandidateWorkStarted, candidate,
                    frameCapturedAt: input.Region.Frame.CapturedAt);
                SignalWhenWorkCompletes(task);
            }
        }

        private bool InvalidateMenuTranslation(LiveCandidateState candidate)
        {
            var changed = candidate.Result is not null || candidate.Failure is not null;
            CancelActiveWork(candidate);
            candidate.Result = null;
            candidate.ResultPublishedAt = null;
            candidate.Failure = null;
            candidate.TranslationRetry.Reset();
            forceImmediateEmptyOverlayPublication = true;
            return changed;
        }

        private async Task<bool> CollectMenuWorkAsync()
        {
            var changed = false;
            foreach (var candidate in zoneStates.Values.SelectMany(s => s.CandidateStates.Values))
                if (candidate.Menu?.Authority.Expire(MenuNow) == true)
                {
                    changed |= InvalidateMenuTranslation(candidate);
                    RecordMenuDecision(candidate, "text-expired", candidate.Menu.LatestAcceptedResult?.CapturedFrame);
                }
            PumpMenuOcr();
            foreach (var work in menuOcrWork.Where(w => w.Task.IsCompleted).ToArray())
            {
                menuOcrWork.Remove(work);
                var current = menuScheduler.Complete(work.Lease);
                var input = work.Lease.Work;
                var candidate = input.Candidate;
                try
                {
                    var result = await work.Task;
                    if (!current)
                    {
                        RecordMenuDecision(candidate, "ocr-retired-generation", result.CapturedFrame, MenuNow - work.Lease.StartedAt);
                        continue;
                    }
                    var projected = TranslationTextGroupingService.CreateTranslationSourceResult(result.SourceOcrResult,
                        input.Zone, profile.TranslatorSettings.AutoDetectSourceLanguage == true);
                    if (candidate.Region.Frame.Origin is { } geometryOrigin
                        && geometryOrigin.SourceTimestamp < work.Lease.Origin.SourceTimestamp
                        && candidate.Menu!.Authority.RevokeIfDifferent(work.Lease.Origin, projected.TextBlocks.Select(b => b.Text), MenuNow))
                    {
                        changed |= InvalidateMenuTranslation(candidate);
                        RecordMenuDecision(candidate, "text-revoked-before-geometry", result.CapturedFrame);
                    }
                    candidate.Menu!.CompletedOcr = new CompletedMenuOcr(input, result, work.Lease.Origin, MenuNow - work.Lease.StartedAt);
                }
                catch (OperationCanceledException) when (work.Cancellation.IsCancellationRequested) { }
                catch (TranslationPipelineException exception)
                {
                    if (current)
                    {
                        changed |= InvalidateMenuTranslation(candidate);
                        candidate.Failure = CreateZoneFailure(candidate.Zone, exception);
                    }
                }
                finally { work.Cancellation.Dispose(); }
            }
            foreach (var candidate in zoneStates.Values.SelectMany(s => s.CandidateStates.Values))
            {
                changed |= AcceptCompletedMenuOcr(candidate);
                if (candidate.Menu is not { } menu || candidate.ActiveWork is not { } work) continue;
                CollectCandidateProgress(candidate, work);
                if (!work.Task.IsCompleted) continue;
                candidate.ActiveWork = null;
                try
                {
                    var result = await work.Task;
                    if (!menu.Authority.CanPublish(menu.TranslatingVersion, MenuNow))
                    {
                        RecordMenuDecision(candidate, "translation-rejected-version-or-age");
                        continue;
                    }
                    candidate.Result = PlaceMenuTranslation(candidate, result);
                    candidate.ResultPublishedAt = null;
                    candidate.Failure = null;
                    candidate.TranslationRetry.Reset();
                    changed = true;
                    RecordMenuDecision(candidate, "translation-published");
                    RecordCandidateLifecycleEvent(LiveCandidateLifecycleEventKind.CandidateWorkCompleted, candidate, result);
                }
                catch (OperationCanceledException) when (work.Cancellation.IsCancellationRequested) { }
                catch (TranslationPipelineException exception)
                {
                    candidate.Failure = CreateZoneFailure(candidate.Zone, exception);
                    candidate.TranslationRetry.Schedule(exception, service.timeProvider.GetUtcNow());
                    changed = true;
                }
                finally
                {
                    RecordCandidateProviderRequestDiagnostics(candidate, work);
                    work.Cancellation.Dispose();
                }
            }
            PumpMenuOcr();
            return changed;
        }

        private bool AcceptCompletedMenuOcr(LiveCandidateState candidate)
        {
            if (candidate.Menu?.CompletedOcr is not { } completed) return false;
            var input = completed.Input;
            var result = completed.Result;
            if (MenuNow - completed.Origin.FreshnessTimestamp > TimeSpan.FromSeconds(2))
            {
                candidate.Menu.CompletedOcr = null;
                RecordMenuDecision(candidate, "ocr-rejected-source", result.CapturedFrame, completed.WorkAge);
                return false;
            }
            var detectedOrigin = candidate.Region.Frame.Origin;
            if (detectedOrigin is null || detectedOrigin.SourceId != completed.Origin.SourceId
                || detectedOrigin.SourceTimestamp < completed.Origin.SourceTimestamp) return false;
            candidate.Menu.CompletedOcr = null;
            // Both observations must belong to the same fixed discovery anchor.
            // Comparing consecutive jittered boxes instead introduces a second,
            // inconsistent identity rule (compatibility is not transitive).
            if (!MenuGeometryMatches(candidate, input.Region, getCore: _ => input.Core)
                || !MenuGeometryMatches(candidate, candidate.Region, getCore: _ => candidate.Menu.CurrentCore))
            {
                RecordMenuDecision(candidate, "ocr-rejected-geometry", result.CapturedFrame, completed.WorkAge);
                return false;
            }
            var changed = false;
            var menu = candidate.Menu!;
            var version = menu.Authority.Version;
            var projected = TranslationTextGroupingService.CreateTranslationSourceResult(result.SourceOcrResult,
                input.Zone, profile.TranslatorSettings.AutoDetectSourceLanguage == true);
            if (!menu.Authority.Observe(completed.Origin, projected.TextBlocks.Select(b => b.Text), MenuNow))
            {
                RecordMenuDecision(candidate, "ocr-rejected-source", result.CapturedFrame, completed.WorkAge);
                return false;
            }
            if (menu.Authority.Version != version) changed |= InvalidateMenuTranslation(candidate);
            RecordMenuDecision(candidate, menu.Authority.Version != version ? "text-version-changed" : "text-observed",
                result.CapturedFrame, completed.WorkAge);
            menu.LatestAcceptedInput = input;
            menu.LatestAcceptedResult = result;
            if (candidate.Result is { TranslateResponse: not null } existing && menu.Authority.Confirmed)
            {
                var refreshed = PlaceMenuTranslation(candidate, existing);
                changed |= !SameMenuPresentation(existing.OverlaySnapshot, refreshed.OverlaySnapshot);
                candidate.Result = refreshed;
            }
            RecordCandidateLifecycleEvent(LiveCandidateLifecycleEventKind.CandidateOcrCompleted, candidate, result,
                frameCapturedAt: result.CapturedFrame.CapturedAt);
            if (menu.Authority.Confirmed && candidate.ActiveWork is null && candidate.Result is null
                && (candidate.Failure is null || candidate.TranslationRetry.IsDue(service.timeProvider.GetUtcNow())))
                StartMenuTranslation(candidate, input, result);
            return changed;
        }

        private void StartMenuTranslation(LiveCandidateState candidate, MenuOcrInput input, TranslationPipelineResult recognized)
        {
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationSource.Token);
            var diagnostics = new ProviderRequestDiagnosticsCollector();
            candidate.Menu!.TranslatingVersion = candidate.Menu.Authority.Version;
            var task = service.RunCapturedZoneAsync(CreateCandidateProfile(input.Zone), input.Zone, input.Region.Frame,
                input.CaptureElapsed, null, new TranslationPipelineRunOptions(enableCandidateDetectorPilot: false),
                cancellation.Token, input.Constraints, candidateTranslationLimiter,
                providerRequestDiagnosticsCollector: diagnostics, preparedSourceResult: recognized.SourceOcrResult);
            candidate.ActiveWork = new LiveZoneWork(cancellation, task, candidate.Revision,
                ProviderRequestDiagnostics: diagnostics);
            SignalWhenWorkCompletes(task);
        }

        private TranslationPipelineResult PlaceMenuTranslation(LiveCandidateState candidate, TranslationPipelineResult translated)
        {
            var input = candidate.Menu!.LatestAcceptedInput!;
            var recognized = candidate.Menu.LatestAcceptedResult!;
            var projected = TranslationTextGroupingService.CreateTranslationSourceResult(recognized.SourceOcrResult,
                input.Zone, profile.TranslatorSettings.AutoDetectSourceLanguage == true);
            var snapshot = service.overlayPositioningService.CreateSnapshot(CreateTranslatedResult(projected, translated.TranslateResponse!),
                translated.TranslateResponse!.TranslatedAt, null,
                input.Zone.TextStyle with { LayoutMode = OverlayTextLayoutMode.KeepWithinSourceBounds },
                profile.OverlaySettings, input.Constraints);
            return new TranslationPipelineResult(profile.Id, candidate.Zone.Id, recognized.CapturedFrame,
                recognized.SourceOcrResult, translated.TranslateResponse, snapshot, translated.CacheResult,
                translated.Timings, translated.Optimization, projected.TextBlocks.Count, translated.TextStability);
        }

        private void RecordMenuDecision(LiveCandidateState candidate, string decision, CapturedFrame? frame = null, TimeSpan? workAge = null)
        {
            var menu = candidate.Menu!;
            frame ??= menu.LatestAcceptedResult?.CapturedFrame ?? candidate.Region.Frame;
            var origin = frame.Origin;
            RecordLifecycleEvent(LiveCandidateLifecycleEventKind.CandidateMenuAuthorityObserved,
                zoneId: candidate.SourceZoneId, candidateId: candidate.Id, candidateBounds: candidate.Region.Candidate.Bounds,
                menuDiagnostics: new MenuLiveDiagnostics(menu.Generation, menu.Authority.Version, origin?.SourceId,
                    origin?.SourceTimestamp.Ticks, origin is null ? null : (MenuNow - origin.FreshnessTimestamp).TotalMilliseconds,
                    frame.IsCachedReuse, menu.Authority.ObservationCount, menu.Authority.Confirmed, menu.Periodic,
                    menuScheduler.ActiveCount, menuScheduler.PendingCount, menuScheduler.ReplacedPendingCount,
                    menuScheduler.ExpiredPendingCount, decision, workAge?.TotalMilliseconds));
        }

        private static bool SameMenuPresentation(OverlaySnapshot left, OverlaySnapshot right)
        {
            return left.OverlaySettings == right.OverlaySettings
                && left.TextItems.Select(i => (i.Text, i.X, i.Y, i.Width, i.Height, i.TextStyle, i.UseCalloutPresentation))
                    .SequenceEqual(right.TextItems.Select(i => (i.Text, i.X, i.Y, i.Width, i.Height, i.TextStyle, i.UseCalloutPresentation)))
                && left.MaskItems.Select(i => (i.Mode, i.Color, i.Opacity, i.X, i.Y, i.Width, i.Height))
                    .SequenceEqual(right.MaskItems.Select(i => (i.Mode, i.Color, i.Opacity, i.X, i.Y, i.Width, i.Height)))
                && left.DebugItems.Select(i => (i.SourceText, i.TranslatedText, i.X, i.Y, i.Width, i.Height))
                    .SequenceEqual(right.DebugItems.Select(i => (i.SourceText, i.TranslatedText, i.X, i.Y, i.Width, i.Height)))
                && left.DebugMetricLines.SequenceEqual(right.DebugMetricLines);
        }

        private sealed record MenuOcrInput(LiveCandidateState Candidate, OcrZone Zone, TextCandidateRegion Region,
            TimeSpan CaptureElapsed, OverlayPlacementConstraints Constraints, MenuGlyphCoreEvidence? Core);
        private sealed record CompletedMenuOcr(MenuOcrInput Input, TranslationPipelineResult Result, CaptureFrameOrigin Origin, TimeSpan WorkAge);
        private sealed record MenuOcrWork(MenuOcrScheduler<MenuOcrInput>.Lease Lease, CancellationTokenSource Cancellation,
            Task<TranslationPipelineResult> Task);
        private sealed class MenuCandidateState(TextCandidateRegion anchor, MenuTextAuthority authority,
            MenuFixedInkEvidence? anchorInk = null, MenuGlyphCoreEvidence? anchorCore = null)
        {
            internal BoundingBox AnchorBounds { get; } = anchor.Candidate.Bounds;
            internal BoundingBox[] AnchorMembers { get; } = OrderCandidateMemberBounds(anchor.Candidate.SourceCandidateBounds);
            internal MenuFixedInkEvidence? AnchorInk { get; } = anchorInk;
            internal MenuGlyphCoreEvidence? AnchorCore { get; } = anchorCore;
            internal MenuGlyphCoreEvidence? CurrentCore { get; set; }
            internal MenuTextAuthority Authority { get; } = authority;
            internal CompletedMenuOcr? CompletedOcr { get; set; }
            internal long TranslatingVersion { get; set; }
            internal long Generation { get; set; }
            internal bool Periodic { get; set; }
            internal MenuOcrInput? LatestAcceptedInput { get; set; }
            internal TranslationPipelineResult? LatestAcceptedResult { get; set; }
        }
    }
}
