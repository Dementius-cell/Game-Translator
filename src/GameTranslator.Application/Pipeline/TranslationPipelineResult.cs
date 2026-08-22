using GameTranslator.Application.Cache;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Translation;

namespace GameTranslator.Application.Pipeline;

public sealed class TranslationPipelineResult
{
    public TranslationPipelineResult(
        string profileId,
        string zoneId,
        CapturedFrame capturedFrame,
        OcrResult sourceOcrResult,
        TranslateResponse? translateResponse,
        OverlaySnapshot overlaySnapshot,
        TranslationCacheResult? cacheResult = null,
        TranslationPipelineTimings? timings = null,
        TranslationPipelineOptimizationInfo? optimization = null,
        int translationInputBlockCount = 0,
        TranslationPipelineTextStability? textStability = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        if (translationInputBlockCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(translationInputBlockCount));
        }

        ProfileId = profileId?.Trim() ?? string.Empty;
        ZoneId = zoneId;
        CapturedFrame = capturedFrame ?? throw new ArgumentNullException(nameof(capturedFrame));
        SourceOcrResult = sourceOcrResult ?? throw new ArgumentNullException(nameof(sourceOcrResult));
        TranslateResponse = translateResponse;
        OverlaySnapshot = overlaySnapshot ?? throw new ArgumentNullException(nameof(overlaySnapshot));
        CacheResult = cacheResult;
        Timings = timings ?? TranslationPipelineTimings.Empty;
        Optimization = optimization ?? TranslationPipelineOptimizationInfo.None;
        TranslationInputBlockCount = translationInputBlockCount;
        TextStability = textStability ?? TranslationPipelineTextStability.NotRequired;
    }

    public string ProfileId { get; }

    public string ZoneId { get; }

    public CapturedFrame CapturedFrame { get; }

    public OcrResult SourceOcrResult { get; }

    public TranslateResponse? TranslateResponse { get; }

    public OverlaySnapshot OverlaySnapshot { get; }

    public TranslationCacheResult? CacheResult { get; }

    public TranslationPipelineTimings Timings { get; }

    public TranslationPipelineOptimizationInfo Optimization { get; }

    /// <summary>
    /// Count of logical OCR groups submitted to the translator. This is a count only and never
    /// exposes recognized or translated text.
    /// </summary>
    public int TranslationInputBlockCount { get; }

    /// <summary>
    /// Privacy-safe timing outcome of the optional text-stability gate.
    /// </summary>
    public TranslationPipelineTextStability TextStability { get; }

    public int RecognizedBlockCount => SourceOcrResult.TextBlocks.Count;

    public int TranslatedBlockCount => TranslateResponse?.TranslatedTexts.Count ?? 0;
}
