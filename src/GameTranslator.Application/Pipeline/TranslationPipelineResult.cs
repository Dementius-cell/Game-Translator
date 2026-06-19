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
        OverlaySnapshot overlaySnapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        ProfileId = profileId?.Trim() ?? string.Empty;
        ZoneId = zoneId;
        CapturedFrame = capturedFrame ?? throw new ArgumentNullException(nameof(capturedFrame));
        SourceOcrResult = sourceOcrResult ?? throw new ArgumentNullException(nameof(sourceOcrResult));
        TranslateResponse = translateResponse;
        OverlaySnapshot = overlaySnapshot ?? throw new ArgumentNullException(nameof(overlaySnapshot));
    }

    public string ProfileId { get; }

    public string ZoneId { get; }

    public CapturedFrame CapturedFrame { get; }

    public OcrResult SourceOcrResult { get; }

    public TranslateResponse? TranslateResponse { get; }

    public OverlaySnapshot OverlaySnapshot { get; }

    public int RecognizedBlockCount => SourceOcrResult.TextBlocks.Count;

    public int TranslatedBlockCount => TranslateResponse?.TranslatedTexts.Count ?? 0;
}
