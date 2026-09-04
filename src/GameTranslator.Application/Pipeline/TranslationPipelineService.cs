using System.Collections.Concurrent;
using System.Diagnostics;
using GameTranslator.Application.Cache;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Content;
using GameTranslator.Application.Credentials;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Translation;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Pipeline;

public sealed class TranslationPipelineService
{
    private static readonly TimeSpan CjkVerticalTypewriterGrowthAdditionalQuietInterval =
        TimeSpan.FromMilliseconds(300);
    private readonly CaptureService captureService;
    private readonly OcrService ocrService;
    private readonly TranslatorManager translatorManager;
    private readonly TranslatorCredentialService credentialService;
    private readonly TranslationCacheService cacheService;
    private readonly OverlayPositioningService overlayPositioningService;
    private readonly IOverlayService overlayService;
    private readonly TranslationPipelineOptimizationOptions optimizationOptions;
    private readonly TextCandidateRegionOcrService candidateRegionOcrService;
    private readonly TimeProvider timeProvider;
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
        TextCandidateRegionOcrService? candidateRegionOcrService = null,
        TimeProvider? timeProvider = null)
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
        this.timeProvider = timeProvider ?? TimeProvider.System;
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
                        candidateTranslationLimiter,
                        new CandidateRecognitionContext(region.Candidate, zone.AbsoluteBounds.Height))));
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
        SemaphoreSlim? candidateTranslationLimiter = null,
        CandidateRecognitionContext? candidateRecognitionContext = null,
        ProviderRequestDiagnosticsCollector? providerRequestDiagnosticsCollector = null)
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

        if (candidateRecognitionContext is { } candidateContext
            && !candidateRegionOcrService.IsRecognizedCandidateAccepted(
                request,
                candidateContext.Candidate,
                candidateContext.SourceZoneHeight,
                sourceResult))
        {
            sourceResult = new OcrResult(
                sourceResult.Request,
                Array.Empty<OcrTextBlock>(),
                sourceResult.RecognizedAt);
        }

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

        var currentTextSignature = CreateTextSignature(translationSourceResult);
        var typewriterGrowthGuardApplied = ShouldApplyCjkVerticalTypewriterGrowthGuard(
            translationSourceResult,
            candidateRecognitionContext,
            currentTextSignature);
        var requiredStableTextDuration = runOptions.StableTextInterval
            + (typewriterGrowthGuardApplied
                ? CjkVerticalTypewriterGrowthAdditionalQuietInterval
                : TimeSpan.Zero);
        var textStability = EvaluateTextStability(
            optimizationContext.StateKey,
            translationSourceResult,
            runOptions,
            requiredStableTextDuration,
            typewriterGrowthGuardApplied);
        if (!textStability.IsStable)
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
                    frameDifferenceRatio: optimizationContext.FrameDifferenceRatio),
                translationInputBlockCount: translationSourceResult.TextBlocks.Count,
                textStability: textStability);
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
                        var providerRequestDiagnostics = string.Equals(
                            profile.TranslatorSettings.Provider,
                            "BingWeb",
                            StringComparison.OrdinalIgnoreCase)
                                ? providerRequestDiagnosticsCollector?.Begin(
                                    missingTexts,
                                    timeProvider.GetUtcNow())
                            : null;
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
                                () => translatorManager.TranslateAsync(
                                    profile.TranslatorSettings,
                                    missingTexts,
                                    credentialsMeasurement.Value,
                                    cancellationToken,
                                    providerRequestDiagnostics));
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
                CreateProcessedOptimization(optimizationContext),
                translationInputBlockCount: translationSourceResult.TextBlocks.Count,
                textStability: textStability);
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
        SemaphoreSlim? candidateTranslationLimiter = null,
        CandidateRecognitionContext? candidateRecognitionContext = null,
        ProviderRequestDiagnosticsCollector? providerRequestDiagnosticsCollector = null)
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
            candidateTranslationLimiter: candidateTranslationLimiter,
            candidateRecognitionContext: candidateRecognitionContext,
            providerRequestDiagnosticsCollector: providerRequestDiagnosticsCollector);
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
            ResolveOcrLayoutMode(zone),
            zone.ContentLayoutMode,
            zone.CandidateGrouping)
        {
            DetectorPreset = zone.DetectorPreset,
        };
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
                LayoutMode = ContentLayoutPolicyResolver
                    .Resolve(sourceZone.ContentLayoutMode)
                    .CandidateOverlayLayout,
            },
            ContentLayoutMode = sourceZone.ContentLayoutMode,
            DetectorPreset = sourceZone.DetectorPreset,
            CandidateGrouping = sourceZone.CandidateGrouping ?? OcrCandidateGroupingSettings.Default,
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
            minimumCandidateOverlayVisibleDuration: sourceOptions.MinimumCandidateOverlayVisibleDuration,
            candidateTranslationMaxParallelism: sourceOptions.CandidateTranslationMaxParallelism,
            minimumCandidateGroupingObservations: sourceOptions.MinimumCandidateGroupingObservations,
            minimumStableTextObservations: sourceOptions.MinimumStableTextObservations)
        {
            MinimumCandidateGroupingDuration = sourceOptions.MinimumCandidateGroupingDuration,
        };
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

    private TranslationPipelineTextStability EvaluateTextStability(
        PipelineFrameStateKey stateKey,
        OcrResult sourceResult,
        TranslationPipelineRunOptions runOptions,
        TimeSpan requiredStableTextDuration,
        bool typewriterGrowthGuardApplied)
    {
        if (!runOptions.RequireStableTextBeforeTranslation)
        {
            return TranslationPipelineTextStability.NotRequired;
        }

        var textSignature = CreateTextSignature(sourceResult);
        if (string.IsNullOrWhiteSpace(textSignature))
        {
            ClearTextStabilityState(stateKey);
            return new TranslationPipelineTextStability(
                isRequired: true,
                isStable: false,
                firstObservedAt: null,
                lastObservedAt: null,
                observationCount: 0,
                requiredObservationCount: runOptions.MinimumStableTextObservations,
                requiredDuration: requiredStableTextDuration,
                typewriterGrowthGuardApplied: typewriterGrowthGuardApplied);
        }

        lock (textStabilityStateLock)
        {
            if (!textStabilityStates.TryGetValue(stateKey, out var state)
                || !string.Equals(state.TextSignature, textSignature, StringComparison.Ordinal))
            {
                textStabilityStates[stateKey] = new PipelineTextStabilityState(
                    textSignature,
                    sourceResult.RecognizedAt,
                    sourceResult.RecognizedAt,
                    ObservationCount: 1);

                return new TranslationPipelineTextStability(
                    isRequired: true,
                    isStable: requiredStableTextDuration == TimeSpan.Zero
                        && runOptions.MinimumStableTextObservations <= 1,
                    firstObservedAt: sourceResult.RecognizedAt,
                    lastObservedAt: sourceResult.RecognizedAt,
                    observationCount: 1,
                    requiredObservationCount: runOptions.MinimumStableTextObservations,
                    requiredDuration: requiredStableTextDuration,
                    typewriterGrowthGuardApplied: typewriterGrowthGuardApplied);
            }

            var observationCount = checked(state.ObservationCount + 1);
            textStabilityStates[stateKey] = state with
            {
                LastSeenAt = sourceResult.RecognizedAt,
                ObservationCount = observationCount,
            };

            return new TranslationPipelineTextStability(
                isRequired: true,
                isStable: observationCount >= runOptions.MinimumStableTextObservations
                    && CalculateElapsed(state.FirstSeenAt, sourceResult.RecognizedAt) >= requiredStableTextDuration,
                firstObservedAt: state.FirstSeenAt,
                lastObservedAt: sourceResult.RecognizedAt,
                observationCount: observationCount,
                requiredObservationCount: runOptions.MinimumStableTextObservations,
                requiredDuration: requiredStableTextDuration,
                typewriterGrowthGuardApplied: typewriterGrowthGuardApplied);
        }
    }

    private static bool ShouldApplyCjkVerticalTypewriterGrowthGuard(
        OcrResult sourceResult,
        CandidateRecognitionContext? candidateRecognitionContext,
        string currentTextSignature)
    {
        if (candidateRecognitionContext is not { } context
            || WritingSystemGroupingProfileResolver.Resolve(
                sourceResult.Request.Language,
                sourceResult.Request.OrientationMode) != WritingSystemGroupingProfile.CjkVertical)
        {
            return false;
        }

        return context.TypewriterGrowthGuardActive
            || IsMonotonicPrefixGrowth(context.PreviousTranslationInputSignature, currentTextSignature);
    }

    private static bool IsMonotonicPrefixGrowth(string? previousTextSignature, string currentTextSignature)
    {
        return !string.IsNullOrWhiteSpace(previousTextSignature)
            && currentTextSignature.Length > previousTextSignature.Length
            && currentTextSignature.StartsWith(previousTextSignature, StringComparison.Ordinal);
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
                frameDifferenceRatio: optimizationContext.FrameDifferenceRatio),
            translationInputBlockCount: previousResult.TranslationInputBlockCount,
            textStability: previousResult.TextStability);
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
            result.Optimization,
            translationInputBlockCount: result.TranslationInputBlockCount,
            textStability: result.TextStability);
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
            ResolveOcrLayoutMode(zone),
            zone.ContentLayoutMode,
            zone.CandidateGrouping)
        {
            DetectorPreset = zone.DetectorPreset,
        };

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
            zone.ContentLayoutMode,
            zone.CandidateGrouping ?? OcrCandidateGroupingSettings.Default,
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

        var normalizedLanguageTag = languageTag.Trim();
        if (string.Equals(normalizedLanguageTag, "zh", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-CN";
        }

        return TesseractLanguageCatalog.TryMapTrainedDataCodeToPreferredLanguageTag(normalizedLanguageTag, out var preferredLanguageTag)
            ? preferredLanguageTag
            : normalizedLanguageTag;
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
        var message = $"Candidate-region detector is unavailable: {reason}";
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

    private readonly record struct CandidateRecognitionContext(
        TextCandidate Candidate,
        int SourceZoneHeight,
        string? PreviousTranslationInputSignature = null,
        bool TypewriterGrowthGuardActive = false);

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
        ContentLayoutMode ContentLayoutMode,
        OcrCandidateGroupingSettings CandidateGrouping,
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
        DateTimeOffset LastSeenAt,
        int ObservationCount);

    private sealed class ProviderRequestDiagnosticsCollector
    {
        private readonly object syncRoot = new();
        private readonly List<TranslationProviderRequestDiagnostics> requests = new();

        public TranslationProviderRequestDiagnostics Begin(
            IEnumerable<string> inputTexts,
            DateTimeOffset queuedAt)
        {
            var request = new TranslationProviderRequestDiagnostics(inputTexts, queuedAt);
            lock (syncRoot)
            {
                requests.Add(request);
            }

            return request;
        }

        public IReadOnlyList<TranslationProviderRequestDiagnosticsSnapshot> CreateSnapshots()
        {
            lock (syncRoot)
            {
                return requests.Select(request => request.CreateSnapshot()).ToArray();
            }
        }

        public void MarkPendingRequestsCancelled(DateTimeOffset completedAt)
        {
            lock (syncRoot)
            {
                foreach (var request in requests.Where(request =>
                             request.CreateSnapshot().Outcome == TranslationProviderInvocationOutcome.Pending))
                {
                    request.MarkProviderInvocationCompleted(
                        TranslationProviderInvocationOutcome.Cancelled,
                        completedAt);
                }
            }
        }
    }

    public sealed class LiveTranslationSession : IDisposable
    {
        // The trace remains in memory only for the duration of a live session. The automatic
        // report writer independently enforces its 99 MB UTF-8 file limit.
        private const int MaximumCandidateLifecycleEvents = 131_072;
        private const int MaximumCandidateGeometryJitterPixels = 4;
        private const double MinimumCandidateGeometryJitterIntersectionOverUnion = 0.95d;

        private readonly TranslationPipelineService service;
        private readonly GameProfile profile;
        private readonly TranslationPipelineRunOptions runOptions;
        private readonly CancellationTokenSource cancellationSource;
        private readonly Dictionary<string, LiveZoneState> zoneStates;
        private readonly SemaphoreSlim? candidateTranslationLimiter;
        private readonly SemaphoreSlim stateAuthority = new(initialCount: 1, maxCount: 1);
        private readonly object workCompletionSignalSyncRoot = new();
        private readonly Queue<LiveCandidateLifecycleEvent> candidateLifecycleEvents = new();
        private readonly ConcurrentQueue<RetiredCandidateProviderDiagnostics> retiredCandidateProviderDiagnostics = new();
        private List<LiveCandidateLifecycleEvent> candidateLifecycleEventsSinceLastUpdate = new();
        private TaskCompletionSource<bool> workCompletionSignal = CreateWorkCompletionSignal();
        private CandidatePipelineReadiness candidateReadiness = CandidatePipelineReadiness.Disabled;
        private int droppedCandidateLifecycleEventCount;
        private long candidateLifecycleEventSequence;
        private long refreshSequence;
        private bool hasDeferredOverlayPublication;
        private TransientEmptyOverlayRetention? transientEmptyOverlayRetention;
        private bool forceImmediateEmptyOverlayPublication;
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
            candidateReadiness = runOptions.EnableCandidateDetectorPilot
                ? CandidatePipelineReadiness.Active
                : CandidatePipelineReadiness.Disabled;
        }

        public async Task<LiveTranslationPipelineUpdate> RefreshAsync()
        {
            ThrowIfDisposed();
            var cancellationToken = cancellationSource.Token;
            cancellationToken.ThrowIfCancellationRequested();
            await stateAuthority.WaitAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                return await RefreshCoreAsync(cancellationToken);
            }
            finally
            {
                stateAuthority.Release();
            }
        }

        public async Task WaitForWorkCompletionAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            Task signalTask;
            lock (workCompletionSignalSyncRoot)
            {
                signalTask = workCompletionSignal.Task;
            }

            if (!cancellationToken.CanBeCanceled)
            {
                await signalTask.WaitAsync(cancellationSource.Token);
                return;
            }

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationSource.Token,
                cancellationToken);
            await signalTask.WaitAsync(linkedCancellation.Token);
        }

        public async Task<LiveTranslationPipelineUpdate> PublishCompletedWorkAsync()
        {
            ThrowIfDisposed();
            var cancellationToken = cancellationSource.Token;
            cancellationToken.ThrowIfCancellationRequested();
            await stateAuthority.WaitAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                ResetWorkCompletionSignalForCollection();
                var overlayChanged = await CollectCompletedWorkAsync();
                var batchResult = CreateBatchResult();
                overlayChanged = PublishOverlaySnapshotIfReady(batchResult, overlayChanged);

                return CreateLiveUpdate(
                    batchResult,
                    overlayChanged,
                    Array.Empty<string>());
            }
            finally
            {
                stateAuthority.Release();
            }
        }

        private async Task<LiveTranslationPipelineUpdate> RefreshCoreAsync(CancellationToken cancellationToken)
        {
            refreshSequence = checked(refreshSequence + 1);

            var cancelledZoneIds = new List<string>();
            ResetWorkCompletionSignalForCollection();
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
                overlayChanged = PublishOverlaySnapshotIfReady(captureLossBatchResult, overlayChanged);

                return CreateLiveUpdate(
                    captureLossBatchResult,
                    overlayChanged,
                    cancelledZoneIds);
            }

            foreach (var capturedZone in capturedZones)
            {
                overlayChanged |= await ReconcileCapturedZoneAsync(
                    capturedZone,
                    cancelledZoneIds,
                    cancellationToken);
            }

            await Task.Yield();
            ResetWorkCompletionSignalForCollection();
            overlayChanged |= await CollectCompletedWorkAsync();
            var batchResult = CreateBatchResult();
            overlayChanged = PublishOverlaySnapshotIfReady(batchResult, overlayChanged);

            return CreateLiveUpdate(
                batchResult,
                overlayChanged,
                cancelledZoneIds);
        }

        private LiveTranslationPipelineUpdate CreateLiveUpdate(
            TranslationPipelineBatchResult batchResult,
            bool overlayChanged,
            IEnumerable<string> cancelledZoneIds)
        {
            var lifecycleEventsSinceLastUpdate = candidateLifecycleEventsSinceLastUpdate;
            candidateLifecycleEventsSinceLastUpdate = new List<LiveCandidateLifecycleEvent>();
            return new LiveTranslationPipelineUpdate(
                batchResult,
                overlayChanged,
                cancelledZoneIds,
                candidateReadiness,
                lifecycleEventsSinceLastUpdate,
                droppedCandidateLifecycleEventCount);
        }

        private bool PublishOverlaySnapshotIfReady(
            TranslationPipelineBatchResult batchResult,
            bool overlayChanged)
        {
            if (!overlayChanged && !hasDeferredOverlayPublication)
            {
                return false;
            }

            if (ShouldDeferTransientEmptyOverlay(batchResult.OverlaySnapshot))
            {
                hasDeferredOverlayPublication = true;
                return false;
            }

            hasDeferredOverlayPublication = false;
            service.overlayService.Show(batchResult.OverlaySnapshot);
            if (batchResult.OverlaySnapshot.TextItems.Count > 0)
            {
                MarkCandidateResultsPublished(service.timeProvider.GetUtcNow());
            }

            ClearTransientEmptyOverlayRetention();
            forceImmediateEmptyOverlayPublication = false;
            RecordOverlaySnapshotPublished(batchResult.OverlaySnapshot);
            return true;
        }

        private bool ShouldDeferTransientEmptyOverlay(OverlaySnapshot snapshot)
        {
            if (!runOptions.EnableCandidateDetectorPilot
                || snapshot.TextItems.Count > 0
                || service.overlayService.CurrentSnapshot?.TextItems.Count is not > 0)
            {
                return false;
            }

            if (forceImmediateEmptyOverlayPublication)
            {
                return false;
            }

            if (transientEmptyOverlayRetention is { } retention)
            {
                if (service.timeProvider.GetUtcNow() < retention.RetainUntil)
                {
                    return true;
                }

                ClearTransientEmptyOverlayRetention();
                return false;
            }

            return zoneStates.Values
                .SelectMany(state => state.CandidateStates.Values)
                .Any(candidateState =>
                    candidateState.Failure is null
                        ? candidateState.Result is null
                            || candidateState.Result.Optimization.TranslationSkipped
                        : IsRetainableBingWebFailure(candidateState.Failure));
        }

        private void MarkCandidateResultsPublished(DateTimeOffset publishedAt)
        {
            foreach (var candidateState in zoneStates.Values
                         .SelectMany(state => state.CandidateStates.Values)
                         .Where(candidateState => candidateState.ResultPublishedAt is null
                             && candidateState.Result?.OverlaySnapshot.TextItems.Count > 0))
            {
                candidateState.ResultPublishedAt = publishedAt;
            }
        }

        private void RegisterTransientEmptyOverlayRetention(LiveCandidateState candidateState)
        {
            if (runOptions.MinimumCandidateOverlayVisibleDuration <= TimeSpan.Zero
                || candidateState.ResultPublishedAt is not { } publishedAt)
            {
                return;
            }

            var retainUntil = publishedAt + runOptions.MinimumCandidateOverlayVisibleDuration;
            if (retainUntil <= service.timeProvider.GetUtcNow())
            {
                return;
            }

            transientEmptyOverlayRetention ??= new TransientEmptyOverlayRetention(retainUntil);
            if (retainUntil > transientEmptyOverlayRetention.RetainUntil)
            {
                transientEmptyOverlayRetention.RetainUntil = retainUntil;
            }

            transientEmptyOverlayRetention.RetainedCandidates[candidateState.Id] =
                new RetainedCandidateSource(candidateState.SourceZoneId, candidateState.SourceIdentity);
        }

        private void ValidateTransientEmptyOverlayRetention(
            LiveZoneState sourceState,
            IReadOnlyList<TextCandidateRegion> regions)
        {
            if (transientEmptyOverlayRetention is not { } retention
                || regions.Count == 0)
            {
                return;
            }

            var retainedForZone = retention.RetainedCandidates
                .Where(entry => string.Equals(
                    entry.Value.SourceZoneId,
                    sourceState.Zone.Id,
                    StringComparison.Ordinal))
                .ToArray();
            if (retainedForZone.Length == 0)
            {
                return;
            }

            var regionsById = regions.ToDictionary(
                region => CreateCandidateId(sourceState.Zone, region.Candidate.Bounds),
                StringComparer.Ordinal);
            var allSourcesStillMatch = retainedForZone.All(entry =>
                regionsById.TryGetValue(entry.Key, out var region)
                    && entry.Value.SourceIdentity.Matches(region.Frame));
            if (!allSourcesStillMatch)
            {
                ClearTransientEmptyOverlayRetention();
                forceImmediateEmptyOverlayPublication = true;
            }
        }

        private void InvalidateTransientEmptyOverlayRetention()
        {
            forceImmediateEmptyOverlayPublication |= transientEmptyOverlayRetention is not null;
            ClearTransientEmptyOverlayRetention();
        }

        private void ClearTransientEmptyOverlayRetention()
        {
            transientEmptyOverlayRetention = null;
        }

        private static bool IsRetainableBingWebFailure(TranslationPipelineZoneFailure failure)
        {
            if (failure.Stage != TranslationPipelineStage.Translation)
            {
                return false;
            }

            for (var exception = failure.Exception; exception is not null; exception = exception.InnerException!)
            {
                if (exception is TranslatorProviderException providerException)
                {
                    return string.Equals(providerException.ProviderId, "BingWeb", StringComparison.OrdinalIgnoreCase)
                        && providerException.FailureKind is
                            TranslatorProviderFailureKind.Timeout or TranslatorProviderFailureKind.Throttled;
                }
            }

            return false;
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

        private bool InvalidateCandidatesForCaptureLoss(
            string unavailableReason,
            ICollection<string> cancelledZoneIds)
        {
            InvalidateTransientEmptyOverlayRetention();
            var overlayChanged = false;
            foreach (var state in zoneStates.Values)
            {
                foreach (var candidateState in state.CandidateStates.Values.ToArray())
                {
                    overlayChanged |= CancelAndRemoveCandidate(
                        state,
                        candidateState,
                        cancelledZoneIds,
                        LiveCandidateCancellationReason.CaptureLost);
                }
            }

            return overlayChanged;
        }

        private async Task<IReadOnlyList<LiveCapturedZone>> CaptureZonesAsync(CancellationToken cancellationToken)
        {
            var refreshAt = DateTimeOffset.UtcNow;
            var zonesToCapture = profile.OcrZones
                .Where(zone => IsZoneRefreshDue(zoneStates[zone.Id], refreshAt))
                .ToArray();
            if (zonesToCapture.Length == 0)
            {
                return Array.Empty<LiveCapturedZone>();
            }

            var snapshotToRestore = service.overlayService.CurrentSnapshot;
            var shouldRestoreOverlay = runOptions.RestorePreviousOverlayAfterCapture
                && snapshotToRestore is not null
                && service.overlayService.IsVisible
                && !service.overlayService.IsExcludedFromCapture;

            if (shouldRestoreOverlay)
            {
                RecordLifecycleEvent(
                    LiveCandidateLifecycleEventKind.PreviousOverlayHiddenForCapture,
                    overlayTextItemCount: snapshotToRestore!.TextItems.Count,
                    overlayMaskItemCount: snapshotToRestore.MaskItems.Count);
                service.overlayService.Hide();
            }

            try
            {
                var capturedZones = new List<LiveCapturedZone>(zonesToCapture.Length);
                foreach (var zone in zonesToCapture)
                {
                    RecordLifecycleEvent(
                        LiveCandidateLifecycleEventKind.CaptureStarted,
                        zoneId: zone.Id);
                    var frameMeasurement = await RunTimedStageAsync(
                        TranslationPipelineStage.Capture,
                        () => service.captureService.CaptureAsync(CreateCaptureRegion(zone), cancellationToken));
                    zoneStates[zone.Id].LastRefreshAt = refreshAt;
                    RecordLifecycleEvent(
                        LiveCandidateLifecycleEventKind.CaptureCompleted,
                        zoneId: zone.Id,
                        frameCapturedAt: frameMeasurement.Value.CapturedAt,
                        elapsed: frameMeasurement.Elapsed);
                    capturedZones.Add(new LiveCapturedZone(zone, frameMeasurement.Value, frameMeasurement.Elapsed));
                }

                return capturedZones;
            }
            finally
            {
                if (shouldRestoreOverlay)
                {
                    service.overlayService.Show(snapshotToRestore!);
                    RecordLifecycleEvent(
                        LiveCandidateLifecycleEventKind.PreviousOverlayRestoredAfterCapture,
                        overlayTextItemCount: snapshotToRestore!.TextItems.Count,
                        overlayMaskItemCount: snapshotToRestore.MaskItems.Count);
                }
            }
        }

        private bool IsZoneRefreshDue(LiveZoneState state, DateTimeOffset now)
        {
            return ContentLayoutPolicyResolver
                .Resolve(state.Zone.ContentLayoutMode)
                .IsLiveRefreshDue(state.LastRefreshAt, now);
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
            RecordLifecycleEvent(
                LiveCandidateLifecycleEventKind.CandidateDetectionStarted,
                zoneId: state.Zone.Id,
                frameCapturedAt: capturedZone.Frame.CapturedAt);
            var detectionStopwatch = Stopwatch.StartNew();
            var detection = await service.candidateRegionOcrService.DetectAsync(
                CreateOcrRequest(profile, state.Zone, capturedZone.Frame),
                cancellationToken);
            detectionStopwatch.Stop();
            RecordLifecycleEvent(
                LiveCandidateLifecycleEventKind.CandidateDetectionCompleted,
                zoneId: state.Zone.Id,
                frameCapturedAt: capturedZone.Frame.CapturedAt,
                elapsed: detectionStopwatch.Elapsed,
                candidateCount: detection.Regions.Count,
                requestedDetectorPreset: detection.Diagnostics?.RequestedPreset,
                effectiveDetectorPreset: detection.Diagnostics?.EffectivePreset,
                detectorThreshold: detection.Diagnostics?.Threshold,
                detectorBoxThreshold: detection.Diagnostics?.BoxThreshold,
                detectorUnclipRatio: detection.Diagnostics?.UnclipRatio,
                rawDetectorCandidateCount: detection.Diagnostics?.RawCandidateCount,
                minimumDetectorConfidence: detection.Diagnostics?.MinimumConfidence,
                maximumDetectorConfidence: detection.Diagnostics?.MaximumConfidence,
                averageDetectorConfidence: detection.Diagnostics?.AverageConfidence);
            var overlayChanged = false;

            if (detection.Availability != TextCandidateDetectorAvailability.Available)
            {
                InvalidateTransientEmptyOverlayRetention();
                foreach (var candidateState in state.CandidateStates.Values.ToArray())
                {
                    overlayChanged |= CancelAndRemoveCandidate(
                        state,
                        candidateState,
                        cancelledZoneIds,
                        LiveCandidateCancellationReason.CandidatePipelineDegraded);
                }

                return overlayChanged;
            }

            var matchedCandidateIds = new HashSet<string>(StringComparer.Ordinal);
            var orderedRegions = OrderCandidateRegions(detection.Regions);
            ValidateTransientEmptyOverlayRetention(state, orderedRegions);
            foreach (var region in orderedRegions)
            {
                var candidateId = CreateCandidateId(state.Zone, region.Candidate.Bounds);
                var candidateState = FindMatchingCandidateState(
                    state,
                    region,
                    candidateId,
                    matchedCandidateIds,
                    out var matchedGeometryJitter);

                if (candidateState is null)
                {
                    candidateState = new LiveCandidateState(
                        candidateId,
                        state.Zone.Id,
                        CreateCandidateZone(state.Zone, region.Candidate.Bounds),
                        region,
                        FrameFingerprint.FromFrame(region.Frame),
                        CreateCandidateGeometrySignature(region.Candidate),
                        capturedZone.Frame.CapturedAt);
                    state.CandidateStates.Add(candidateId, candidateState);
                    matchedCandidateIds.Add(candidateId);
                    RecordCandidateLifecycleEvent(
                        LiveCandidateLifecycleEventKind.CandidateDiscovered,
                        candidateState,
                        frameCapturedAt: capturedZone.Frame.CapturedAt);
                    RecordCandidateGroupingAwaitingConfirmationIfNeeded(
                        candidateState,
                        capturedZone.Frame.CapturedAt);
                    if (IsCandidateGroupingConfirmed(candidateState))
                    {
                        StartCandidateWork(
                            candidateState,
                            capturedZone.CaptureElapsed,
                            CreateCandidateOverlayPlacementConstraints(state.Zone, orderedRegions, region));
                    }

                    continue;
                }

                matchedCandidateIds.Add(candidateState.Id);

                var geometrySignature = CreateCandidateGeometrySignature(region.Candidate);
                var geometryChanged = !string.Equals(
                    candidateState.GeometrySignature,
                    geometrySignature,
                    StringComparison.Ordinal);
                var sourceChanged = !candidateState.SourceIdentity.Matches(region.Frame);
                if (geometryChanged
                    && matchedGeometryJitter
                    && HasBoundedCandidateMemberGeometryJitter(candidateState.Region, region))
                {
                    candidateState.Region = region;
                    candidateState.GeometrySignature = geometrySignature;
                    ObserveMatchingCandidateGrouping(
                        candidateState,
                        capturedZone.Frame.CapturedAt);
                    RecordCandidateLifecycleEvent(
                        LiveCandidateLifecycleEventKind.CandidateGeometryJitterMatched,
                        candidateState,
                        frameCapturedAt: capturedZone.Frame.CapturedAt);
                }
                else if (geometryChanged)
                {
                    var hadPublishedResult = candidateState.Result is not null || candidateState.Failure is not null;
                    if (candidateState.ActiveWork is not null)
                    {
                        cancelledZoneIds.Add(candidateState.Id);
                        RecordCandidateLifecycleEvent(
                            LiveCandidateLifecycleEventKind.CandidateWorkCancelled,
                            candidateState,
                            frameCapturedAt: capturedZone.Frame.CapturedAt,
                            cancellationReason: LiveCandidateCancellationReason.CandidateGroupingChanged);
                        CancelActiveWork(candidateState);
                    }

                    candidateState.Region = region;
                    candidateState.GeometrySignature = geometrySignature;
                    ResetCandidateGroupingObservation(
                        candidateState,
                        capturedZone.Frame.CapturedAt);
                    candidateState.SourceIdentity = FrameFingerprint.FromFrame(region.Frame);
                    candidateState.Result = null;
                    candidateState.ResultPublishedAt = null;
                    candidateState.Failure = null;
                    candidateState.Revision = checked(candidateState.Revision + 1);
                    ClearCandidateTextStability(candidateState);
                    RecordCandidateLifecycleEvent(
                        LiveCandidateLifecycleEventKind.CandidateGroupingChanged,
                        candidateState,
                        frameCapturedAt: capturedZone.Frame.CapturedAt);

                    if (sourceChanged)
                    {
                        RecordCandidateLifecycleEvent(
                            LiveCandidateLifecycleEventKind.CandidateSourceChanged,
                            candidateState,
                            frameCapturedAt: capturedZone.Frame.CapturedAt);
                    }

                    RecordCandidateGroupingAwaitingConfirmationIfNeeded(
                        candidateState,
                        capturedZone.Frame.CapturedAt);
                    if (IsCandidateGroupingConfirmed(candidateState))
                    {
                        StartCandidateWork(
                            candidateState,
                            capturedZone.CaptureElapsed,
                            CreateCandidateOverlayPlacementConstraints(state.Zone, orderedRegions, region));
                    }

                    overlayChanged |= hadPublishedResult;
                    continue;
                }
                else
                {
                    ObserveMatchingCandidateGrouping(
                        candidateState,
                        capturedZone.Frame.CapturedAt);
                }

                if (sourceChanged)
                {
                    var hadPublishedResult = candidateState.Result is not null || candidateState.Failure is not null;
                    if (candidateState.ActiveWork is not null)
                    {
                        cancelledZoneIds.Add(candidateState.Id);
                        RecordCandidateLifecycleEvent(
                            LiveCandidateLifecycleEventKind.CandidateWorkCancelled,
                            candidateState,
                            frameCapturedAt: capturedZone.Frame.CapturedAt,
                            cancellationReason: LiveCandidateCancellationReason.CandidateSourceChanged);
                        CancelActiveWork(candidateState);
                    }

                    candidateState.Region = region;
                    candidateState.SourceIdentity = FrameFingerprint.FromFrame(region.Frame);
                    candidateState.Result = null;
                    candidateState.ResultPublishedAt = null;
                    candidateState.Failure = null;
                    candidateState.Revision = checked(candidateState.Revision + 1);
                    ClearCandidateTextStability(candidateState);
                    RecordCandidateLifecycleEvent(
                        LiveCandidateLifecycleEventKind.CandidateSourceChanged,
                        candidateState,
                        frameCapturedAt: capturedZone.Frame.CapturedAt);
                    RecordCandidateGroupingAwaitingConfirmationIfNeeded(
                        candidateState,
                        capturedZone.Frame.CapturedAt);
                    if (IsCandidateGroupingConfirmed(candidateState))
                    {
                        StartCandidateWork(
                            candidateState,
                            capturedZone.CaptureElapsed,
                            CreateCandidateOverlayPlacementConstraints(state.Zone, orderedRegions, region));
                    }

                    overlayChanged |= hadPublishedResult;
                    continue;
                }

                if (candidateState.ActiveWork is null
                    && ShouldProcessStableFrame(candidateState)
                    && IsCandidateGroupingConfirmed(candidateState))
                {
                    StartCandidateWork(
                        candidateState,
                        capturedZone.CaptureElapsed,
                        CreateCandidateOverlayPlacementConstraints(state.Zone, orderedRegions, region));
                }
                else
                {
                    RecordCandidateGroupingAwaitingConfirmationIfNeeded(
                        candidateState,
                        capturedZone.Frame.CapturedAt);
                }
            }

            foreach (var candidateState in state.CandidateStates.Values
                         .Where(candidate => !matchedCandidateIds.Contains(candidate.Id))
                         .ToArray())
            {
                overlayChanged |= CancelAndRemoveCandidate(
                    state,
                    candidateState,
                    cancelledZoneIds,
                    LiveCandidateCancellationReason.CandidateDisappeared,
                    allowTransientOverlayRetention: orderedRegions.Count == 0);
            }

            return overlayChanged;
        }

        private static LiveCandidateState? FindMatchingCandidateState(
            LiveZoneState sourceState,
            TextCandidateRegion region,
            string exactCandidateId,
            ISet<string> matchedCandidateIds,
            out bool matchedGeometryJitter)
        {
            ArgumentNullException.ThrowIfNull(sourceState);
            ArgumentNullException.ThrowIfNull(region);
            ArgumentException.ThrowIfNullOrWhiteSpace(exactCandidateId);
            ArgumentNullException.ThrowIfNull(matchedCandidateIds);

            matchedGeometryJitter = false;
            if (sourceState.CandidateStates.TryGetValue(exactCandidateId, out var exactCandidate)
                && !matchedCandidateIds.Contains(exactCandidate.Id))
            {
                return exactCandidate;
            }

            var matchedCandidate = sourceState.CandidateStates.Values
                .Where(candidate => !matchedCandidateIds.Contains(candidate.Id))
                .Where(candidate => HasCompatibleCandidateMemberCount(candidate.Region, region))
                .Select(candidate => new
                {
                    Candidate = candidate,
                    IntersectionOverUnion = CalculateIntersectionOverUnion(
                        candidate.AnchorBounds,
                        region.Candidate.Bounds),
                })
                .Where(match => match.IntersectionOverUnion >= MinimumCandidateGeometryJitterIntersectionOverUnion)
                .Where(match => HasBoundedCandidateGeometryJitter(
                    match.Candidate.AnchorBounds,
                    region.Candidate.Bounds))
                .OrderByDescending(match => match.IntersectionOverUnion)
                .ThenBy(match => match.Candidate.Id, StringComparer.Ordinal)
                .Select(match => match.Candidate)
                .FirstOrDefault();
            if (matchedCandidate is not null)
            {
                matchedGeometryJitter = true;
            }

            return matchedCandidate;
        }

        private static bool HasCompatibleCandidateMemberCount(
            TextCandidateRegion existingRegion,
            TextCandidateRegion currentRegion)
        {
            ArgumentNullException.ThrowIfNull(existingRegion);
            ArgumentNullException.ThrowIfNull(currentRegion);

            return existingRegion.Candidate.SourceCandidateBounds.Count
                == currentRegion.Candidate.SourceCandidateBounds.Count;
        }

        private static bool HasBoundedCandidateMemberGeometryJitter(
            TextCandidateRegion existingRegion,
            TextCandidateRegion currentRegion)
        {
            var existingMembers = OrderCandidateMemberBounds(existingRegion.Candidate.SourceCandidateBounds);
            var currentMembers = OrderCandidateMemberBounds(currentRegion.Candidate.SourceCandidateBounds);
            if (existingMembers.Length != currentMembers.Length)
            {
                return false;
            }

            return existingMembers
                .Zip(currentMembers)
                .All(pair => HasBoundedCandidateGeometryJitter(pair.First, pair.Second));
        }

        private static BoundingBox[] OrderCandidateMemberBounds(IEnumerable<BoundingBox> bounds)
        {
            return bounds
                .OrderBy(member => member.Y)
                .ThenBy(member => member.X)
                .ThenBy(member => member.Width)
                .ThenBy(member => member.Height)
                .ToArray();
        }

        private static bool HasBoundedCandidateGeometryJitter(BoundingBox anchor, BoundingBox current)
        {
            return Math.Abs(anchor.X - current.X) <= MaximumCandidateGeometryJitterPixels
                && Math.Abs(anchor.Y - current.Y) <= MaximumCandidateGeometryJitterPixels
                && Math.Abs(anchor.Right - current.Right) <= MaximumCandidateGeometryJitterPixels
                && Math.Abs(anchor.Bottom - current.Bottom) <= MaximumCandidateGeometryJitterPixels;
        }

        private static double CalculateIntersectionOverUnion(BoundingBox left, BoundingBox right)
        {
            var intersectionWidth = Math.Min(left.Right, right.Right) - Math.Max(left.X, right.X);
            var intersectionHeight = Math.Min(left.Bottom, right.Bottom) - Math.Max(left.Y, right.Y);
            if (intersectionWidth <= 0 || intersectionHeight <= 0)
            {
                return 0d;
            }

            var intersectionArea = checked((long)intersectionWidth * intersectionHeight);
            var unionArea = checked(
                (long)left.Width * left.Height
                + (long)right.Width * right.Height
                - intersectionArea);
            return unionArea <= 0 ? 0d : intersectionArea / (double)unionArea;
        }

        private bool CancelAndRemoveCandidate(
            LiveZoneState sourceState,
            LiveCandidateState candidateState,
            ICollection<string> cancelledZoneIds,
            LiveCandidateCancellationReason cancellationReason,
            bool allowTransientOverlayRetention = false)
        {
            var hadPublishedResult = candidateState.Result is not null || candidateState.Failure is not null;
            if (allowTransientOverlayRetention
                && cancellationReason == LiveCandidateCancellationReason.CandidateDisappeared)
            {
                RegisterTransientEmptyOverlayRetention(candidateState);
            }

            if (candidateState.ActiveWork is not null)
            {
                cancelledZoneIds.Add(candidateState.Id);
                RecordCandidateLifecycleEvent(
                    LiveCandidateLifecycleEventKind.CandidateWorkCancelled,
                    candidateState,
                    cancellationReason: cancellationReason);
                CancelActiveWork(candidateState);
            }

            RecordCandidateLifecycleEvent(
                LiveCandidateLifecycleEventKind.CandidateRemoved,
                candidateState,
                cancellationReason: cancellationReason);
            ClearCandidateTextStability(candidateState);
            sourceState.CandidateStates.Remove(candidateState.Id);
            return hadPublishedResult;
        }

        private void StartCandidateWork(
            LiveCandidateState candidateState,
            TimeSpan captureElapsed,
            OverlayPlacementConstraints overlayPlacementConstraints)
        {
            var candidateCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationSource.Token);
            var providerRequestDiagnostics = new ProviderRequestDiagnosticsCollector();
            candidateState.WorkAttempt = checked(candidateState.WorkAttempt + 1);
            RecordCandidateLifecycleEvent(
                LiveCandidateLifecycleEventKind.CandidateWorkStarted,
                candidateState,
                frameCapturedAt: candidateState.Region.Frame.CapturedAt);
            var candidateWork = new LiveZoneWork(
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
                    candidateTranslationLimiter,
                    new CandidateRecognitionContext(
                        candidateState.Region.Candidate,
                        zoneStates[candidateState.SourceZoneId].Zone.AbsoluteBounds.Height,
                        candidateState.LastObservedTranslationInputSignature,
                        candidateState.TypewriterGrowthGuardActive),
                    providerRequestDiagnostics),
                candidateState.Revision,
                candidateState.GeometrySignature,
                candidateState.SourceIdentity,
                providerRequestDiagnostics);
            candidateState.ActiveWork = candidateWork;
            SignalWhenWorkCompletes(candidateWork.Task);
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
            var zoneWork = new LiveZoneWork(
                zoneCancellation,
                service.RunCapturedZoneAsync(
                    profile,
                    state.Zone,
                    capturedZone.Frame,
                    capturedZone.CaptureElapsed,
                    state.Result?.OverlaySnapshot,
                    runOptions,
                    zoneCancellation.Token));
            state.ActiveWork = zoneWork;
            SignalWhenWorkCompletes(zoneWork.Task);
        }

        private void SignalWhenWorkCompletes(Task workTask)
        {
            _ = workTask.ContinueWith(
                (_, state) => ((LiveTranslationSession)state!).SignalWorkCompletion(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void SignalWorkCompletion()
        {
            lock (workCompletionSignalSyncRoot)
            {
                workCompletionSignal.TrySetResult(true);
            }
        }

        private void ResetWorkCompletionSignalForCollection()
        {
            lock (workCompletionSignalSyncRoot)
            {
                if (workCompletionSignal.Task.IsCompleted)
                {
                    workCompletionSignal = CreateWorkCompletionSignal();
                }
            }
        }

        private static TaskCompletionSource<bool> CreateWorkCompletionSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
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

        private bool IsCandidateGroupingConfirmed(LiveCandidateState state)
        {
            return !runOptions.RequireStableTextBeforeTranslation
                || (state.ConsecutiveGroupingObservations >= runOptions.MinimumCandidateGroupingObservations
                    && state.GroupingObservedDuration >= runOptions.MinimumCandidateGroupingDuration);
        }

        private static void ObserveMatchingCandidateGrouping(
            LiveCandidateState state,
            DateTimeOffset observedAt)
        {
            state.ConsecutiveGroupingObservations = checked(
                state.ConsecutiveGroupingObservations + 1);
            if (observedAt > state.GroupingLastObservedAt)
            {
                state.GroupingLastObservedAt = observedAt;
            }
        }

        private static void ResetCandidateGroupingObservation(
            LiveCandidateState state,
            DateTimeOffset observedAt)
        {
            state.ConsecutiveGroupingObservations = 1;
            state.GroupingFirstObservedAt = observedAt;
            state.GroupingLastObservedAt = observedAt;
        }

        private void ClearCandidateTextStability(LiveCandidateState candidateState)
        {
            var candidateProfile = CreateCandidateProfile(candidateState.Zone);
            service.ClearTextStabilityState(CreateStateKey(candidateProfile, candidateState.Zone));
        }

        private void RecordCandidateGroupingAwaitingConfirmationIfNeeded(
            LiveCandidateState candidateState,
            DateTimeOffset frameCapturedAt)
        {
            if (IsCandidateGroupingConfirmed(candidateState))
            {
                return;
            }

            RecordCandidateLifecycleEvent(
                LiveCandidateLifecycleEventKind.CandidateGroupingAwaitingConfirmation,
                candidateState,
                frameCapturedAt: frameCapturedAt);
        }

        private async Task<bool> CollectCompletedWorkAsync()
        {
            var overlayChanged = false;
            while (retiredCandidateProviderDiagnostics.TryDequeue(out var retiredDiagnostics))
            {
                RecordCandidateProviderRequestDiagnostics(
                    retiredDiagnostics.SourceZoneId,
                    retiredDiagnostics.CandidateId,
                    retiredDiagnostics.CandidateBounds,
                    retiredDiagnostics.CandidateRevision,
                    retiredDiagnostics.WorkAttempt,
                    retiredDiagnostics.Requests);
            }

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
                        var completedResult = await candidateWork.Task;
                        if (candidateWork.CandidateRevision != candidateState.Revision
                            || !string.Equals(
                                candidateWork.CandidateGeometrySignature,
                                candidateState.GeometrySignature,
                                StringComparison.Ordinal)
                            || candidateWork.CandidateSourceIdentity is null
                            || !candidateWork.CandidateSourceIdentity.Matches(candidateState.Region.Frame))
                        {
                            RecordCandidateLifecycleEvent(
                                LiveCandidateLifecycleEventKind.CandidateWorkCancelled,
                                candidateState,
                                cancellationReason: LiveCandidateCancellationReason.CandidateSourceChanged);
                            continue;
                        }

                        candidateState.Result = completedResult;
                        UpdateCandidateTypewriterGrowthState(candidateState, completedResult);
                        candidateState.ResultPublishedAt = null;
                        candidateState.Failure = null;
                        RecordCandidateLifecycleEvent(
                            completedResult.Optimization.TranslationSkipped
                                ? LiveCandidateLifecycleEventKind.CandidateWorkDeferredForStability
                                : LiveCandidateLifecycleEventKind.CandidateWorkCompleted,
                            candidateState,
                            completedResult,
                            frameCapturedAt: completedResult.CapturedFrame.CapturedAt);
                        overlayChanged = true;
                    }
                    catch (OperationCanceledException) when (candidateWork.Cancellation.IsCancellationRequested)
                    {
                    }
                    catch (TranslationPipelineException exception)
                    {
                        candidateState.Result = null;
                        candidateState.ResultPublishedAt = null;
                        candidateState.Failure = CreateZoneFailure(candidateState.Zone, exception);
                        var failureDiagnostics = CreateOcrFailureDiagnostics(exception);
                        var providerFailureDiagnostics = CreateProviderFailureDiagnostics(exception);
                        RecordCandidateLifecycleEvent(
                            LiveCandidateLifecycleEventKind.CandidateWorkFailed,
                            candidateState,
                            failureStage: exception.Stage,
                            failureExceptionType: failureDiagnostics.ExceptionType,
                            failureExceptionMessage: failureDiagnostics.ExceptionMessage,
                            failureRootCauseType: failureDiagnostics.RootCauseType,
                            failureRootCauseMessage: failureDiagnostics.RootCauseMessage,
                            failureProviderId: providerFailureDiagnostics.ProviderId,
                            failureProviderKind: providerFailureDiagnostics.FailureKind,
                            failureProviderHttpStatusCode: providerFailureDiagnostics.HttpStatusCode,
                            failureProviderPaused: providerFailureDiagnostics.Paused,
                            failureProviderRetryAfter: providerFailureDiagnostics.RetryAfter,
                            failureProviderNextRetryAt: providerFailureDiagnostics.NextRetryAt,
                            failureProviderConsecutiveFailureCount: providerFailureDiagnostics.ConsecutiveFailureCount);
                        overlayChanged = true;
                    }
                    finally
                    {
                        RecordCandidateProviderRequestDiagnostics(candidateState, candidateWork);
                        candidateWork.Cancellation.Dispose();
                    }
                }
            }

            return overlayChanged;
        }

        private static void UpdateCandidateTypewriterGrowthState(
            LiveCandidateState candidateState,
            TranslationPipelineResult result)
        {
            var translationSourceResult = TranslationTextGroupingService.CreateTranslationSourceResult(
                result.SourceOcrResult,
                candidateState.Zone);
            var currentTextSignature = CreateTextSignature(translationSourceResult);
            if (string.IsNullOrWhiteSpace(currentTextSignature))
            {
                return;
            }

            candidateState.LastObservedTranslationInputSignature = currentTextSignature;
            candidateState.TypewriterGrowthGuardActive =
                result.TextStability.TypewriterGrowthGuardApplied && !result.TextStability.IsStable;
        }

        private void RecordCandidateProviderRequestDiagnostics(
            LiveCandidateState candidateState,
            LiveZoneWork candidateWork)
        {
            if (candidateWork.ProviderRequestDiagnostics is null)
            {
                return;
            }

            RecordCandidateProviderRequestDiagnostics(
                candidateState.SourceZoneId,
                candidateState.Id,
                candidateState.Region.Candidate.Bounds,
                candidateWork.CandidateRevision,
                candidateState.WorkAttempt,
                candidateWork.ProviderRequestDiagnostics.CreateSnapshots());
        }

        private void RecordCandidateProviderRequestDiagnostics(
            string sourceZoneId,
            string candidateId,
            BoundingBox candidateBounds,
            int candidateRevision,
            int workAttempt,
            IReadOnlyList<TranslationProviderRequestDiagnosticsSnapshot> requests)
        {
            foreach (var request in requests)
            {
                if (request.NetworkAttempts.Count == 0)
                {
                    RecordLifecycleEvent(
                        LiveCandidateLifecycleEventKind.CandidateProviderRequestObserved,
                        zoneId: sourceZoneId,
                        candidateId: candidateId,
                        candidateBounds: candidateBounds,
                        candidateRevision: candidateRevision,
                        workAttempt: workAttempt,
                        providerDiagnosticRequestId: request.RequestId,
                        providerRequestQueuedAt: request.QueuedAt,
                        providerInvocationStartedAt: request.ProviderInvocationStartedAt,
                        providerInvocationCompletedAt: request.ProviderInvocationCompletedAt,
                        providerInvocationOutcome: request.Outcome,
                        providerNetworkRequestSent: false,
                        translationInputTexts: request.InputTexts);
                    continue;
                }

                foreach (var attempt in request.NetworkAttempts)
                {
                    RecordLifecycleEvent(
                        LiveCandidateLifecycleEventKind.CandidateProviderRequestObserved,
                        zoneId: sourceZoneId,
                        candidateId: candidateId,
                        candidateBounds: candidateBounds,
                        candidateRevision: candidateRevision,
                        workAttempt: workAttempt,
                        providerDiagnosticRequestId: request.RequestId,
                        providerRequestQueuedAt: request.QueuedAt,
                        providerInvocationStartedAt: request.ProviderInvocationStartedAt,
                        providerInvocationCompletedAt: request.ProviderInvocationCompletedAt,
                        providerInvocationOutcome: request.Outcome,
                        providerNetworkAttemptId: attempt.AttemptId,
                        providerNetworkRequestKind: attempt.Kind,
                        providerNetworkRequestSent: attempt.WasSent,
                        providerNetworkRequestStartedAt: attempt.StartedAt,
                        providerNetworkRequestCompletedAt: attempt.CompletedAt,
                        providerNetworkRequestOutcome: attempt.Outcome,
                        providerNetworkHttpStatusCode: attempt.StatusCode is { } statusCode
                            ? (int)statusCode
                            : null,
                        translationInputTexts: request.InputTexts);
                }
            }
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

        private void CancelActiveWork(LiveCandidateState state)
        {
            var work = state.ActiveWork;
            if (work is null)
            {
                return;
            }

            state.ActiveWork = null;
            var sourceZoneId = state.SourceZoneId;
            var candidateId = state.Id;
            var candidateBounds = state.Region.Candidate.Bounds;
            var candidateRevision = work.CandidateRevision;
            var workAttempt = state.WorkAttempt;
            work.Cancellation.Cancel();
            _ = work.Task.ContinueWith(
                task =>
                {
                    _ = task.Exception;
                    if (work.ProviderRequestDiagnostics is { } diagnostics)
                    {
                        if (task.IsCanceled)
                        {
                            diagnostics.MarkPendingRequestsCancelled(service.timeProvider.GetUtcNow());
                        }

                        retiredCandidateProviderDiagnostics.Enqueue(new RetiredCandidateProviderDiagnostics(
                            sourceZoneId,
                            candidateId,
                            candidateBounds,
                            candidateRevision,
                            workAttempt,
                            diagnostics.CreateSnapshots()));
                    }

                    work.Cancellation.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
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

        private void RecordOverlaySnapshotPublished(OverlaySnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            RecordLifecycleEvent(
                LiveCandidateLifecycleEventKind.OverlaySnapshotPublished,
                overlayTextItemCount: snapshot.TextItems.Count,
                overlayMaskItemCount: snapshot.MaskItems.Count);
        }

        private void RecordCandidateLifecycleEvent(
            LiveCandidateLifecycleEventKind kind,
            LiveCandidateState candidateState,
            TranslationPipelineResult? result = null,
            DateTimeOffset? frameCapturedAt = null,
            TranslationPipelineStage? failureStage = null,
            string? failureExceptionType = null,
            string? failureExceptionMessage = null,
            string? failureRootCauseType = null,
            string? failureRootCauseMessage = null,
            string? failureProviderId = null,
            TranslatorProviderFailureKind? failureProviderKind = null,
            int? failureProviderHttpStatusCode = null,
            bool? failureProviderPaused = null,
            TimeSpan? failureProviderRetryAfter = null,
            DateTimeOffset? failureProviderNextRetryAt = null,
            int? failureProviderConsecutiveFailureCount = null,
            LiveCandidateCancellationReason cancellationReason = LiveCandidateCancellationReason.None)
        {
            ArgumentNullException.ThrowIfNull(candidateState);

            var sourceOcrResult = result?.SourceOcrResult;
            var translationInputTexts = sourceOcrResult is null
                ? null
                : TranslationTextGroupingService
                    .CreateTranslationSourceResult(sourceOcrResult, candidateState.Zone)
                    .TextBlocks
                    .Select(block => block.Text);
            WritingSystemGroupingProfile? writingSystemGroupingProfile = sourceOcrResult is null
                ? null
                : WritingSystemGroupingProfileResolver.Resolve(
                    sourceOcrResult.Request.Language,
                    sourceOcrResult.Request.OrientationMode);

            RecordLifecycleEvent(
                kind,
                zoneId: candidateState.SourceZoneId,
                candidateId: candidateState.Id,
                candidateBounds: candidateState.Region.Candidate.Bounds,
                sourceCandidateBounds: candidateState.Region.Candidate.SourceCandidateBounds,
                candidateRevision: candidateState.Revision,
                workAttempt: candidateState.WorkAttempt,
                frameCapturedAt: frameCapturedAt,
                elapsed: result?.Timings.TotalElapsed,
                recognizedBlockCount: result?.RecognizedBlockCount,
                translationInputBlockCount: result?.TranslationInputBlockCount,
                translatedBlockCount: result?.TranslatedBlockCount,
                textStability: result?.TextStability,
                groupingObservationCount: candidateState.ConsecutiveGroupingObservations,
                requiredGroupingObservationCount: runOptions.RequireStableTextBeforeTranslation
                    ? runOptions.MinimumCandidateGroupingObservations
                    : 0,
                groupingFirstObservedAt: candidateState.GroupingFirstObservedAt,
                groupingLastObservedAt: candidateState.GroupingLastObservedAt,
                requiredGroupingDuration: runOptions.RequireStableTextBeforeTranslation
                    ? runOptions.MinimumCandidateGroupingDuration
                    : TimeSpan.Zero,
                translationMemoryCacheHitCount: result?.CacheResult?.MemoryHitCount,
                translationPersistentCacheHitCount: result?.CacheResult?.PersistentHitCount,
                translationCacheMissCount: result?.CacheResult?.MissCount,
                translationCacheStoredCount: result?.CacheResult?.StoredCount,
                translationOutputSanitizedCount: result?.CacheResult?.SanitizedTranslationCount,
                translationProviderId: result?.CacheResult?.ProviderId,
                providerRequestStartedAt: result?.CacheResult?.ProviderRequestStartedAt,
                providerRequestCompletedAt: result?.CacheResult?.ProviderRequestCompletedAt,
                failureStage: failureStage,
                failureExceptionType: failureExceptionType,
                failureExceptionMessage: failureExceptionMessage,
                failureRootCauseType: failureRootCauseType,
                failureRootCauseMessage: failureRootCauseMessage,
                failureProviderId: failureProviderId,
                failureProviderKind: failureProviderKind,
                failureProviderHttpStatusCode: failureProviderHttpStatusCode,
                failureProviderPaused: failureProviderPaused,
                failureProviderRetryAfter: failureProviderRetryAfter,
                failureProviderNextRetryAt: failureProviderNextRetryAt,
                failureProviderConsecutiveFailureCount: failureProviderConsecutiveFailureCount,
                cancellationReason: cancellationReason,
                orderedOcrBlockBounds: sourceOcrResult?.TextBlocks.Select(block => block.Bounds),
                orderedGroupedMemberBounds: sourceOcrResult is null
                    ? null
                    : TranslationTextGroupingService.ResolveOrderedMemberBoundsForDiagnostics(sourceOcrResult),
                writingSystemGroupingProfile: writingSystemGroupingProfile,
                ocrOrientationMode: sourceOcrResult?.Request.OrientationMode,
                candidateConfidence: candidateState.Region.Candidate.Confidence,
                ocrTexts: sourceOcrResult?.TextBlocks.Select(block => block.Text),
                translationInputTexts: translationInputTexts,
                translatedTexts: result?.TranslateResponse?.TranslatedTexts);
        }

        private void RecordLifecycleEvent(
            LiveCandidateLifecycleEventKind kind,
            string? zoneId = null,
            string? candidateId = null,
            BoundingBox? candidateBounds = null,
            IEnumerable<BoundingBox>? sourceCandidateBounds = null,
            int candidateRevision = 0,
            int workAttempt = 0,
            DateTimeOffset? frameCapturedAt = null,
            TimeSpan? elapsed = null,
            int? candidateCount = null,
            int? recognizedBlockCount = null,
            int? translationInputBlockCount = null,
            int? translatedBlockCount = null,
            TranslationPipelineTextStability? textStability = null,
            int? groupingObservationCount = null,
            int? requiredGroupingObservationCount = null,
            DateTimeOffset? groupingFirstObservedAt = null,
            DateTimeOffset? groupingLastObservedAt = null,
            TimeSpan? requiredGroupingDuration = null,
            int? translationMemoryCacheHitCount = null,
            int? translationPersistentCacheHitCount = null,
            int? translationCacheMissCount = null,
            int? translationCacheStoredCount = null,
            string? translationProviderId = null,
            DateTimeOffset? providerRequestStartedAt = null,
            DateTimeOffset? providerRequestCompletedAt = null,
            string? providerDiagnosticRequestId = null,
            DateTimeOffset? providerRequestQueuedAt = null,
            DateTimeOffset? providerInvocationStartedAt = null,
            DateTimeOffset? providerInvocationCompletedAt = null,
            TranslationProviderInvocationOutcome? providerInvocationOutcome = null,
            string? providerNetworkAttemptId = null,
            TranslationProviderNetworkRequestKind? providerNetworkRequestKind = null,
            bool? providerNetworkRequestSent = null,
            DateTimeOffset? providerNetworkRequestStartedAt = null,
            DateTimeOffset? providerNetworkRequestCompletedAt = null,
            TranslationProviderNetworkRequestOutcome? providerNetworkRequestOutcome = null,
            int? providerNetworkHttpStatusCode = null,
            int? overlayTextItemCount = null,
            int? overlayMaskItemCount = null,
            TranslationPipelineStage? failureStage = null,
            string? failureExceptionType = null,
            string? failureExceptionMessage = null,
            string? failureRootCauseType = null,
            string? failureRootCauseMessage = null,
            string? failureProviderId = null,
            TranslatorProviderFailureKind? failureProviderKind = null,
            int? failureProviderHttpStatusCode = null,
            bool? failureProviderPaused = null,
            TimeSpan? failureProviderRetryAfter = null,
            DateTimeOffset? failureProviderNextRetryAt = null,
            int? failureProviderConsecutiveFailureCount = null,
            LiveCandidateCancellationReason cancellationReason = LiveCandidateCancellationReason.None,
            int? translationOutputSanitizedCount = null,
            IEnumerable<BoundingBox>? orderedOcrBlockBounds = null,
            IEnumerable<BoundingBox>? orderedGroupedMemberBounds = null,
            WritingSystemGroupingProfile? writingSystemGroupingProfile = null,
            OcrOrientationMode? ocrOrientationMode = null,
            TextCandidateDetectorPreset? requestedDetectorPreset = null,
            TextCandidateDetectorPreset? effectiveDetectorPreset = null,
            double? detectorThreshold = null,
            double? detectorBoxThreshold = null,
            double? detectorUnclipRatio = null,
            int? rawDetectorCandidateCount = null,
            double? minimumDetectorConfidence = null,
            double? maximumDetectorConfidence = null,
            double? averageDetectorConfidence = null,
            double? candidateConfidence = null,
            IEnumerable<string>? ocrTexts = null,
            IEnumerable<string>? translationInputTexts = null,
            IEnumerable<string>? translatedTexts = null)
        {
            if (!runOptions.EnableCandidateDetectorPilot)
            {
                return;
            }

            if (candidateLifecycleEvents.Count == MaximumCandidateLifecycleEvents)
            {
                candidateLifecycleEvents.Dequeue();
                droppedCandidateLifecycleEventCount = checked(droppedCandidateLifecycleEventCount + 1);
            }

            var lifecycleEvent = new LiveCandidateLifecycleEvent(
                sequence: checked(candidateLifecycleEventSequence + 1),
                refreshSequence: refreshSequence,
                occurredAt: DateTimeOffset.UtcNow,
                kind: kind,
                zoneId: zoneId,
                candidateId: candidateId,
                candidateBounds: candidateBounds,
                sourceCandidateBounds: sourceCandidateBounds,
                candidateRevision: candidateRevision,
                workAttempt: workAttempt,
                frameCapturedAt: frameCapturedAt,
                elapsed: elapsed,
                candidateCount: candidateCount,
                recognizedBlockCount: recognizedBlockCount,
                translationInputBlockCount: translationInputBlockCount,
                translatedBlockCount: translatedBlockCount,
                textStability: textStability,
                groupingObservationCount: groupingObservationCount,
                requiredGroupingObservationCount: requiredGroupingObservationCount,
                groupingFirstObservedAt: groupingFirstObservedAt,
                groupingLastObservedAt: groupingLastObservedAt,
                requiredGroupingDuration: requiredGroupingDuration,
                translationMemoryCacheHitCount: translationMemoryCacheHitCount,
                translationPersistentCacheHitCount: translationPersistentCacheHitCount,
                translationCacheMissCount: translationCacheMissCount,
                translationCacheStoredCount: translationCacheStoredCount,
                translationProviderId: translationProviderId,
                providerRequestStartedAt: providerRequestStartedAt,
                providerRequestCompletedAt: providerRequestCompletedAt,
                providerDiagnosticRequestId: providerDiagnosticRequestId,
                providerRequestQueuedAt: providerRequestQueuedAt,
                providerInvocationStartedAt: providerInvocationStartedAt,
                providerInvocationCompletedAt: providerInvocationCompletedAt,
                providerInvocationOutcome: providerInvocationOutcome,
                providerNetworkAttemptId: providerNetworkAttemptId,
                providerNetworkRequestKind: providerNetworkRequestKind,
                providerNetworkRequestSent: providerNetworkRequestSent,
                providerNetworkRequestStartedAt: providerNetworkRequestStartedAt,
                providerNetworkRequestCompletedAt: providerNetworkRequestCompletedAt,
                providerNetworkRequestOutcome: providerNetworkRequestOutcome,
                providerNetworkHttpStatusCode: providerNetworkHttpStatusCode,
                overlayTextItemCount: overlayTextItemCount,
                overlayMaskItemCount: overlayMaskItemCount,
                failureStage: failureStage,
                failureExceptionType: failureExceptionType,
                failureExceptionMessage: failureExceptionMessage,
                failureRootCauseType: failureRootCauseType,
                failureRootCauseMessage: failureRootCauseMessage,
                failureProviderId: failureProviderId,
                failureProviderKind: failureProviderKind,
                failureProviderHttpStatusCode: failureProviderHttpStatusCode,
                failureProviderPaused: failureProviderPaused,
                failureProviderRetryAfter: failureProviderRetryAfter,
                failureProviderNextRetryAt: failureProviderNextRetryAt,
                failureProviderConsecutiveFailureCount: failureProviderConsecutiveFailureCount,
                cancellationReason: cancellationReason,
                translationOutputSanitizedCount: translationOutputSanitizedCount,
                orderedOcrBlockBounds: orderedOcrBlockBounds,
                orderedGroupedMemberBounds: orderedGroupedMemberBounds,
                writingSystemGroupingProfile: writingSystemGroupingProfile,
                ocrOrientationMode: ocrOrientationMode,
                requestedDetectorPreset: requestedDetectorPreset,
                effectiveDetectorPreset: effectiveDetectorPreset,
                detectorThreshold: detectorThreshold,
                detectorBoxThreshold: detectorBoxThreshold,
                detectorUnclipRatio: detectorUnclipRatio,
                rawDetectorCandidateCount: rawDetectorCandidateCount,
                minimumDetectorConfidence: minimumDetectorConfidence,
                maximumDetectorConfidence: maximumDetectorConfidence,
                averageDetectorConfidence: averageDetectorConfidence,
                candidateConfidence: candidateConfidence,
                ocrTexts: ocrTexts,
                translationInputTexts: translationInputTexts,
                translatedTexts: translatedTexts);
            candidateLifecycleEvents.Enqueue(lifecycleEvent);
            candidateLifecycleEventsSinceLastUpdate.Add(lifecycleEvent);
            candidateLifecycleEventSequence = checked(candidateLifecycleEventSequence + 1);
        }

        private static OcrFailureDiagnostics CreateOcrFailureDiagnostics(
            TranslationPipelineException exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            if (exception.Stage != TranslationPipelineStage.Ocr || exception.InnerException is not { } innerException)
            {
                return OcrFailureDiagnostics.Empty;
            }

            var rootCause = innerException.GetBaseException();
            return new OcrFailureDiagnostics(
                innerException.GetType().FullName ?? innerException.GetType().Name,
                innerException.Message,
                rootCause.GetType().FullName ?? rootCause.GetType().Name,
                rootCause.Message);
        }

        private static ProviderFailureDiagnostics CreateProviderFailureDiagnostics(
            TranslationPipelineException exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is not TranslatorProviderException providerException)
                {
                    continue;
                }

                return new ProviderFailureDiagnostics(
                    providerException.ProviderId,
                    providerException.FailureKind,
                    providerException.StatusCode is { } statusCode ? (int)statusCode : null,
                    providerException.NextRetryAt.HasValue,
                    providerException.RetryAfter,
                    providerException.NextRetryAt,
                    providerException.ConsecutiveFailureCount);
            }

            return ProviderFailureDiagnostics.Empty;
        }

        private static string CreateCandidateGeometrySignature(TextCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            return string.Join(
                ";",
                candidate.SourceCandidateBounds
                    .OrderBy(bounds => bounds.Y)
                    .ThenBy(bounds => bounds.X)
                    .ThenBy(bounds => bounds.Width)
                    .ThenBy(bounds => bounds.Height)
                    .Select(bounds => $"{bounds.X}:{bounds.Y}:{bounds.Width}:{bounds.Height}"));
        }

        private sealed record OcrFailureDiagnostics(
            string? ExceptionType,
            string? ExceptionMessage,
            string? RootCauseType,
            string? RootCauseMessage)
        {
            public static OcrFailureDiagnostics Empty { get; } = new(null, null, null, null);
        }

        private sealed record ProviderFailureDiagnostics(
            string? ProviderId,
            TranslatorProviderFailureKind? FailureKind,
            int? HttpStatusCode,
            bool? Paused,
            TimeSpan? RetryAfter,
            DateTimeOffset? NextRetryAt,
            int? ConsecutiveFailureCount)
        {
            public static ProviderFailureDiagnostics Empty { get; } =
                new(null, null, null, null, null, null, null);
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

            public DateTimeOffset? LastRefreshAt { get; set; }

            public TranslationPipelineResult? Result { get; set; }

            public TranslationPipelineZoneFailure? Failure { get; set; }

            public LiveZoneWork? ActiveWork { get; set; }

            public Dictionary<string, LiveCandidateState> CandidateStates { get; } = new(StringComparer.Ordinal);
        }

        private sealed class LiveCandidateState
        {
            public LiveCandidateState(
                string id,
                string sourceZoneId,
                OcrZone zone,
                TextCandidateRegion region,
                FrameFingerprint sourceIdentity,
                string geometrySignature,
                DateTimeOffset groupingObservedAt)
            {
                Id = id;
                SourceZoneId = sourceZoneId;
                Zone = zone;
                Region = region;
                SourceIdentity = sourceIdentity;
                GeometrySignature = geometrySignature;
                AnchorBounds = region.Candidate.Bounds;
                GroupingFirstObservedAt = groupingObservedAt;
                GroupingLastObservedAt = groupingObservedAt;
            }

            public string Id { get; }

            public string SourceZoneId { get; }

            public OcrZone Zone { get; }

            public TextCandidateRegion Region { get; set; }

            /// <summary>
            /// Fixed geometry at discovery. Matching against this anchor prevents successive
            /// small detector changes from gradually attaching a candidate to another region.
            /// </summary>
            public BoundingBox AnchorBounds { get; }

            public FrameFingerprint SourceIdentity { get; set; }

            public string GeometrySignature { get; set; }

            /// <summary>
            /// A live profile that requires stable OCR text must see the same bounded grouping
            /// on a subsequent detector pass before it starts crop OCR or translation.
            /// </summary>
            public int ConsecutiveGroupingObservations { get; set; } = 1;

            public DateTimeOffset GroupingFirstObservedAt { get; set; }

            public DateTimeOffset GroupingLastObservedAt { get; set; }

            public TimeSpan GroupingObservedDuration => GroupingLastObservedAt - GroupingFirstObservedAt;

            public int Revision { get; set; } = 1;

            public int WorkAttempt { get; set; }

            public TranslationPipelineResult? Result { get; set; }

            public DateTimeOffset? ResultPublishedAt { get; set; }

            public TranslationPipelineZoneFailure? Failure { get; set; }

            public LiveZoneWork? ActiveWork { get; set; }

            public string? LastObservedTranslationInputSignature { get; set; }

            public bool TypewriterGrowthGuardActive { get; set; }
        }

        private sealed record LiveZoneWork(
            CancellationTokenSource Cancellation,
            Task<TranslationPipelineResult> Task,
            int CandidateRevision = 0,
            string? CandidateGeometrySignature = null,
            FrameFingerprint? CandidateSourceIdentity = null,
            ProviderRequestDiagnosticsCollector? ProviderRequestDiagnostics = null);

        private sealed record LiveCapturedZone(
            OcrZone Zone,
            CapturedFrame Frame,
            TimeSpan CaptureElapsed);

        private sealed record LivePublishedState(
            TranslationPipelineResult? Result,
            TranslationPipelineZoneFailure? Failure);

        private sealed class TransientEmptyOverlayRetention
        {
            public TransientEmptyOverlayRetention(DateTimeOffset retainUntil)
            {
                RetainUntil = retainUntil;
            }

            public DateTimeOffset RetainUntil { get; set; }

            public Dictionary<string, RetainedCandidateSource> RetainedCandidates { get; } =
                new(StringComparer.Ordinal);
        }

        private sealed record RetainedCandidateSource(
            string SourceZoneId,
            FrameFingerprint SourceIdentity);

        private sealed record RetiredCandidateProviderDiagnostics(
            string SourceZoneId,
            string CandidateId,
            BoundingBox CandidateBounds,
            int CandidateRevision,
            int WorkAttempt,
            IReadOnlyList<TranslationProviderRequestDiagnosticsSnapshot> Requests);
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
