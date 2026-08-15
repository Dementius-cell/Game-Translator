using System.Diagnostics;
using GameTranslator.Application.Cache;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Credentials;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Translation;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Pipeline;

public sealed class TranslationPipelineService
{
    private readonly CaptureService captureService;
    private readonly OcrService ocrService;
    private readonly TranslatorManager translatorManager;
    private readonly TranslatorCredentialService credentialService;
    private readonly TranslationCacheService cacheService;
    private readonly OverlayPositioningService overlayPositioningService;
    private readonly IOverlayService overlayService;
    private readonly TranslationPipelineOptimizationOptions optimizationOptions;
    private readonly TextCandidateRegionOcrService candidateRegionOcrService;
    private readonly object optimizationStateLock = new();
    private readonly object textStabilityStateLock = new();
    private readonly Dictionary<PipelineFrameStateKey, PipelineFrameState> optimizationStates = new();
    private readonly Dictionary<PipelineFrameStateKey, PipelineTextStabilityState> textStabilityStates = new();

    public TranslationPipelineService(
        CaptureService captureService,
        OcrService ocrService,
        TranslatorManager translatorManager,
        TranslatorCredentialService credentialService,
        TranslationCacheService cacheService,
        OverlayPositioningService overlayPositioningService,
        IOverlayService overlayService,
        TranslationPipelineOptimizationOptions? optimizationOptions = null,
        TextCandidateRegionOcrService? candidateRegionOcrService = null)
    {
        this.captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        this.translatorManager = translatorManager ?? throw new ArgumentNullException(nameof(translatorManager));
        this.credentialService = credentialService ?? throw new ArgumentNullException(nameof(credentialService));
        this.cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        this.overlayPositioningService = overlayPositioningService ?? throw new ArgumentNullException(nameof(overlayPositioningService));
        this.overlayService = overlayService ?? throw new ArgumentNullException(nameof(overlayService));
        this.optimizationOptions = optimizationOptions ?? new TranslationPipelineOptimizationOptions();
        this.candidateRegionOcrService = candidateRegionOcrService
            ?? new TextCandidateRegionOcrService(new UnavailableTextCandidateDetector(), this.ocrService);
    }

    public Task<TranslationPipelineResult> RunAsync(
        GameProfile profile,
        OcrZone zone,
        OverlaySnapshot? previousSnapshot = null,
        TranslationPipelineRunOptions? runOptions = null,
        CancellationToken cancellationToken = default)
    {
        profile = NormalizeTranslatorLanguageTags(profile);
        return RunZoneAsync(
            profile,
            zone,
            previousSnapshot,
            showOverlay: true,
            runOptions ?? TranslationPipelineRunOptions.Default,
            cancellationToken);
    }

    public async Task<TranslationPipelineBatchResult> RunAllZonesAsync(
        GameProfile profile,
        OverlaySnapshot? previousSnapshot = null,
        TranslationPipelineRunOptions? runOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        profile = NormalizeTranslatorLanguageTags(profile);
        runOptions ??= TranslationPipelineRunOptions.Default;

        var zones = (profile.OcrZones ?? Array.Empty<OcrZone>()).ToArray();
        if (zones.Length == 0)
        {
            throw new ArgumentException("Profile must contain at least one OCR zone.", nameof(profile));
        }

        if (runOptions.EnableCandidateDetectorPilot)
        {
            return await RunAllCandidateRegionsAsync(
                profile,
                zones,
                previousSnapshot,
                runOptions,
                cancellationToken);
        }

        var resultSlots = new TranslationPipelineResult?[zones.Length];
        var failureSlots = new TranslationPipelineZoneFailure?[zones.Length];
        var pendingZoneTasks = new List<PipelineZoneTask>(zones.Length);

        for (var index = 0; index < zones.Length; index++)
        {
            var zone = zones[index];
            pendingZoneTasks.Add(new PipelineZoneTask(
                index,
                zone,
                RunZoneAsync(
                    profile,
                    zone,
                    previousSnapshot: null,
                    showOverlay: false,
                    runOptions,
                    cancellationToken,
                    overlaySnapshotToRestoreAfterCapture: previousSnapshot)));
        }

        OverlaySnapshot? combinedSnapshot = null;
        while (pendingZoneTasks.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var completedTask = await Task.WhenAny(pendingZoneTasks.Select(pending => pending.Task));
            var completedIndex = pendingZoneTasks.FindIndex(pending => ReferenceEquals(pending.Task, completedTask));
            if (completedIndex < 0)
            {
                throw new InvalidOperationException("Completed zone task was not tracked by the pipeline.");
            }

            var completedZoneTask = pendingZoneTasks[completedIndex];
            pendingZoneTasks.RemoveAt(completedIndex);

            try
            {
                resultSlots[completedZoneTask.Index] = await completedZoneTask.Task;
            }
            catch (TranslationPipelineException exception)
            {
                failureSlots[completedZoneTask.Index] = CreateZoneFailure(completedZoneTask.Zone, exception);
            }

            var completedResults = CreateOrderedResults(resultSlots);
            combinedSnapshot = CreateCombinedSnapshot(completedResults, previousSnapshot, profile.OverlaySettings, runOptions);
            await ShowOverlayAsync(combinedSnapshot);
        }

        var results = CreateOrderedResults(resultSlots);
        var failures = CreateOrderedFailures(failureSlots);
        combinedSnapshot ??= CreateCombinedSnapshot(results, previousSnapshot, profile.OverlaySettings, runOptions);

        return new TranslationPipelineBatchResult(
            profile.Id,
            results,
            failures,
            combinedSnapshot);
    }

    private async Task<TranslationPipelineBatchResult> RunAllCandidateRegionsAsync(
        GameProfile profile,
        IReadOnlyList<OcrZone> zones,
        OverlaySnapshot? previousSnapshot,
        TranslationPipelineRunOptions runOptions,
        CancellationToken cancellationToken)
    {
        if (runOptions.RequireCandidateReadinessBarrier)
        {
            throw new InvalidOperationException(
                "ADR-028 readiness requires a persistent LiveTranslationSession; one-shot candidate runs cannot publish before a verified prewarm.");
        }

        var resultSlots = new List<TranslationPipelineResult?>();
        var failureSlots = new List<TranslationPipelineZoneFailure?>();
        var pendingCandidateTasks = new List<PipelineZoneTask>();
        using var candidateTranslationLimiter = CreateCandidateTranslationLimiter(runOptions);

        foreach (var zone in zones)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CapturedFrame frame;
            TimeSpan captureElapsed;
            try
            {
                var captureMeasurement = await RunTimedStageAsync(
                    TranslationPipelineStage.Capture,
                    () => CaptureFrameAsync(zone, runOptions, previousSnapshot, cancellationToken));
                frame = captureMeasurement.Value;
                captureElapsed = captureMeasurement.Elapsed;
            }
            catch (TranslationPipelineException exception)
            {
                failureSlots.Add(CreateZoneFailure(zone, exception));
                resultSlots.Add(null);
                continue;
            }

            TextCandidateRegionDetectionResult detection;
            try
            {
                var detectionMeasurement = await RunTimedStageAsync(
                    TranslationPipelineStage.Ocr,
                    () => candidateRegionOcrService.DetectAsync(
                        CreateOcrRequest(profile, zone, frame),
                        cancellationToken));
                detection = detectionMeasurement.Value;
            }
            catch (TranslationPipelineException exception)
            {
                failureSlots.Add(CreateZoneFailure(zone, exception));
                resultSlots.Add(null);
                continue;
            }

            if (detection.Availability != TextCandidateDetectorAvailability.Available)
            {
                resultSlots.Add(null);
                failureSlots.Add(CreateCandidateDetectorUnavailableFailure(zone, detection));
                continue;
            }

            var orderedRegions = OrderCandidateRegions(detection.Regions);
            foreach (var region in orderedRegions)
            {
                var candidateZone = CreateTransientCandidateZone(zone, region.Candidate.Bounds);
                var candidateIndex = resultSlots.Count;
                resultSlots.Add(null);
                failureSlots.Add(null);
                pendingCandidateTasks.Add(new PipelineZoneTask(
                    candidateIndex,
                    candidateZone,
                    RunCapturedZoneAsync(
                        CreateTransientCandidateProfile(profile, candidateZone),
                        candidateZone,
                        region.Frame,
                        captureElapsed,
                        previousSnapshot: null,
                        CreateCandidateRunOptionsWithoutDetector(runOptions),
                        cancellationToken,
                        CreateCandidateOverlayPlacementConstraints(zone, orderedRegions, region),
                        candidateTranslationLimiter)));
            }
        }

        OverlaySnapshot? combinedSnapshot = null;
        while (pendingCandidateTasks.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completedTask = await Task.WhenAny(pendingCandidateTasks.Select(pending => pending.Task));
            var completedIndex = pendingCandidateTasks.FindIndex(pending => ReferenceEquals(pending.Task, completedTask));
            if (completedIndex < 0)
            {
                throw new InvalidOperationException("Completed candidate task was not tracked by the pipeline.");
            }

            var completedCandidateTask = pendingCandidateTasks[completedIndex];
            pendingCandidateTasks.RemoveAt(completedIndex);
            try
            {
                resultSlots[completedCandidateTask.Index] = await completedCandidateTask.Task;
            }
            catch (TranslationPipelineException exception)
            {
                failureSlots[completedCandidateTask.Index] = CreateZoneFailure(completedCandidateTask.Zone, exception);
            }

            var completedResults = resultSlots
                .Where(result => result is not null)
                .Cast<TranslationPipelineResult>()
                .ToArray();
            combinedSnapshot = CreateCombinedSnapshot(
                completedResults,
                previousSnapshot,
                profile.OverlaySettings,
                runOptions);
            await ShowOverlayAsync(combinedSnapshot);
        }

        var results = resultSlots
            .Where(result => result is not null)
            .Cast<TranslationPipelineResult>()
            .ToArray();
        var failures = failureSlots
            .Where(failure => failure is not null)
            .Cast<TranslationPipelineZoneFailure>()
            .ToArray();
        if (combinedSnapshot is null)
        {
            combinedSnapshot = CreateCombinedSnapshot(
                results,
                previousSnapshot,
                profile.OverlaySettings,
                runOptions);
            await ShowOverlayAsync(combinedSnapshot);
        }

        return new TranslationPipelineBatchResult(
            profile.Id,
            results,
            failures,
            combinedSnapshot);
    }

    public LiveTranslationSession CreateLiveSession(
        GameProfile profile,
        TranslationPipelineRunOptions? runOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.OcrZones is null || profile.OcrZones.Count == 0)
        {
            throw new ArgumentException("Profile must contain at least one OCR zone.", nameof(profile));
        }

        profile = NormalizeTranslatorLanguageTags(profile);

        return new LiveTranslationSession(
            this,
            profile,
            runOptions ?? TranslationPipelineRunOptions.Default,
            cancellationToken);
    }

    private async Task<TranslationPipelineResult> RunZoneAsync(
        GameProfile profile,
        OcrZone zone,
        OverlaySnapshot? previousSnapshot,
        bool showOverlay,
        TranslationPipelineRunOptions runOptions,
        CancellationToken cancellationToken,
        OverlaySnapshot? overlaySnapshotToRestoreAfterCapture = null,
        CapturedFrame? capturedFrame = null,
        TimeSpan? capturedFrameElapsed = null,
        OverlayPlacementConstraints? overlayPlacementConstraints = null,
        SemaphoreSlim? candidateTranslationLimiter = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(zone);
        cancellationToken.ThrowIfCancellationRequested();

        if (!profile.OcrZones.Any(profileZone => string.Equals(profileZone.Id, zone.Id, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Pipeline OCR zone must belong to the supplied profile.", nameof(zone));
        }

        var totalStopwatch = Stopwatch.StartNew();
        var captureElapsed = TimeSpan.Zero;
        var ocrElapsed = TimeSpan.Zero;
        var credentialsElapsed = TimeSpan.Zero;
        var translationElapsed = TimeSpan.Zero;
        var cacheElapsed = TimeSpan.Zero;
        var overlayElapsed = TimeSpan.Zero;

        CapturedFrame frame;
        if (capturedFrame is null)
        {
            var frameMeasurement = await RunTimedStageAsync(
                TranslationPipelineStage.Capture,
                () => CaptureFrameAsync(
                    zone,
                    runOptions,
                    overlaySnapshotToRestoreAfterCapture ?? previousSnapshot,
                    cancellationToken));
            frame = frameMeasurement.Value;
            captureElapsed = frameMeasurement.Elapsed;
        }
        else
        {
            frame = capturedFrame;
            captureElapsed = capturedFrameElapsed ?? TimeSpan.Zero;
        }

        var optimizationContext = CreateOptimizationContext(profile, zone, frame, runOptions);
        if (optimizationContext.ShouldReusePreviousResult && optimizationContext.PreviousState is not null)
        {
            var reusedResult = CreateReusedResult(
                profile,
                zone,
                frame,
                optimizationContext.PreviousState.Result,
                captureElapsed,
                optimizationContext);
            if (showOverlay)
            {
                overlayElapsed = await ShowOverlayAsync(reusedResult.OverlaySnapshot);
                totalStopwatch.Stop();
                reusedResult = ReplaceResultTimings(
                    reusedResult,
                    captureElapsed,
                    ocrElapsed,
                    credentialsElapsed,
                    translationElapsed,
                    cacheElapsed,
                    overlayElapsed,
                    totalStopwatch.Elapsed);
            }
            else
            {
                totalStopwatch.Stop();
                reusedResult = ReplaceResultTimings(
                    reusedResult,
                    captureElapsed,
                    ocrElapsed,
                    credentialsElapsed,
                    translationElapsed,
                    cacheElapsed,
                    overlayElapsed,
                    totalStopwatch.Elapsed);
            }

            StoreOptimizationState(optimizationContext.StateKey, frame, reusedResult);
            return reusedResult;
        }

        var request = CreateOcrRequest(profile, zone, frame);
        var ocrMeasurement = await RunTimedStageAsync(
            TranslationPipelineStage.Ocr,
            () => runOptions.EnableCandidateDetectorPilot
                ? RecognizeCandidateRegionsAsync(request, cancellationToken)
                : ocrService.RecognizeAsync(request, cancellationToken));
        var sourceResult = ocrMeasurement.Value;
        ocrElapsed = ocrMeasurement.Elapsed;

        if (sourceResult.TextBlocks.Count == 0)
        {
            ClearTextStabilityState(optimizationContext.StateKey);
            var emptySnapshot = overlayPositioningService.CreateSnapshot(
                sourceResult,
                sourceResult.RecognizedAt,
                previousSnapshot,
                zone.TextStyle,
                profile.OverlaySettings,
                overlayPlacementConstraints);
            if (showOverlay)
            {
                overlayElapsed = await ShowOverlayAsync(emptySnapshot);
            }

            totalStopwatch.Stop();

            var emptyResult = new TranslationPipelineResult(
                profile.Id,
                zone.Id,
                frame,
                sourceResult,
                translateResponse: null,
                emptySnapshot,
                cacheResult: null,
                CreateTimings(
                    captureElapsed,
                    ocrElapsed,
                    credentialsElapsed,
                    translationElapsed,
                    cacheElapsed,
                    overlayElapsed,
                    totalStopwatch.Elapsed),
                CreateProcessedOptimization(optimizationContext));
            StoreOptimizationState(optimizationContext.StateKey, frame, emptyResult);

            return emptyResult;
        }

        var groupingMeasurement = await RunTimedStageAsync(
            TranslationPipelineStage.Grouping,
            () => Task.FromResult(TranslationTextGroupingService.CreateTranslationSourceResult(sourceResult, zone)));
        var translationSourceResult = groupingMeasurement.Value;

        if (!IsTextStableForTranslation(optimizationContext.StateKey, translationSourceResult, runOptions))
        {
            var pendingSnapshot = runOptions.PreservePreviousOverlayWhileWaitingForStableText
                ? previousSnapshot ?? CreateEmptySnapshot(sourceResult.RecognizedAt, profile.OverlaySettings)
                : CreateEmptySnapshot(sourceResult.RecognizedAt, profile.OverlaySettings);
            if (showOverlay)
            {
                overlayElapsed = await ShowOverlayAsync(pendingSnapshot);
            }

            totalStopwatch.Stop();

            var pendingResult = new TranslationPipelineResult(
                profile.Id,
                zone.Id,
                frame,
                sourceResult,
                translateResponse: null,
                pendingSnapshot,
                cacheResult: null,
                CreateTimings(
                    captureElapsed,
                    ocrElapsed,
                    credentialsElapsed,
                    translationElapsed,
                    cacheElapsed,
                    overlayElapsed,
                    totalStopwatch.Elapsed),
                new TranslationPipelineOptimizationInfo(
                    ocrSkipped: false,
                    translationSkipped: true,
                    debounced: true,
                    frameDifferenceRatio: optimizationContext.FrameDifferenceRatio));
            StoreOptimizationState(optimizationContext.StateKey, frame, pendingResult);

            return pendingResult;
        }

        try
        {
            var texts = translationSourceResult.TextBlocks.Select(block => block.Text).ToArray();
            var cacheMeasurement = await RunTimedStageAsync(
                TranslationPipelineStage.Cache,
                () => cacheService.GetOrAddAsync(
                    profile.TranslatorSettings,
                    texts,
                    async missingTexts =>
                    {
                        var acquiredCandidateTranslationSlot = false;
                        if (candidateTranslationLimiter is not null)
                        {
                            await candidateTranslationLimiter.WaitAsync(cancellationToken);
                            acquiredCandidateTranslationSlot = true;
                        }

                        try
                        {
                            var credentialsMeasurement = await RunTimedStageAsync(
                                TranslationPipelineStage.Credentials,
                                () => credentialService.CreateCredentialsAsync(profile.TranslatorSettings.Provider, cancellationToken));
                            credentialsElapsed += credentialsMeasurement.Elapsed;

                            var translationMeasurement = await RunTimedStageAsync(
                                TranslationPipelineStage.Translation,
                                () => translatorManager.TranslateAsync(profile.TranslatorSettings, missingTexts, credentialsMeasurement.Value, cancellationToken));
                            translationElapsed += translationMeasurement.Elapsed;

                            return translationMeasurement.Value;
                        }
                        finally
                        {
                            if (acquiredCandidateTranslationSlot)
                            {
                                candidateTranslationLimiter!.Release();
                            }
                        }
                    },
                    DateTimeOffset.UtcNow,
                    cancellationToken));
            var cacheResult = cacheMeasurement.Value;
            cacheElapsed = cacheMeasurement.Elapsed;
            var translateResponse = cacheResult.ToTranslateResponse();

            if (translateResponse.TranslatedTexts.Count != translationSourceResult.TextBlocks.Count)
            {
                throw new TranslationPipelineException(
                    TranslationPipelineStage.Translation,
                    "Translation pipeline failed during Translation.",
                    new InvalidOperationException("Translator response item count must match OCR text block count."),
                    frame,
                    sourceResult);
            }

            var translatedResult = CreateTranslatedResult(translationSourceResult, translateResponse);
            var snapshot = overlayPositioningService.CreateSnapshot(
                translatedResult,
                translateResponse.TranslatedAt,
                previousSnapshot,
                zone.TextStyle,
                profile.OverlaySettings,
                overlayPlacementConstraints);
            if (showOverlay)
            {
                overlayElapsed = await ShowOverlayAsync(snapshot);
            }

            totalStopwatch.Stop();

            var result = new TranslationPipelineResult(
                profile.Id,
                zone.Id,
                frame,
                sourceResult,
                translateResponse,
                snapshot,
                cacheResult,
                CreateTimings(
                    captureElapsed,
                    ocrElapsed,
                    credentialsElapsed,
                    translationElapsed,
                    cacheElapsed,
                    overlayElapsed,
                    totalStopwatch.Elapsed),
                CreateProcessedOptimization(optimizationContext));
            StoreOptimizationState(optimizationContext.StateKey, frame, result);

            return result;
        }
        catch (TranslationPipelineException exception) when (exception.SourceOcrResult is null)
        {
            throw new TranslationPipelineException(
                exception.Stage,
                exception.Message,
                exception.InnerException ?? exception,
                frame,
                sourceResult);
        }
    }

    private Task<TranslationPipelineResult> RunCapturedZoneAsync(
        GameProfile profile,
        OcrZone zone,
        CapturedFrame frame,
        TimeSpan captureElapsed,
        OverlaySnapshot? previousSnapshot,
        TranslationPipelineRunOptions runOptions,
        CancellationToken cancellationToken,
        OverlayPlacementConstraints? overlayPlacementConstraints = null,
        SemaphoreSlim? candidateTranslationLimiter = null)
    {
        return RunZoneAsync(
            profile,
            zone,
            previousSnapshot,
            showOverlay: false,
            runOptions,
            cancellationToken,
            overlaySnapshotToRestoreAfterCapture: null,
            capturedFrame: frame,
            capturedFrameElapsed: captureElapsed,
            overlayPlacementConstraints: overlayPlacementConstraints,
            candidateTranslationLimiter: candidateTranslationLimiter);
    }

    private async Task<OcrResult> RecognizeCandidateRegionsAsync(
        OcrRequest zoneRequest,
        CancellationToken cancellationToken)
    {
        var candidateResults = new List<TextCandidateRegionOcrResult>();
        await foreach (var candidateResult in candidateRegionOcrService
                           .RecognizeAsync(zoneRequest, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            candidateResults.Add(candidateResult);
        }

        var recognizedAt = candidateResults.Count == 0
            ? DateTimeOffset.UtcNow
            : candidateResults.Max(result => result.RecognizedAt);
        return new OcrResult(
            zoneRequest,
            candidateResults.Select(result => result.CreateSourceTextBlock()),
            recognizedAt,
            candidateResults.Select(result => result.CreateSourceGeometry()));
    }

    private static OcrRequest CreateOcrRequest(
        GameProfile profile,
        OcrZone zone,
        CapturedFrame frame)
    {
        return new OcrRequest(
            frame,
            ResolveOcrLanguage(profile, zone),
            zone.Id,
            profile.OcrPreprocessingSettings,
            profile.OcrSettings.Engine,
            profile.OcrSettings.OrientationMode,
            ResolveOcrLayoutMode(zone));
    }

    private static IReadOnlyList<TextCandidateRegion> OrderCandidateRegions(
        IEnumerable<TextCandidateRegion> regions)
    {
        return regions
            .OrderBy(region => region.Candidate.Bounds.Y)
            .ThenBy(region => region.Candidate.Bounds.X)
            .ThenBy(region => region.Candidate.Bounds.Width)
            .ThenBy(region => region.Candidate.Bounds.Height)
            .ToArray();
    }

    private static OcrZone CreateTransientCandidateZone(OcrZone sourceZone, BoundingBox bounds)
    {
        return new OcrZone
        {
            Id = CreateTransientCandidateId(sourceZone, bounds),
            Name = $"{sourceZone.Name} candidate",
            AbsoluteBounds = new AbsoluteRectangle(
                checked(sourceZone.AbsoluteBounds.X + bounds.X),
                checked(sourceZone.AbsoluteBounds.Y + bounds.Y),
                bounds.Width,
                bounds.Height),
            RelativeBounds = sourceZone.RelativeBounds,
            OcrLanguage = sourceZone.OcrLanguage,
            // Candidate crops are intentionally transient. Their narrow vertical source geometry
            // must be laid out as readable horizontal translated text without changing the saved
            // profile's default layout mode.
            TextStyle = sourceZone.TextStyle with
            {
                LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter,
            },
            TranslationGroupingMode = TranslationGroupingMode.WholeZone,
            TextGrouping = OcrZoneTextGroupingSettings.Default,
        };
    }

    private static OverlayPlacementConstraints CreateCandidateOverlayPlacementConstraints(
        OcrZone sourceZone,
        IEnumerable<TextCandidateRegion> regions,
        TextCandidateRegion currentRegion)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(currentRegion);

        var placementRegion = new CaptureRegion(
            sourceZone.AbsoluteBounds.X,
            sourceZone.AbsoluteBounds.Y,
            sourceZone.AbsoluteBounds.Width,
            sourceZone.AbsoluteBounds.Height);
        var occupiedRegions = regions
            .Where(region => !ReferenceEquals(region, currentRegion))
            .Select(region => region.Candidate.Bounds)
            .Select(bounds => new CaptureRegion(
                checked(sourceZone.AbsoluteBounds.X + bounds.X),
                checked(sourceZone.AbsoluteBounds.Y + bounds.Y),
                bounds.Width,
                bounds.Height));

        return new OverlayPlacementConstraints(placementRegion, occupiedRegions);
    }

    private static string CreateTransientCandidateId(OcrZone sourceZone, BoundingBox bounds)
    {
        return $"{sourceZone.Id}:candidate:{bounds.X}:{bounds.Y}:{bounds.Width}:{bounds.Height}";
    }

    private static GameProfile CreateTransientCandidateProfile(
        GameProfile profile,
        OcrZone candidateZone)
    {
        return profile with
        {
            OcrZones = new[] { candidateZone },
            OcrSettings = profile.OcrSettings with { Engine = OcrSettings.TesseractEngineId },
        };
    }

    private static TranslationPipelineRunOptions CreateCandidateRunOptionsWithoutDetector(
        TranslationPipelineRunOptions sourceOptions)
    {
        return new TranslationPipelineRunOptions(
            sourceOptions.RequireStableTextBeforeTranslation,
            sourceOptions.StableTextInterval,
            sourceOptions.PreservePreviousOverlayWhileWaitingForStableText,
            sourceOptions.RestorePreviousOverlayAfterCapture,
            enableCandidateDetectorPilot: false,
            requireCandidateReadinessBarrier: sourceOptions.RequireCandidateReadinessBarrier,
            minimumCandidateOverlayVisibleDuration: sourceOptions.MinimumCandidateOverlayVisibleDuration,
            candidateTranslationMaxParallelism: sourceOptions.CandidateTranslationMaxParallelism,
            candidatePrewarmMaximumAttempts: sourceOptions.CandidatePrewarmMaximumAttempts,
            candidatePrewarmInitialRetryDelay: sourceOptions.CandidatePrewarmInitialRetryDelay);
    }

    private static SemaphoreSlim? CreateCandidateTranslationLimiter(
        TranslationPipelineRunOptions runOptions)
    {
        return runOptions.EnableCandidateDetectorPilot
            ? new SemaphoreSlim(
                runOptions.CandidateTranslationMaxParallelism,
                runOptions.CandidateTranslationMaxParallelism)
            : null;
    }

    private async Task<CapturedFrame> CaptureFrameAsync(
        OcrZone zone,
        TranslationPipelineRunOptions runOptions,
        OverlaySnapshot? overlaySnapshotToRestoreAfterCapture,
        CancellationToken cancellationToken)
    {
        var shouldRestoreOverlay = runOptions.RestorePreviousOverlayAfterCapture
            && overlaySnapshotToRestoreAfterCapture is not null
            && overlayService.IsVisible
            && !overlayService.IsExcludedFromCapture;

        if (!shouldRestoreOverlay)
        {
            return await captureService.CaptureAsync(CreateCaptureRegion(zone), cancellationToken);
        }

        overlayService.Hide();
        try
        {
            return await captureService.CaptureAsync(CreateCaptureRegion(zone), cancellationToken);
        }
        finally
        {
            overlayService.Show(overlaySnapshotToRestoreAfterCapture!);
        }
    }

    private async Task<TimeSpan> ShowOverlayAsync(OverlaySnapshot snapshot)
    {
        var measurement = await RunTimedStageAsync(
            TranslationPipelineStage.Overlay,
            () =>
            {
                overlayService.Show(snapshot);
                return Task.FromResult(true);
            });

        return measurement.Elapsed;
    }

    private PipelineOptimizationContext CreateOptimizationContext(
        GameProfile profile,
        OcrZone zone,
        CapturedFrame frame,
        TranslationPipelineRunOptions runOptions)
    {
        var stateKey = CreateStateKey(profile, zone);
        if (!optimizationOptions.IsEnabled || runOptions.RequireStableTextBeforeTranslation)
        {
            return new PipelineOptimizationContext(
                stateKey,
                PreviousState: null,
                ShouldReusePreviousResult: false,
                Debounced: false,
                FrameDifferenceRatio: null);
        }

        var previousState = GetOptimizationState(stateKey);
        if (previousState is null)
        {
            return new PipelineOptimizationContext(
                stateKey,
                PreviousState: null,
                ShouldReusePreviousResult: false,
                Debounced: false,
                FrameDifferenceRatio: null);
        }

        var frameDifferenceRatio = CalculateFrameDifferenceRatio(previousState.Fingerprint, frame);
        var shouldReusePreviousResult = frameDifferenceRatio <= optimizationOptions.FrameDifferenceThreshold;
        var debounced = shouldReusePreviousResult
            && IsWithinDebounceWindow(previousState.CapturedAt, frame.CapturedAt);

        return new PipelineOptimizationContext(
            stateKey,
            previousState,
            shouldReusePreviousResult,
            debounced,
            frameDifferenceRatio);
    }

    private PipelineFrameState? GetOptimizationState(PipelineFrameStateKey stateKey)
    {
        lock (optimizationStateLock)
        {
            return optimizationStates.TryGetValue(stateKey, out var state)
                ? state
                : null;
        }
    }

    private void StoreOptimizationState(
        PipelineFrameStateKey stateKey,
        CapturedFrame frame,
        TranslationPipelineResult result)
    {
        if (!optimizationOptions.IsEnabled)
        {
            return;
        }

        var state = new PipelineFrameState(
            FrameFingerprint.FromFrame(frame),
            result,
            frame.CapturedAt);

        lock (optimizationStateLock)
        {
            optimizationStates[stateKey] = state;
        }
    }

    private bool IsTextStableForTranslation(
        PipelineFrameStateKey stateKey,
        OcrResult sourceResult,
        TranslationPipelineRunOptions runOptions)
    {
        if (!runOptions.RequireStableTextBeforeTranslation)
        {
            return true;
        }

        var textSignature = CreateTextSignature(sourceResult);
        if (string.IsNullOrWhiteSpace(textSignature))
        {
            ClearTextStabilityState(stateKey);
            return false;
        }

        lock (textStabilityStateLock)
        {
            if (!textStabilityStates.TryGetValue(stateKey, out var state)
                || !string.Equals(state.TextSignature, textSignature, StringComparison.Ordinal))
            {
                textStabilityStates[stateKey] = new PipelineTextStabilityState(
                    textSignature,
                    sourceResult.RecognizedAt,
                    sourceResult.RecognizedAt);

                return runOptions.StableTextInterval == TimeSpan.Zero;
            }

            textStabilityStates[stateKey] = state with
            {
                LastSeenAt = sourceResult.RecognizedAt,
            };

            return CalculateElapsed(state.FirstSeenAt, sourceResult.RecognizedAt) >= runOptions.StableTextInterval;
        }
    }

    private void ClearTextStabilityState(PipelineFrameStateKey stateKey)
    {
        lock (textStabilityStateLock)
        {
            textStabilityStates.Remove(stateKey);
        }
    }

    private static TimeSpan CalculateElapsed(DateTimeOffset start, DateTimeOffset end)
    {
        return end >= start ? end - start : TimeSpan.Zero;
    }

    private static string CreateTextSignature(OcrResult sourceResult)
    {
        return OcrTextNormalizer.NormalizeForComparison(sourceResult.Text);
    }

    private bool IsWithinDebounceWindow(DateTimeOffset previousCapturedAt, DateTimeOffset currentCapturedAt)
    {
        var elapsed = currentCapturedAt >= previousCapturedAt
            ? currentCapturedAt - previousCapturedAt
            : previousCapturedAt - currentCapturedAt;

        return elapsed <= optimizationOptions.DebounceInterval;
    }

    private static double CalculateFrameDifferenceRatio(FrameFingerprint previous, CapturedFrame current)
    {
        if (previous.Width != current.Width
            || previous.Height != current.Height
            || previous.Stride != current.Stride
            || !string.Equals(previous.PixelFormat, current.PixelFormat, StringComparison.Ordinal))
        {
            return 1d;
        }

        var currentPixels = current.PixelData.Span;
        if (previous.PixelData.Length != currentPixels.Length || currentPixels.Length == 0)
        {
            return 1d;
        }

        long totalDifference = 0;
        for (var index = 0; index < currentPixels.Length; index++)
        {
            totalDifference += Math.Abs(previous.PixelData[index] - currentPixels[index]);
        }

        return totalDifference / (255d * currentPixels.Length);
    }

    private static TranslationPipelineResult CreateReusedResult(
        GameProfile profile,
        OcrZone zone,
        CapturedFrame frame,
        TranslationPipelineResult previousResult,
        TimeSpan captureElapsed,
        PipelineOptimizationContext optimizationContext)
    {
        var sourceResult = CreateReusedOcrResult(profile, zone, frame, previousResult.SourceOcrResult);
        return new TranslationPipelineResult(
            profile.Id,
            zone.Id,
            frame,
            sourceResult,
            previousResult.TranslateResponse,
            previousResult.OverlaySnapshot,
            cacheResult: null,
            CreateTimings(
                captureElapsed,
                ocrElapsed: TimeSpan.Zero,
                credentialsElapsed: TimeSpan.Zero,
                translationElapsed: TimeSpan.Zero,
                cacheElapsed: TimeSpan.Zero,
                overlayElapsed: TimeSpan.Zero,
                totalElapsed: captureElapsed),
            new TranslationPipelineOptimizationInfo(
                ocrSkipped: true,
                translationSkipped: previousResult.TranslateResponse is not null,
                debounced: optimizationContext.Debounced,
                frameDifferenceRatio: optimizationContext.FrameDifferenceRatio));
    }

    private static TranslationPipelineResult ReplaceResultTimings(
        TranslationPipelineResult result,
        TimeSpan captureElapsed,
        TimeSpan ocrElapsed,
        TimeSpan credentialsElapsed,
        TimeSpan translationElapsed,
        TimeSpan cacheElapsed,
        TimeSpan overlayElapsed,
        TimeSpan totalElapsed)
    {
        return new TranslationPipelineResult(
            result.ProfileId,
            result.ZoneId,
            result.CapturedFrame,
            result.SourceOcrResult,
            result.TranslateResponse,
            result.OverlaySnapshot,
            result.CacheResult,
            CreateTimings(
                captureElapsed,
                ocrElapsed,
                credentialsElapsed,
                translationElapsed,
                cacheElapsed,
                overlayElapsed,
                totalElapsed),
            result.Optimization);
    }

    private static OcrResult CreateReusedOcrResult(
        GameProfile profile,
        OcrZone zone,
        CapturedFrame frame,
        OcrResult previousResult)
    {
        var request = new OcrRequest(
            frame,
            ResolveOcrLanguage(profile, zone),
            zone.Id,
            profile.OcrPreprocessingSettings,
            profile.OcrSettings.Engine,
            profile.OcrSettings.OrientationMode,
            ResolveOcrLayoutMode(zone));

        return new OcrResult(
            request,
            previousResult.TextBlocks,
            previousResult.RecognizedAt,
            previousResult.TextBlockSources,
            previousResult.Words);
    }

    private static TranslationPipelineOptimizationInfo CreateProcessedOptimization(PipelineOptimizationContext optimizationContext)
    {
        return optimizationContext.FrameDifferenceRatio is null
            ? TranslationPipelineOptimizationInfo.None
            : new TranslationPipelineOptimizationInfo(
                ocrSkipped: false,
                translationSkipped: false,
                debounced: false,
                frameDifferenceRatio: optimizationContext.FrameDifferenceRatio);
    }

    private static PipelineFrameStateKey CreateStateKey(GameProfile profile, OcrZone zone)
    {
        return new PipelineFrameStateKey(
            profile.Id,
            profile.Name,
            zone.Id,
            zone.AbsoluteBounds,
            zone.TextStyle,
            ResolveOcrLanguage(profile, zone),
            zone.TranslationGroupingMode,
            zone.TextGrouping ?? OcrZoneTextGroupingSettings.Default,
            profile.TranslatorSettings,
            profile.OcrSettings,
            profile.OcrPreprocessingSettings,
            profile.OverlaySettings);
    }

    private static string ResolveOcrLanguage(GameProfile profile, OcrZone zone)
    {
        return zone.ResolveOcrLanguage(profile.TranslatorSettings.SourceLanguage);
    }

    private static GameProfile NormalizeTranslatorLanguageTags(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var sourceLanguage = NormalizeTranslatorLanguageTag(profile.TranslatorSettings.SourceLanguage);
        var targetLanguage = NormalizeTranslatorLanguageTag(profile.TranslatorSettings.TargetLanguage);
        if (string.Equals(sourceLanguage, profile.TranslatorSettings.SourceLanguage, StringComparison.Ordinal)
            && string.Equals(targetLanguage, profile.TranslatorSettings.TargetLanguage, StringComparison.Ordinal))
        {
            return profile;
        }

        return profile with
        {
            TranslatorSettings = profile.TranslatorSettings with
            {
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage,
            },
        };
    }

    private static string NormalizeTranslatorLanguageTag(string languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return languageTag;
        }

        return TesseractLanguageCatalog.TryMapTrainedDataCodeToPreferredLanguageTag(languageTag, out var preferredLanguageTag)
            ? preferredLanguageTag
            : languageTag.Trim();
    }

    private static OcrLayoutMode ResolveOcrLayoutMode(OcrZone zone)
    {
        return zone.TranslationGroupingMode switch
        {
            TranslationGroupingMode.BlockByBlock => OcrLayoutMode.Menu,
            TranslationGroupingMode.WholeZone => OcrLayoutMode.Dialog,
            TranslationGroupingMode.NearbyBlocks => OcrLayoutMode.Comic,
            _ => OcrLayoutMode.Auto,
        };
    }

    private static CaptureRegion CreateCaptureRegion(OcrZone zone)
    {
        return new CaptureRegion(
            zone.AbsoluteBounds.X,
            zone.AbsoluteBounds.Y,
            zone.AbsoluteBounds.Width,
            zone.AbsoluteBounds.Height);
    }

    private static OcrResult CreateTranslatedResult(OcrResult sourceResult, TranslateResponse translateResponse)
    {
        var translatedBlocks = sourceResult.TextBlocks
            .Zip(
                translateResponse.TranslatedTexts,
                (sourceBlock, translatedText) => new OcrTextBlock(translatedText, sourceBlock.Bounds))
            .ToArray();

        return new OcrResult(
            sourceResult.Request,
            translatedBlocks,
            translateResponse.TranslatedAt,
            sourceResult.TextBlockSources);
    }

    private static OverlaySnapshot CreateEmptySnapshot(DateTimeOffset shownAt, OverlaySettings overlaySettings)
    {
        return new OverlaySnapshot(
            Array.Empty<OverlayTextItem>(),
            shownAt,
            overlaySettings);
    }

    private static OverlaySnapshot CreateCombinedSnapshot(
        IReadOnlyList<TranslationPipelineResult> results,
        OverlaySnapshot? previousSnapshot,
        OverlaySettings overlaySettings,
        TranslationPipelineRunOptions runOptions)
    {
        if (previousSnapshot is not null
            && runOptions.PreservePreviousOverlayWhileWaitingForStableText
            && ShouldPreservePreviousOverlay(results))
        {
            return previousSnapshot;
        }

        var successfulSnapshots = results.Select(result => result.OverlaySnapshot).ToArray();
        var shownAt = successfulSnapshots.Length == 0
            ? DateTimeOffset.UtcNow
            : successfulSnapshots.Max(snapshot => snapshot.ShownAt);
        var settings = overlaySettings ?? previousSnapshot?.OverlaySettings ?? OverlaySettings.Default;

        return OverlayPositioningService.CombineCandidateSnapshots(
            successfulSnapshots,
            shownAt,
            settings);
    }

    private static TranslationPipelineResult[] CreateOrderedResults(
        IReadOnlyList<TranslationPipelineResult?> resultSlots)
    {
        return resultSlots
            .Where(result => result is not null)
            .Cast<TranslationPipelineResult>()
            .ToArray();
    }

    private static TranslationPipelineZoneFailure[] CreateOrderedFailures(
        IReadOnlyList<TranslationPipelineZoneFailure?> failureSlots)
    {
        return failureSlots
            .Where(failure => failure is not null)
            .Cast<TranslationPipelineZoneFailure>()
            .ToArray();
    }

    private static TranslationPipelineZoneFailure CreateZoneFailure(
        OcrZone zone,
        TranslationPipelineException exception)
    {
        return new TranslationPipelineZoneFailure(
            zone.Id,
            zone.Name,
            exception.Stage,
            exception.Message,
            exception,
            exception.CapturedFrame,
            exception.SourceOcrResult);
    }

    private static TranslationPipelineZoneFailure CreateCandidateDetectorUnavailableFailure(
        OcrZone zone,
        TextCandidateRegionDetectionResult detection)
    {
        var reason = string.IsNullOrWhiteSpace(detection.UnavailableReason)
            ? "The candidate-region detector is unavailable."
            : detection.UnavailableReason;
        var message = $"Candidate-region pipeline is degraded: {reason}";
        return CreateZoneFailure(
            zone,
            new TranslationPipelineException(
                TranslationPipelineStage.Ocr,
                message,
                new InvalidOperationException(reason)));
    }

    private static bool IsWaitingForStableText(IReadOnlyList<TranslationPipelineResult> results)
    {
        return results.Count > 0
            && results.Any(result => result.RecognizedBlockCount > 0)
            && results.All(result => result.TranslatedBlockCount == 0)
            && results.Any(result => result.Optimization.TranslationSkipped);
    }

    private static bool ShouldPreservePreviousOverlay(IReadOnlyList<TranslationPipelineResult> results)
    {
        return results.Count == 0
            || IsWaitingForStableText(results)
            || IsTemporarilyEmptyOcrResult(results);
    }

    private static bool IsTemporarilyEmptyOcrResult(IReadOnlyList<TranslationPipelineResult> results)
    {
        return results.Count > 0
            && results.All(result => result.RecognizedBlockCount == 0)
            && results.All(result => result.TranslatedBlockCount == 0);
    }

    private static TranslationPipelineTimings CreateTimings(
        TimeSpan captureElapsed,
        TimeSpan ocrElapsed,
        TimeSpan credentialsElapsed,
        TimeSpan translationElapsed,
        TimeSpan cacheElapsed,
        TimeSpan overlayElapsed,
        TimeSpan totalElapsed)
    {
        return new TranslationPipelineTimings(
            captureElapsed,
            ocrElapsed,
            credentialsElapsed,
            translationElapsed,
            cacheElapsed,
            overlayElapsed,
            totalElapsed);
    }

    private static async Task<(TValue Value, TimeSpan Elapsed)> RunTimedStageAsync<TValue>(
        TranslationPipelineStage stage,
        Func<Task<TValue>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var value = await action();
            stopwatch.Stop();
            return (value, stopwatch.Elapsed);
        }
        catch (TranslationPipelineException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TranslationPipelineException(
                stage,
                $"Translation pipeline failed during {stage}.",
                exception);
        }
    }

    private sealed record PipelineOptimizationContext(
        PipelineFrameStateKey StateKey,
        PipelineFrameState? PreviousState,
        bool ShouldReusePreviousResult,
        bool Debounced,
        double? FrameDifferenceRatio);

    private sealed record PipelineZoneTask(
        int Index,
        OcrZone Zone,
        Task<TranslationPipelineResult> Task);

    private sealed record PipelineFrameStateKey(
        string ProfileId,
        string ProfileName,
        string ZoneId,
        AbsoluteRectangle ZoneBounds,
        OcrZoneTextStyle TextStyle,
        string OcrLanguage,
        TranslationGroupingMode TranslationGroupingMode,
        OcrZoneTextGroupingSettings TextGrouping,
        TranslatorSettings TranslatorSettings,
        OcrSettings OcrSettings,
        OcrPreprocessingSettings PreprocessingSettings,
        OverlaySettings OverlaySettings);

    private sealed record PipelineFrameState(
        FrameFingerprint Fingerprint,
        TranslationPipelineResult Result,
        DateTimeOffset CapturedAt);

    private sealed record PipelineTextStabilityState(
        string TextSignature,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset LastSeenAt);

    public sealed class LiveTranslationSession : IDisposable
    {
        private readonly TranslationPipelineService service;
        private readonly GameProfile profile;
        private readonly TranslationPipelineRunOptions runOptions;
        private readonly CancellationTokenSource cancellationSource;
        private readonly Dictionary<string, LiveZoneState> zoneStates;
        private readonly SemaphoreSlim? candidateTranslationLimiter;
        private CandidatePipelineReadiness candidateReadiness = CandidatePipelineReadiness.Disabled;
        private Task<CandidatePrewarmResult>? candidatePrewarmTask;
        private int candidatePrewarmAttemptCount;
        private DateTimeOffset? nextCandidatePrewarmAt;
        private bool disposed;

        internal LiveTranslationSession(
            TranslationPipelineService service,
            GameProfile profile,
            TranslationPipelineRunOptions runOptions,
            CancellationToken cancellationToken)
        {
            this.service = service;
            this.profile = profile;
            this.runOptions = runOptions;
            cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            zoneStates = profile.OcrZones.ToDictionary(
                zone => zone.Id,
                zone => new LiveZoneState(zone),
                StringComparer.Ordinal);
            candidateTranslationLimiter = CreateCandidateTranslationLimiter(runOptions);
        }

        public async Task<LiveTranslationPipelineUpdate> RefreshAsync()
        {
            ThrowIfDisposed();
            var cancellationToken = cancellationSource.Token;
            cancellationToken.ThrowIfCancellationRequested();

            var cancelledZoneIds = new List<string>();
            var overlayChanged = await CollectCompletedWorkAsync();
            IReadOnlyList<LiveCapturedZone> capturedZones;
            try
            {
                capturedZones = await CaptureZonesAsync(cancellationToken);
            }
            catch (TranslationPipelineException exception)
                when (exception.Stage == TranslationPipelineStage.Capture
                    && runOptions.EnableCandidateDetectorPilot)
            {
                overlayChanged |= InvalidateCandidatesForCaptureLoss(
                    exception.Message,
                    cancelledZoneIds);
                var captureLossBatchResult = CreateBatchResult();
                if (overlayChanged)
                {
                    service.overlayService.Show(captureLossBatchResult.OverlaySnapshot);
                }

                return new LiveTranslationPipelineUpdate(
                    captureLossBatchResult,
                    overlayChanged,
                    cancelledZoneIds,
                    candidateReadiness);
            }

            if (RequiresCandidateReadinessBarrier()
                && !await AdvanceCandidateReadinessAsync(capturedZones, cancelledZoneIds, cancellationToken))
            {
                var pendingBatchResult = CreateBatchResult();
                if (overlayChanged)
                {
                    service.overlayService.Show(pendingBatchResult.OverlaySnapshot);
                }

                return new LiveTranslationPipelineUpdate(
                    pendingBatchResult,
                    overlayChanged,
                    cancelledZoneIds,
                    candidateReadiness);
            }

            foreach (var capturedZone in capturedZones)
            {
                overlayChanged |= await ReconcileCapturedZoneAsync(capturedZone, cancelledZoneIds, cancellationToken);
            }

            await Task.Yield();
            overlayChanged |= await CollectCompletedWorkAsync();
            var batchResult = CreateBatchResult();
            if (overlayChanged)
            {
                service.overlayService.Show(batchResult.OverlaySnapshot);
            }

            return new LiveTranslationPipelineUpdate(
                batchResult,
                overlayChanged,
                cancelledZoneIds,
                candidateReadiness);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cancellationSource.Cancel();
            foreach (var state in zoneStates.Values)
            {
                CancelActiveWork(state);
                foreach (var candidateState in state.CandidateStates.Values)
                {
                    CancelActiveWork(candidateState);
                }
            }

            cancellationSource.Dispose();
        }

        private bool RequiresCandidateReadinessBarrier()
        {
            return runOptions.EnableCandidateDetectorPilot
                && runOptions.RequireCandidateReadinessBarrier;
        }

        private async Task<bool> AdvanceCandidateReadinessAsync(
            IReadOnlyList<LiveCapturedZone> capturedZones,
            ICollection<string> cancelledZoneIds,
            CancellationToken cancellationToken)
        {
            if (candidateReadiness.IsReady)
            {
                return true;
            }

            if (candidatePrewarmTask is not null)
            {
                if (!candidatePrewarmTask.IsCompleted)
                {
                    return false;
                }

                CandidatePrewarmResult prewarmResult;
                try
                {
                    prewarmResult = await candidatePrewarmTask;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                finally
                {
                    candidatePrewarmTask = null;
                }

                if (!prewarmResult.Succeeded)
                {
                    MarkCandidateReadinessDegraded(
                        prewarmResult.UnavailableReason,
                        cancelledZoneIds);
                    return false;
                }

                candidateReadiness = new CandidatePipelineReadiness(
                    CandidatePipelineReadinessStatus.Ready,
                    checked(candidateReadiness.Generation + 1),
                    candidateReadiness.RestartCount,
                    unavailableReason: null,
                    nextRetryAt: null);
                candidatePrewarmAttemptCount = 0;
                nextCandidatePrewarmAt = null;

                // The frame used for prewarm is discarded. The next refresh starts live candidate work.
                return false;
            }

            if (candidatePrewarmAttemptCount >= runOptions.CandidatePrewarmMaximumAttempts)
            {
                return false;
            }

            if (nextCandidatePrewarmAt is { } retryAt && DateTimeOffset.UtcNow < retryAt)
            {
                return false;
            }

            var prewarmZone = capturedZones.FirstOrDefault();
            if (prewarmZone is null)
            {
                MarkCandidateReadinessDegraded(
                    "Candidate readiness requires a captured OCR zone.",
                    cancelledZoneIds);
                return false;
            }

            candidateReadiness = new CandidatePipelineReadiness(
                CandidatePipelineReadinessStatus.Prewarming,
                candidateReadiness.Generation,
                candidateReadiness.RestartCount,
                unavailableReason: null,
                nextRetryAt: null);
            nextCandidatePrewarmAt = null;
            candidatePrewarmAttemptCount++;
            candidatePrewarmTask = PrewarmCandidatePipelineAsync(prewarmZone, cancellationToken);
            return false;
        }

        private async Task<CandidatePrewarmResult> PrewarmCandidatePipelineAsync(
            LiveCapturedZone capturedZone,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(profile.TranslatorSettings.Provider, "GoogleWeb", StringComparison.OrdinalIgnoreCase))
            {
                return CandidatePrewarmResult.Unavailable(
                    "ADR-028 candidate readiness requires the direct GoogleWeb provider.");
            }

            try
            {
                var detection = await service.candidateRegionOcrService.DetectAsync(
                    CreateOcrRequest(profile, capturedZone.Zone, capturedZone.Frame),
                    cancellationToken);
                if (detection.Availability != TextCandidateDetectorAvailability.Available)
                {
                    return CandidatePrewarmResult.Unavailable(
                        detection.UnavailableReason ?? "Candidate detector prewarm was unavailable.");
                }

                var credentials = await service.credentialService.CreateCredentialsAsync(
                    profile.TranslatorSettings.Provider,
                    cancellationToken);
                var response = await service.translatorManager.TranslateAsync(
                    profile.TranslatorSettings,
                    ["テスト"],
                    credentials,
                    cancellationToken);
                return string.Equals(response.ProviderId, "GoogleWeb", StringComparison.Ordinal)
                    && response.TranslatedTexts.Count == 1
                    && !string.IsNullOrWhiteSpace(response.TranslatedTexts[0])
                    ? CandidatePrewarmResult.Success
                    : CandidatePrewarmResult.Unavailable(
                        "Direct GoogleWeb provider prewarm did not return one usable translation.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TranslatorProviderException exception)
            {
                var status = exception.StatusCode is { } statusCode
                    ? $"; HTTP {(int)statusCode}"
                    : string.Empty;
                return CandidatePrewarmResult.Unavailable(
                    $"Direct GoogleWeb provider prewarm was unavailable ({exception.ProviderId}; {exception.FailureKind}{status}).");
            }
            catch (Exception exception)
            {
                return CandidatePrewarmResult.Unavailable(
                    $"Direct GoogleWeb provider prewarm was unavailable ({exception.GetType().Name}).");
            }
        }

        private bool MarkCandidateReadinessDegraded(
            string? unavailableReason,
            ICollection<string> cancelledZoneIds)
        {
            // A readiness failure invalidates any outstanding prewarm result. It may finish in
            // the background, but its completion is no longer eligible to transition this session.
            candidatePrewarmTask = null;
            var overlayChanged = false;
            foreach (var state in zoneStates.Values)
            {
                foreach (var candidateState in state.CandidateStates.Values.ToArray())
                {
                    overlayChanged |= CancelAndRemoveCandidate(state, candidateState, cancelledZoneIds);
                }
            }

            candidateReadiness = new CandidatePipelineReadiness(
                CandidatePipelineReadinessStatus.Degraded,
                candidateReadiness.Generation,
                checked(candidateReadiness.RestartCount + 1),
                unavailableReason,
                nextRetryAt: ScheduleNextCandidatePrewarm());
            return overlayChanged;
        }

        private bool InvalidateCandidatesForCaptureLoss(
            string unavailableReason,
            ICollection<string> cancelledZoneIds)
        {
            if (RequiresCandidateReadinessBarrier())
            {
                return MarkCandidateReadinessDegraded(unavailableReason, cancelledZoneIds);
            }

            var overlayChanged = false;
            foreach (var state in zoneStates.Values)
            {
                foreach (var candidateState in state.CandidateStates.Values.ToArray())
                {
                    overlayChanged |= CancelAndRemoveCandidate(state, candidateState, cancelledZoneIds);
                }
            }

            return overlayChanged;
        }

        private DateTimeOffset? ScheduleNextCandidatePrewarm()
        {
            if (candidatePrewarmAttemptCount >= runOptions.CandidatePrewarmMaximumAttempts)
            {
                nextCandidatePrewarmAt = null;
                return null;
            }

            var exponent = Math.Max(candidatePrewarmAttemptCount - 1, 0);
            var delayMilliseconds = runOptions.CandidatePrewarmInitialRetryDelay.TotalMilliseconds
                * Math.Pow(2d, exponent);
            var delay = TimeSpan.FromMilliseconds(Math.Min(
                delayMilliseconds,
                TimeSpan.FromSeconds(30).TotalMilliseconds));
            nextCandidatePrewarmAt = DateTimeOffset.UtcNow.Add(delay);
            return nextCandidatePrewarmAt;
        }

        private async Task<IReadOnlyList<LiveCapturedZone>> CaptureZonesAsync(CancellationToken cancellationToken)
        {
            var snapshotToRestore = service.overlayService.CurrentSnapshot;
            var shouldRestoreOverlay = runOptions.RestorePreviousOverlayAfterCapture
                && snapshotToRestore is not null
                && service.overlayService.IsVisible
                && !service.overlayService.IsExcludedFromCapture;

            if (shouldRestoreOverlay)
            {
                service.overlayService.Hide();
            }

            try
            {
                var capturedZones = new List<LiveCapturedZone>(profile.OcrZones.Count);
                foreach (var zone in profile.OcrZones)
                {
                    var frameMeasurement = await RunTimedStageAsync(
                        TranslationPipelineStage.Capture,
                        () => service.captureService.CaptureAsync(CreateCaptureRegion(zone), cancellationToken));
                    capturedZones.Add(new LiveCapturedZone(zone, frameMeasurement.Value, frameMeasurement.Elapsed));
                }

                return capturedZones;
            }
            finally
            {
                if (shouldRestoreOverlay)
                {
                    service.overlayService.Show(snapshotToRestore!);
                }
            }
        }

        private async Task<bool> ReconcileCapturedZoneAsync(
            LiveCapturedZone capturedZone,
            ICollection<string> cancelledZoneIds,
            CancellationToken cancellationToken)
        {
            if (runOptions.EnableCandidateDetectorPilot)
            {
                return await ReconcileCandidateRegionsAsync(capturedZone, cancelledZoneIds, cancellationToken);
            }

            var state = zoneStates[capturedZone.Zone.Id];
            if (state.SourceIdentity is null || !state.SourceIdentity.Matches(capturedZone.Frame))
            {
                var hadPublishedResult = state.Result is not null || state.Failure is not null;
                if (state.ActiveWork is not null)
                {
                    cancelledZoneIds.Add(state.Zone.Id);
                    CancelActiveWork(state);
                }

                state.SourceIdentity = FrameFingerprint.FromFrame(capturedZone.Frame);
                state.Result = null;
                state.Failure = null;
                StartWork(state, capturedZone);
                return hadPublishedResult;
            }

            if (state.ActiveWork is null && ShouldProcessStableFrame(state))
            {
                StartWork(state, capturedZone);
            }

            return false;
        }

        private async Task<bool> ReconcileCandidateRegionsAsync(
            LiveCapturedZone capturedZone,
            ICollection<string> cancelledZoneIds,
            CancellationToken cancellationToken)
        {
            var state = zoneStates[capturedZone.Zone.Id];
            state.SourceIdentity = FrameFingerprint.FromFrame(capturedZone.Frame);
            var detection = await service.candidateRegionOcrService.DetectAsync(
                CreateOcrRequest(profile, state.Zone, capturedZone.Frame),
                cancellationToken);
            var overlayChanged = false;

            if (detection.Availability != TextCandidateDetectorAvailability.Available)
            {
                if (RequiresCandidateReadinessBarrier())
                {
                    return MarkCandidateReadinessDegraded(
                        detection.UnavailableReason,
                        cancelledZoneIds);
                }

                foreach (var candidateState in state.CandidateStates.Values.ToArray())
                {
                    overlayChanged |= CancelAndRemoveCandidate(
                        state,
                        candidateState,
                        cancelledZoneIds);
                }

                return overlayChanged;
            }

            var currentCandidateIds = new HashSet<string>(StringComparer.Ordinal);
            var orderedRegions = OrderCandidateRegions(detection.Regions);
            foreach (var region in orderedRegions)
            {
                var candidateId = CreateCandidateId(state.Zone, region.Candidate.Bounds);
                currentCandidateIds.Add(candidateId);

                if (!state.CandidateStates.TryGetValue(candidateId, out var candidateState))
                {
                    candidateState = new LiveCandidateState(
                        candidateId,
                        CreateCandidateZone(state.Zone, region.Candidate.Bounds),
                        region,
                        FrameFingerprint.FromFrame(region.Frame));
                    state.CandidateStates.Add(candidateId, candidateState);
                    StartCandidateWork(
                        candidateState,
                        capturedZone.CaptureElapsed,
                        CreateCandidateOverlayPlacementConstraints(state.Zone, orderedRegions, region));
                    continue;
                }

                if (!candidateState.SourceIdentity.Matches(region.Frame))
                {
                    var hadPublishedResult = candidateState.Result is not null || candidateState.Failure is not null;
                    if (candidateState.ActiveWork is not null)
                    {
                        cancelledZoneIds.Add(candidateState.Id);
                        CancelActiveWork(candidateState);
                    }

                    candidateState.Region = region;
                    candidateState.SourceIdentity = FrameFingerprint.FromFrame(region.Frame);
                    candidateState.Result = null;
                    candidateState.Failure = null;
                    StartCandidateWork(
                        candidateState,
                        capturedZone.CaptureElapsed,
                        CreateCandidateOverlayPlacementConstraints(state.Zone, orderedRegions, region));
                    overlayChanged |= hadPublishedResult;
                    continue;
                }

                if (candidateState.ActiveWork is null && ShouldProcessStableFrame(candidateState))
                {
                    StartCandidateWork(
                        candidateState,
                        capturedZone.CaptureElapsed,
                        CreateCandidateOverlayPlacementConstraints(state.Zone, orderedRegions, region));
                }
            }

            foreach (var candidateState in state.CandidateStates.Values
                         .Where(candidate => !currentCandidateIds.Contains(candidate.Id))
                         .ToArray())
            {
                overlayChanged |= CancelAndRemoveCandidate(state, candidateState, cancelledZoneIds);
            }

            return overlayChanged;
        }

        private static bool CancelAndRemoveCandidate(
            LiveZoneState sourceState,
            LiveCandidateState candidateState,
            ICollection<string> cancelledZoneIds)
        {
            var hadPublishedResult = candidateState.Result is not null || candidateState.Failure is not null;
            if (candidateState.ActiveWork is not null)
            {
                cancelledZoneIds.Add(candidateState.Id);
                CancelActiveWork(candidateState);
            }

            sourceState.CandidateStates.Remove(candidateState.Id);
            return hadPublishedResult;
        }

        private void StartCandidateWork(
            LiveCandidateState candidateState,
            TimeSpan captureElapsed,
            OverlayPlacementConstraints overlayPlacementConstraints)
        {
            var candidateCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationSource.Token);
            candidateState.ActiveWork = new LiveZoneWork(
                candidateCancellation,
                service.RunCapturedZoneAsync(
                    CreateCandidateProfile(candidateState.Zone),
                    candidateState.Zone,
                    candidateState.Region.Frame,
                    captureElapsed,
                    candidateState.Result?.OverlaySnapshot,
                    CreateCandidateRunOptions(),
                    candidateCancellation.Token,
                    overlayPlacementConstraints,
                    candidateTranslationLimiter));
        }

        private GameProfile CreateCandidateProfile(OcrZone candidateZone)
        {
            return CreateTransientCandidateProfile(profile, candidateZone);
        }

        private TranslationPipelineRunOptions CreateCandidateRunOptions()
        {
            return CreateCandidateRunOptionsWithoutDetector(runOptions);
        }

        private static IReadOnlyList<TextCandidateRegion> OrderCandidateRegions(
            IEnumerable<TextCandidateRegion> regions)
        {
            return TranslationPipelineService.OrderCandidateRegions(regions);
        }

        private static string CreateCandidateId(OcrZone sourceZone, BoundingBox bounds)
        {
            return CreateTransientCandidateId(sourceZone, bounds);
        }

        private static OcrZone CreateCandidateZone(OcrZone sourceZone, BoundingBox bounds)
        {
            return CreateTransientCandidateZone(sourceZone, bounds);
        }

        private void StartWork(LiveZoneState state, LiveCapturedZone capturedZone)
        {
            var zoneCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationSource.Token);
            state.ActiveWork = new LiveZoneWork(
                zoneCancellation,
                service.RunCapturedZoneAsync(
                    profile,
                    state.Zone,
                    capturedZone.Frame,
                    capturedZone.CaptureElapsed,
                    state.Result?.OverlaySnapshot,
                    runOptions,
                    zoneCancellation.Token));
        }

        private static bool ShouldProcessStableFrame(LiveZoneState state)
        {
            return state.Failure is null
                && (state.Result is null || state.Result.Optimization.TranslationSkipped);
        }

        private static bool ShouldProcessStableFrame(LiveCandidateState state)
        {
            return state.Failure is null
                && (state.Result is null || state.Result.Optimization.TranslationSkipped);
        }

        private async Task<bool> CollectCompletedWorkAsync()
        {
            var overlayChanged = false;
            foreach (var state in zoneStates.Values)
            {
                var work = state.ActiveWork;
                if (work is not null && work.Task.IsCompleted)
                {
                    state.ActiveWork = null;
                    try
                    {
                        state.Result = await work.Task;
                        state.Failure = null;
                        overlayChanged = true;
                    }
                    catch (OperationCanceledException) when (work.Cancellation.IsCancellationRequested)
                    {
                    }
                    catch (TranslationPipelineException exception)
                    {
                        state.Result = null;
                        state.Failure = CreateZoneFailure(state.Zone, exception);
                        overlayChanged = true;
                    }
                    finally
                    {
                        work.Cancellation.Dispose();
                    }
                }

                foreach (var candidateState in state.CandidateStates.Values.ToArray())
                {
                    var candidateWork = candidateState.ActiveWork;
                    if (candidateWork is null || !candidateWork.Task.IsCompleted)
                    {
                        continue;
                    }

                    candidateState.ActiveWork = null;
                    try
                    {
                        candidateState.Result = await candidateWork.Task;
                        candidateState.Failure = null;
                        overlayChanged = true;
                    }
                    catch (OperationCanceledException) when (candidateWork.Cancellation.IsCancellationRequested)
                    {
                    }
                    catch (TranslationPipelineException exception)
                    {
                        candidateState.Result = null;
                        candidateState.Failure = CreateZoneFailure(candidateState.Zone, exception);
                        overlayChanged = true;
                    }
                    finally
                    {
                        candidateWork.Cancellation.Dispose();
                    }
                }
            }

            return overlayChanged;
        }

        private TranslationPipelineBatchResult CreateBatchResult()
        {
            var results = GetPublishedCandidateOrZoneStates()
                .Select(state => state.Result)
                .Where(result => result is not null)
                .Cast<TranslationPipelineResult>()
                .ToArray();
            var failures = GetPublishedCandidateOrZoneStates()
                .Select(state => state.Failure)
                .Where(failure => failure is not null)
                .Cast<TranslationPipelineZoneFailure>()
                .ToArray();
            var overlaySnapshot = CreateCombinedSnapshot(
                results,
                previousSnapshot: null,
                overlaySettings: profile.OverlaySettings,
                runOptions: runOptions);

            return new TranslationPipelineBatchResult(
                profile.Id,
                results,
                failures,
                overlaySnapshot);
        }

        private IEnumerable<LivePublishedState> GetPublishedCandidateOrZoneStates()
        {
            foreach (var zone in profile.OcrZones)
            {
                var state = zoneStates[zone.Id];
                if (!runOptions.EnableCandidateDetectorPilot)
                {
                    yield return new LivePublishedState(state.Result, state.Failure);
                    continue;
                }

                foreach (var candidateState in state.CandidateStates.Values
                             .OrderBy(candidate => candidate.Region.Candidate.Bounds.Y)
                             .ThenBy(candidate => candidate.Region.Candidate.Bounds.X)
                             .ThenBy(candidate => candidate.Region.Candidate.Bounds.Width)
                             .ThenBy(candidate => candidate.Region.Candidate.Bounds.Height))
                {
                    yield return new LivePublishedState(candidateState.Result, candidateState.Failure);
                }
            }
        }

        private static void CancelActiveWork(LiveZoneState state)
        {
            var work = state.ActiveWork;
            if (work is null)
            {
                return;
            }

            state.ActiveWork = null;
            CancelWork(work);
        }

        private static void CancelActiveWork(LiveCandidateState state)
        {
            var work = state.ActiveWork;
            if (work is null)
            {
                return;
            }

            state.ActiveWork = null;
            CancelWork(work);
        }

        private static void CancelWork(LiveZoneWork work)
        {
            work.Cancellation.Cancel();
            _ = work.Task.ContinueWith(
                task =>
                {
                    _ = task.Exception;
                    work.Cancellation.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }

        private sealed class LiveZoneState
        {
            public LiveZoneState(OcrZone zone)
            {
                Zone = zone;
            }

            public OcrZone Zone { get; }

            public FrameFingerprint? SourceIdentity { get; set; }

            public TranslationPipelineResult? Result { get; set; }

            public TranslationPipelineZoneFailure? Failure { get; set; }

            public LiveZoneWork? ActiveWork { get; set; }

            public Dictionary<string, LiveCandidateState> CandidateStates { get; } = new(StringComparer.Ordinal);
        }

        private sealed class LiveCandidateState
        {
            public LiveCandidateState(
                string id,
                OcrZone zone,
                TextCandidateRegion region,
                FrameFingerprint sourceIdentity)
            {
                Id = id;
                Zone = zone;
                Region = region;
                SourceIdentity = sourceIdentity;
            }

            public string Id { get; }

            public OcrZone Zone { get; }

            public TextCandidateRegion Region { get; set; }

            public FrameFingerprint SourceIdentity { get; set; }

            public TranslationPipelineResult? Result { get; set; }

            public TranslationPipelineZoneFailure? Failure { get; set; }

            public LiveZoneWork? ActiveWork { get; set; }
        }

        private sealed record LiveZoneWork(
            CancellationTokenSource Cancellation,
            Task<TranslationPipelineResult> Task);

        private sealed record LiveCapturedZone(
            OcrZone Zone,
            CapturedFrame Frame,
            TimeSpan CaptureElapsed);

        private sealed record CandidatePrewarmResult(
            bool Succeeded,
            string? UnavailableReason)
        {
            public static CandidatePrewarmResult Success { get; } = new(true, null);

            public static CandidatePrewarmResult Unavailable(string reason) => new(false, reason);
        }

        private sealed record LivePublishedState(
            TranslationPipelineResult? Result,
            TranslationPipelineZoneFailure? Failure);
    }

    private sealed class FrameFingerprint
    {
        private FrameFingerprint(
            int width,
            int height,
            int stride,
            string pixelFormat,
            byte[] pixelData)
        {
            Width = width;
            Height = height;
            Stride = stride;
            PixelFormat = pixelFormat;
            PixelData = pixelData;
        }

        public int Width { get; }

        public int Height { get; }

        public int Stride { get; }

        public string PixelFormat { get; }

        public byte[] PixelData { get; }

        public static FrameFingerprint FromFrame(CapturedFrame frame)
        {
            return new FrameFingerprint(
                frame.Width,
                frame.Height,
                frame.Stride,
                frame.PixelFormat,
                frame.PixelData.ToArray());
        }

        public bool Matches(CapturedFrame frame)
        {
            return Width == frame.Width
                && Height == frame.Height
                && Stride == frame.Stride
                && string.Equals(PixelFormat, frame.PixelFormat, StringComparison.Ordinal)
                && frame.PixelData.Span.SequenceEqual(PixelData);
        }
    }
}
