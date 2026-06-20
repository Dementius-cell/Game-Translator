namespace GameTranslator.Application.Ocr;

/// <summary>
/// Recognizes text blocks from captured frames without exposing platform-specific OCR APIs.
/// </summary>
public interface IOcrEngine
{
    string EngineId { get; }

    Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default);
}