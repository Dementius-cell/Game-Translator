using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;

namespace GameTranslator.Application.Pipeline;

public sealed class TranslationPipelineException : Exception
{
    public TranslationPipelineException(
        TranslationPipelineStage stage,
        string message,
        Exception innerException,
        CapturedFrame? capturedFrame = null,
        OcrResult? sourceOcrResult = null)
        : base(message, innerException)
    {
        Stage = stage;
        CapturedFrame = capturedFrame;
        SourceOcrResult = sourceOcrResult;
    }

    public TranslationPipelineStage Stage { get; }

    public CapturedFrame? CapturedFrame { get; }

    public OcrResult? SourceOcrResult { get; }
}

public enum TranslationPipelineStage
{
    Capture,
    Ocr,
    Cache,
    Credentials,
    Translation,
    Overlay,
}
