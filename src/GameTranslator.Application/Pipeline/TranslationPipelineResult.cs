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
        TranslationPipelineTimings? timings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        ProfileId = profileId?.Trim() ?? string.Empty;
        ZoneId = zoneId;
        CapturedFrame = capturedFrame ?? throw new ArgumentNullException(nameof(capturedFrame));
        SourceOcrResult = sourceOcrResult ?? throw new ArgumentNullException(nameof(sourceOcrResult));
        TranslateResponse = translateResponse;
        OverlaySnapshot = overlaySnapshot ?? throw new ArgumentNullException(nameof(overlaySnapshot));
        CacheResult = cacheResult;
        Timings = timings ?? TranslationPipelineTimings.Empty;
    }

    public string ProfileId { get; }

    public string ZoneId { get; }

    public CapturedFrame CapturedFrame { get; }

    public OcrResult SourceOcrResult { get; }

    public TranslateResponse? TranslateResponse { get; }

    public OverlaySnapshot OverlaySnapshot { get; }

    public TranslationCacheResult? CacheResult { get; }

    public TranslationPipelineTimings Timings { get; }

    public int RecognizedBlockCount => SourceOcrResult.TextBlocks.Count;

    public int TranslatedBlockCount => TranslateResponse?.TranslatedTexts.Count ?? 0;
}