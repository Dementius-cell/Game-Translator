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

    public TranslationPipelineService(
        CaptureService captureService,
        OcrService ocrService,
        TranslatorManager translatorManager,
        TranslatorCredentialService credentialService,
        TranslationCacheService cacheService,
        OverlayPositioningService overlayPositioningService,
        IOverlayService overlayService)
    {
        this.captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        this.translatorManager = translatorManager ?? throw new ArgumentNullException(nameof(translatorManager));
        this.credentialService = credentialService ?? throw new ArgumentNullException(nameof(credentialService));
        this.cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        this.overlayPositioningService = overlayPositioningService ?? throw new ArgumentNullException(nameof(overlayPositioningService));
        this.overlayService = overlayService ?? throw new ArgumentNullException(nameof(overlayService));
    }

    public Task<TranslationPipelineResult> RunAsync(
        GameProfile profile,
        OcrZone zone,
        OverlaySnapshot? previousSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        return RunZoneAsync(profile, zone, previousSnapshot, showOverlay: true, cancellationToken);
    }

    public async Task<TranslationPipelineBatchResult> RunAllZonesAsync(
        GameProfile profile,
        OverlaySnapshot? previousSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

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
                results.Add(await RunZoneAsync(profile, zone, previousSnapshot: null, showOverlay: false, cancellationToken));
            }
            catch (TranslationPipelineException exception)
            {
                failures.Add(new TranslationPipelineZoneFailure(
                    zone.Id,
                    zone.Name,
                    exception.Stage,
                    exception.Message,
                    exception));
            }
        }

        var combinedSnapshot = CreateCombinedSnapshot(results, previousSnapshot, profile.OverlaySettings);
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
        CancellationToken cancellationToken)
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
            () => captureService.CaptureAsync(CreateCaptureRegion(zone), cancellationToken));
        var frame = frameMeasurement.Value;
        captureElapsed = frameMeasurement.Elapsed;

        var request = new OcrRequest(frame, profile.TranslatorSettings.SourceLanguage, zone.Id, profile.OcrPreprocessingSettings);
        var ocrMeasurement = await RunTimedStageAsync(
            TranslationPipelineStage.Ocr,
            () => ocrService.RecognizeAsync(request, cancellationToken));
        var sourceResult = ocrMeasurement.Value;
        ocrElapsed = ocrMeasurement.Elapsed;

        if (sourceResult.TextBlocks.Count == 0)
        {
            var emptySnapshot = overlayPositioningService.CreateSnapshot(
                sourceResult,
                sourceResult.RecognizedAt,
                previousSnapshot,
                profile.OverlaySettings);
            if (showOverlay)
            {
                overlayElapsed = await ShowOverlayAsync(emptySnapshot);
            }

            totalStopwatch.Stop();

            return new TranslationPipelineResult(
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
                    totalStopwatch.Elapsed));
        }

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
                new InvalidOperationException("Translator response item count must match OCR text block count."));
        }

        var translatedResult = CreateTranslatedResult(sourceResult, translateResponse);
        var snapshot = overlayPositioningService.CreateSnapshot(
            translatedResult,
            translateResponse.TranslatedAt,
            previousSnapshot,
            profile.OverlaySettings);
        if (showOverlay)
        {
            overlayElapsed = await ShowOverlayAsync(snapshot);
        }

        totalStopwatch.Stop();

        return new TranslationPipelineResult(
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
                totalStopwatch.Elapsed));
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

    private static OverlaySnapshot CreateCombinedSnapshot(
        IReadOnlyList<TranslationPipelineResult> results,
        OverlaySnapshot? previousSnapshot,
        OverlaySettings overlaySettings)
    {
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
}
