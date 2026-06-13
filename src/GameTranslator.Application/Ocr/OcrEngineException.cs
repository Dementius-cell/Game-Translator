namespace GameTranslator.Application.Ocr;

/// <summary>
/// Represents an OCR-specific failure reported by an application OCR engine.
/// </summary>
public sealed class OcrEngineException : InvalidOperationException
{
    public OcrEngineException(string message)
        : base(message)
    {
    }

    public OcrEngineException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
