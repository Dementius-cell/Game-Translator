namespace GameTranslator.Application.Ocr;

/// <summary>
/// Recognizes text blocks from captured frames without exposing platform-specific OCR APIs.
/// </summary>
public interface IOcrEngine
{
    Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default);
}
