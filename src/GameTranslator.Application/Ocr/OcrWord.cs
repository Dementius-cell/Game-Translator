namespace GameTranslator.Application.Ocr;

/// <summary>
/// Carries optional word-level OCR geometry and engine-reported quality metadata.
/// </summary>
public sealed class OcrWord
{
    public OcrWord(
        string text,
        BoundingBox bounds,
        double? confidence,
        string recognitionPassId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(recognitionPassId);

        if (confidence.HasValue && !double.IsFinite(confidence.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "OCR word confidence must be finite when supplied.");
        }

        Text = text;
        Bounds = bounds;
        Confidence = confidence;
        RecognitionPassId = recognitionPassId;
    }

    public string Text { get; }

    public BoundingBox Bounds { get; }

    /// <summary>
    /// Gets the engine-reported score when available. Its scale is engine-local.
    /// </summary>
    public double? Confidence { get; }

    public string RecognitionPassId { get; }
}
