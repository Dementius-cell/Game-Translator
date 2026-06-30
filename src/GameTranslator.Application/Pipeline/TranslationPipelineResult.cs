using GameTranslator.Application.Cache;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Translation;

namespace GameTranslator.Application.Pipeline;

/// <summary>
/// Captures the outcome of one translation pipeline run for one OCR zone.
/// </summary>
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
        OcrResult? translationSourceOcrResult = null,
        OcrResult? maskSourceOcrResult = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        ProfileId = profileId?.Trim() ?? string.Empty;
        ZoneId = zoneId;
        CapturedFrame = capturedFrame ?? throw new ArgumentNullException(nameof(capturedFrame));
        SourceOcrResult = sourceOcrResult ?? throw new ArgumentNullException(nameof(sourceOcrResult));
        TranslationSourceOcrResult = translationSourceOcrResult ?? SourceOcrResult;
        MaskSourceOcrResult = maskSourceOcrResult ?? SourceOcrResult;
        TranslateResponse = translateResponse;
        OverlaySnapshot = overlaySnapshot ?? throw new ArgumentNullException(nameof(overlaySnapshot));
        CacheResult = cacheResult;
        Timings = timings ?? TranslationPipelineTimings.Empty;
        Optimization = optimization ?? TranslationPipelineOptimizationInfo.None;
    }

    public string ProfileId { get; }

    public string ZoneId { get; }

    public CapturedFrame CapturedFrame { get; }

    /// <summary>
    /// Gets the raw OCR result returned by the selected OCR engine.
    /// </summary>
    public OcrResult SourceOcrResult { get; }

    /// <summary>
    /// Gets the semantic OCR blocks used for cache lookup and translation.
    /// </summary>
    public OcrResult TranslationSourceOcrResult { get; }

    /// <summary>
    /// Gets the accepted raw OCR blocks used to create overlay masks.
    /// </summary>
    public OcrResult MaskSourceOcrResult { get; }

    public TranslateResponse? TranslateResponse { get; }

    public OverlaySnapshot OverlaySnapshot { get; }

    public TranslationCacheResult? CacheResult { get; }

    public TranslationPipelineTimings Timings { get; }

    public TranslationPipelineOptimizationInfo Optimization { get; }

    public int RecognizedBlockCount => SourceOcrResult.TextBlocks.Count;

    public int TranslatedBlockCount => TranslateResponse?.TranslatedTexts.Count ?? 0;
}
