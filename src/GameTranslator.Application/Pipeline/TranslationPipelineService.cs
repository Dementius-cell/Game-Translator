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

    public async Task<TranslationPipelineResult> RunAsync(
        GameProfile profile,
        OcrZone zone,
        OverlaySnapshot? previousSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(zone);
        cancellationToken.ThrowIfCancellationRequested();

        if (!profile.OcrZones.Any(profileZone => string.Equals(profileZone.Id, zone.Id, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Pipeline OCR zone must belong to the supplied profile.", nameof(zone));
        }

        var frame = await RunStageAsync(
            TranslationPipelineStage.Capture,
            () => captureService.CaptureAsync(CreateCaptureRegion(zone), cancellationToken));

        var request = new OcrRequest(frame, profile.TranslatorSettings.SourceLanguage, zone.Id);
        var sourceResult = await RunStageAsync(
            TranslationPipelineStage.Ocr,
            () => ocrService.RecognizeAsync(request, cancellationToken));

        if (sourceResult.TextBlocks.Count == 0)
        {
            var emptySnapshot = overlayPositioningService.CreateSnapshot(
                sourceResult,
                sourceResult.RecognizedAt,
                previousSnapshot,
                profile.OverlaySettings);
            await ShowOverlayAsync(emptySnapshot);

            return new TranslationPipelineResult(
                profile.Id,
                zone.Id,
                frame,
                sourceResult,
                translateResponse: null,
                emptySnapshot);
        }

        var texts = sourceResult.TextBlocks.Select(block => block.Text).ToArray();
        var cacheResult = await RunStageAsync(
            TranslationPipelineStage.Cache,
            () => cacheService.GetOrAddAsync(
                profile.TranslatorSettings,
                texts,
                async missingTexts =>
                {
                    var credentials = await RunStageAsync(
                        TranslationPipelineStage.Credentials,
                        () => credentialService.CreateCredentialsAsync(profile.TranslatorSettings.Provider, cancellationToken));

                    return await RunStageAsync(
                        TranslationPipelineStage.Translation,
                        () => translatorManager.TranslateAsync(profile.TranslatorSettings, missingTexts, credentials, cancellationToken));
                },
                DateTimeOffset.UtcNow,
                cancellationToken));
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
        await ShowOverlayAsync(snapshot);

        return new TranslationPipelineResult(
            profile.Id,
            zone.Id,
            frame,
            sourceResult,
            translateResponse,
            snapshot,
            cacheResult);
    }

    private async Task ShowOverlayAsync(OverlaySnapshot snapshot)
    {
        await RunStageAsync(
            TranslationPipelineStage.Overlay,
            () =>
            {
                overlayService.Show(snapshot);
                return Task.CompletedTask;
            });
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

    private static async Task<TValue> RunStageAsync<TValue>(
        TranslationPipelineStage stage,
        Func<Task<TValue>> action)
    {
        try
        {
            return await action();
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

    private static async Task RunStageAsync(
        TranslationPipelineStage stage,
        Func<Task> action)
    {
        try
        {
            await action();
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
