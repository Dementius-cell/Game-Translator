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
        TranslationPipelineOptimizationOptions? optimizationOptions = null)
    {
        this.captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        this.translatorManager = translatorManager ?? throw new ArgumentNullException(nameof(translatorManager));
        this.credentialService = credentialService ?? throw new ArgumentNullException(nameof(credentialService));
        this.cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        this.overlayPositioningService = overlayPositioningService ?? throw new ArgumentNullException(nameof(overlayPositioningService));
        this.overlayService = overlayService ?? throw new ArgumentNullException(nameof(overlayService));
        this.optimizationOptions = optimizationOptions ?? new TranslationPipelineOptimizationOptions();
    }

    public Task<TranslationPipelineResult> RunAsync(
        GameProfile profile,
        OcrZone zone,
        OverlaySnapshot? previousSnapshot = null,
        TranslationPipelineRunOptions? runOptions = null,
        CancellationToken cancellationToken = default)
    {
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
        runOptions ??= TranslationPipelineRunOptions.Default;

        var zones = (profile.OcrZones ?? Array.Empty<OcrZone>()).ToArray();
        if (zones.Length == 0)
        {
            throw new ArgumentException("Profile must contain at least one OCR zone.", nameof(profile));
        }

        var results = new List<TranslationPipelineResult>(zones.Length);
        var failures = new List<TranslationPipelineZoneFailure>();

        foreach (var zone in zones)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                results.Add(await RunZoneAsync(
                    profile,
                    zone,
                    previousSnapshot: null,
                    showOverlay: false,
                    runOptions,
                    cancellationToken,
                    overlaySnapshotToRestoreAfterCapture: previousSnapshot));
            }
            catch (TranslationPipelineException exception)
            {
                failures.Add(new TranslationPipelineZoneFailure(
                    zone.Id,
                    zone.Name,
                    exception.Stage,
                    exception.Message,
                    exception,
                    exception.CapturedFrame,
                    exception.SourceOcrResult));
            }
        }

        var combinedSnapshot = CreateCombinedSnapshot(results, previousSnapshot, profile.OverlaySettings, runOptions);
        await ShowOverlayAsync(combinedSnapshot);

        return new TranslationPipelineBatchResult(
            profile.Id,
            results,
            failures,
            combinedSnapshot);
    }

    private async Task<TranslationPipelineResult> RunZoneAsync(
        GameProfile profile,
        OcrZone zone,
        OverlaySnapshot? previousSnapshot,
        bool showOverlay,
        TranslationPipelineRunOptions runOptions,
        CancellationToken cancellationToken,
        OverlaySnapshot? overlaySnapshotToRestoreAfterCapture = null)
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

        var frameMeasurement = await RunTimedStageAsync(
            TranslationPipelineStage.Capture,
            () => CaptureFrameAsync(
                zone,
                runOptions,
                overlaySnapshotToRestoreAfterCapture ?? previousSnapshot,
                cancellationToken));
        var frame = frameMeasurement.Value;
        captureElapsed = frameMeasurement.Elapsed;

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

        var request = new OcrRequest(
            frame,
            profile.TranslatorSettings.SourceLanguage,
            zone.Id,
            profile.OcrPreprocessingSettings,
            profile.OcrSettings.Engine,
            profile.OcrSettings.OrientationMode);
        var ocrMeasurement = await RunTimedStageAsync(
            TranslationPipelineStage.Ocr,
            () => ocrService.RecognizeAsync(request, cancellationToken));
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
                profile.OverlaySettings);
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

        if (!IsTextStableForTranslation(optimizationContext.StateKey, sourceResult, runOptions))
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
            var texts = sourceResult.TextBlocks.Select(block => block.Text).ToArray();
            var cacheMeasurement = await RunTimedStageAsync(
                TranslationPipelineStage.Cache,
                () => cacheService.GetOrAddAsync(
                    profile.TranslatorSettings,
                    texts,
                    async missingTexts =>
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
                    },
                    DateTimeOffset.UtcNow,
                    cancellationToken));
            var cacheResult = cacheMeasurement.Value;
            cacheElapsed = cacheMeasurement.Elapsed;
            var translateResponse = cacheResult.ToTranslateResponse();

            if (translateResponse.TranslatedTexts.Count != sourceResult.TextBlocks.Count)
            {
                throw new TranslationPipelineException(
                    TranslationPipelineStage.Translation,
                    "Translation pipeline failed during Translation.",
                    new InvalidOperationException("Translator response item count must match OCR text block count."),
                    frame,
                    sourceResult);
            }

            var translatedResult = CreateTranslatedResult(sourceResult, translateResponse);
            var snapshot = overlayPositioningService.CreateSnapshot(
                translatedResult,
                translateResponse.TranslatedAt,
                previousSnapshot,
                zone.TextStyle,
                profile.OverlaySettings);
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

    private async Task<CapturedFrame> CaptureFrameAsync(
        OcrZone zone,
        TranslationPipelineRunOptions runOptions,
        OverlaySnapshot? overlaySnapshotToRestoreAfterCapture,
        CancellationToken cancellationToken)
    {
        var shouldRestoreOverlay = runOptions.RestorePreviousOverlayAfterCapture
            && overlaySnapshotToRestoreAfterCapture is not null
            && overlayService.IsVisible;

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
        return NormalizeRecognizedText(sourceResult.Text);
    }

    private static string NormalizeRecognizedText(string text)
    {
        return string.Join(' ', text.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries));
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
            profile.TranslatorSettings.SourceLanguage,
            zone.Id,
            profile.OcrPreprocessingSettings,
            profile.OcrSettings.Engine,
            profile.OcrSettings.OrientationMode);

        return new OcrResult(
            request,
            previousResult.TextBlocks,
            previousResult.RecognizedAt);
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
            profile.TranslatorSettings,
            profile.OcrSettings,
            profile.OcrPreprocessingSettings,
            profile.OverlaySettings);
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

        return new OcrResult(sourceResult.Request, translatedBlocks, translateResponse.TranslatedAt);
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
            && IsWaitingForStableText(results))
        {
            return previousSnapshot;
        }

        var successfulSnapshots = results.Select(result => result.OverlaySnapshot).ToArray();
        var shownAt = successfulSnapshots.Length == 0
            ? DateTimeOffset.UtcNow
            : successfulSnapshots.Max(snapshot => snapshot.ShownAt);
        var settings = overlaySettings ?? previousSnapshot?.OverlaySettings ?? OverlaySettings.Default;

        return new OverlaySnapshot(
            successfulSnapshots.SelectMany(snapshot => snapshot.TextItems),
            shownAt,
            settings,
            successfulSnapshots.SelectMany(snapshot => snapshot.MaskItems),
            successfulSnapshots.SelectMany(snapshot => snapshot.DebugItems),
            successfulSnapshots.SelectMany(snapshot => snapshot.DebugMetricLines));
    }

    private static bool IsWaitingForStableText(IReadOnlyList<TranslationPipelineResult> results)
    {
        return results.Count > 0
            && results.Any(result => result.RecognizedBlockCount > 0)
            && results.All(result => result.TranslatedBlockCount == 0)
            && results.Any(result => result.Optimization.TranslationSkipped);
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

    private sealed record PipelineFrameStateKey(
        string ProfileId,
        string ProfileName,
        string ZoneId,
        AbsoluteRectangle ZoneBounds,
        OcrZoneTextStyle TextStyle,
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
    }
}
