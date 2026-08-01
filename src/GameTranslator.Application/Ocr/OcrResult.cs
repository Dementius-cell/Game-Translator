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
        DateTimeOffset recognizedAt,
        IEnumerable<OcrTextBlockSource>? textBlockSources = null,
        IEnumerable<OcrWord>? words = null)
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

        var sourceList = textBlockSources?.ToArray()
            ?? blockList
                .Select(block => new OcrTextBlockSource(
                    block.Bounds,
                    new[] { block.Bounds }))
                .ToArray();
        if (sourceList.Length != blockList.Length)
        {
            throw new ArgumentException(
                "OCR text block source count must match OCR text block count.",
                nameof(textBlockSources));
        }

        foreach (var source in sourceList)
        {
            if (!source.SemanticBounds.IsWithin(request.Frame.Width, request.Frame.Height)
                || source.MemberBounds.Any(bounds => !bounds.IsWithin(request.Frame.Width, request.Frame.Height)))
            {
                throw new ArgumentException(
                    "OCR text block source bounds must stay within the captured frame.",
                    nameof(textBlockSources));
            }
        }

        var wordList = words?.ToArray() ?? Array.Empty<OcrWord>();
        foreach (var word in wordList)
        {
            if (!word.Bounds.IsWithin(request.Frame.Width, request.Frame.Height))
            {
                throw new ArgumentException(
                    "OCR word bounds must stay within the captured frame.",
                    nameof(words));
            }
        }

        Request = request;
        ZoneId = request.ZoneId;
        Region = request.Region;
        Language = request.Language;
        InputWidth = request.Frame.Width;
        InputHeight = request.Frame.Height;
        TextBlocks = blockList;
        TextBlockSources = sourceList;
        Words = wordList;
        RecognizedAt = recognizedAt;
    }

    public OcrRequest Request { get; }

    public string? ZoneId { get; }

    public CaptureRegion Region { get; }

    public string Language { get; }

    public int InputWidth { get; }

    public int InputHeight { get; }

    public IReadOnlyList<OcrTextBlock> TextBlocks { get; }

    public IReadOnlyList<OcrTextBlockSource> TextBlockSources { get; }

    /// <summary>
    /// Gets optional engine-reported word geometry and quality metadata.
    /// </summary>
    public IReadOnlyList<OcrWord> Words { get; }

    public DateTimeOffset RecognizedAt { get; }

    public string Text => string.Join(Environment.NewLine, TextBlocks.Select(block => block.Text));
}
