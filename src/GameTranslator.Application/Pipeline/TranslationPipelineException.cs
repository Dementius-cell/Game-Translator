namespace GameTranslator.Application.Pipeline;

public sealed class TranslationPipelineException : Exception
{
    public TranslationPipelineException(
        TranslationPipelineStage stage,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Stage = stage;
    }

    public TranslationPipelineStage Stage { get; }
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
