using GameTranslator.Application.Capture;

namespace GameTranslator.Application.Ocr;

/// <summary>
/// Contains recognized OCR text blocks for one captured frame.
/// </summary>
public sealed class OcrResult
{
    public OcrResult(
        OcrRequest request,
        IEnumerable<OcrTextBlock> textBlocks,
        DateTimeOffset recognizedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(textBlocks);

        var blockList = textBlocks.ToArray();
        foreach (var block in blockList)
        {
            if (!block.Bounds.IsWithin(request.Frame.Width, request.Frame.Height))
            {
                throw new ArgumentException(
                    "OCR text block bounds must stay within the captured frame.",
                    nameof(textBlocks));
            }
        }

        Request = request;
        ZoneId = request.ZoneId;
        Region = request.Region;
        Language = request.Language;
        InputWidth = request.Frame.Width;
        InputHeight = request.Frame.Height;
        TextBlocks = blockList;
        RecognizedAt = recognizedAt;
    }

    public OcrRequest Request { get; }

    public string? ZoneId { get; }

    public CaptureRegion Region { get; }

    public string Language { get; }

    public int InputWidth { get; }

    public int InputHeight { get; }

    public IReadOnlyList<OcrTextBlock> TextBlocks { get; }

    public DateTimeOffset RecognizedAt { get; }

    public string Text => string.Join(Environment.NewLine, TextBlocks.Select(block => block.Text));
}
