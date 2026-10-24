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

        private TimeSpan MenuNow => TimeSpan.FromSeconds(
            service.timeProvider.GetTimestamp() / (double)service.timeProvider.TimestampFrequency);

        private bool ReconcileMenuRegions(LiveZoneState state, LiveCapturedZone captured,
            IReadOnlyList<TextCandidateRegion> regions, ICollection<string> cancelledIds)
        {
            var changed = false;
            // Match all pairs before mutating tracks. Both directions must be unambiguous.
            var previous = state.CandidateStates.Values.ToArray();
            var matches = MenuGeometryMatcher.MatchUnique(previous, regions,
                (c, r) => MenuGeometryMatches(c, r)
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
                            runOptions.RequireStableTextBeforeTranslation ? runOptions.StableTextInterval : TimeSpan.FromMilliseconds(200))),
                    };
                    state.CandidateStates.Add(id, candidate);
                    candidate.Menu.Generation = menuScheduler.Register(id);
                    RecordCandidateLifecycleEvent(LiveCandidateLifecycleEventKind.CandidateDiscovered, candidate,
                        frameCapturedAt: captured.Frame.CapturedAt);
                }
                var current = candidate!;
                var pixelsChanged = !current.SourceIdentity.Matches(region.Frame);
                current.Region = region;
                current.Zone = CreateCandidateZone(state.Zone, region.Candidate.Bounds) with { Id = current.Zone.Id };
                current.GeometrySignature = CreateCandidateGeometrySignature(region.Candidate);
                current.SourceIdentity = FrameFingerprint.FromFrame(region.Frame);
                var outcome = menuScheduler.Offer(current.Id, new MenuOcrInput(current, current.Zone, region,
                    captured.CaptureElapsed, CreateCandidateOverlayPlacementConstraints(state.Zone, regions, region)),
                    region.Frame.Origin, pixelsChanged, MenuNow);
                if (outcome == MenuOcrScheduler<MenuOcrInput>.OfferOutcome.Queued && !isNew)
                    ObserveMatchingCandidateGrouping(current, region.Frame.Origin!.CapturedAt);
            }
            PumpMenuOcr();
            return changed;
        }

        private static bool MenuGeometryMatches(LiveCandidateState candidate, TextCandidateRegion current)
        {
            if (candidate.Menu is not { } menu) return false;
            return MenuGeometryMatcher.Matches(menu.AnchorBounds, menu.AnchorMembers,
                current.Candidate.Bounds, current.Candidate.SourceCandidateBounds);
        }

        private void PumpMenuOcr()
        {
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
                        zoneStates[candidate.SourceZoneId].Zone.AbsoluteBounds.Height, WatermarkEvidence: input.Region.WatermarkEvidence),
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
                    var menu = candidate.Menu!;
                    var version = menu.Authority.Version;
                    var projected = TranslationTextGroupingService.CreateTranslationSourceResult(result.SourceOcrResult,
                        input.Zone, profile.TranslatorSettings.AutoDetectSourceLanguage == true);
                    if (!menu.Authority.Observe(work.Lease.Origin, projected.TextBlocks.Select(b => b.Text), MenuNow))
                    {
                        RecordMenuDecision(candidate, "ocr-rejected-source", result.CapturedFrame, MenuNow - work.Lease.StartedAt);
                        continue;
                    }
                    if (menu.Authority.Version != version) changed |= InvalidateMenuTranslation(candidate);
                    RecordMenuDecision(candidate, menu.Authority.Version != version ? "text-version-changed" : "text-observed",
                        result.CapturedFrame, MenuNow - work.Lease.StartedAt);
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
                && left.DebugMetricLines.SequenceEqual(right.DebugMetricLines)
                && Equals(left.PlacementConstraints?.PlacementRegion, right.PlacementConstraints?.PlacementRegion)
                && (left.PlacementConstraints?.OccupiedRegions ?? []).SequenceEqual(right.PlacementConstraints?.OccupiedRegions ?? []);
        }

        private sealed record MenuOcrInput(LiveCandidateState Candidate, OcrZone Zone, TextCandidateRegion Region,
            TimeSpan CaptureElapsed, OverlayPlacementConstraints Constraints);
        private sealed record MenuOcrWork(MenuOcrScheduler<MenuOcrInput>.Lease Lease, CancellationTokenSource Cancellation,
            Task<TranslationPipelineResult> Task);
        private sealed class MenuCandidateState(TextCandidateRegion anchor, MenuTextAuthority authority)
        {
            internal BoundingBox AnchorBounds { get; } = anchor.Candidate.Bounds;
            internal BoundingBox[] AnchorMembers { get; } = OrderCandidateMemberBounds(anchor.Candidate.SourceCandidateBounds);
            internal MenuTextAuthority Authority { get; } = authority;
            internal long TranslatingVersion { get; set; }
            internal long Generation { get; set; }
            internal bool Periodic { get; set; }
            internal MenuOcrInput? LatestAcceptedInput { get; set; }
            internal TranslationPipelineResult? LatestAcceptedResult { get; set; }
        }
    }
}
