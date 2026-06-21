using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;

namespace GameTranslator.Application.Pipeline;

public sealed class TranslationPipelineZoneFailure
{
    public TranslationPipelineZoneFailure(
        string zoneId,
        string zoneName,
        TranslationPipelineStage stage,
        string message,
        Exception exception,
        CapturedFrame? capturedFrame = null,
        OcrResult? sourceOcrResult = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);

        ZoneId = zoneId;
        ZoneName = zoneName?.Trim() ?? string.Empty;
        Stage = stage;
        Message = message?.Trim() ?? string.Empty;
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        CapturedFrame = capturedFrame;
        SourceOcrResult = sourceOcrResult;
    }

    public string ZoneId { get; }

    public string ZoneName { get; }

    public TranslationPipelineStage Stage { get; }

    public string Message { get; }

    public Exception Exception { get; }

    public CapturedFrame? CapturedFrame { get; }

    public OcrResult? SourceOcrResult { get; }

    public int RecognizedBlockCount => SourceOcrResult?.TextBlocks.Count ?? 0;
}
