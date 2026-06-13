namespace GameTranslator.Application.Ocr;

/// <summary>
/// Represents one recognized text block and its frame-relative bounds.
/// </summary>
public sealed class OcrTextBlock
{
    public OcrTextBlock(string text, BoundingBox bounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        Text = text;
        Bounds = bounds;
    }

    public string Text { get; }

    public BoundingBox Bounds { get; }
}
