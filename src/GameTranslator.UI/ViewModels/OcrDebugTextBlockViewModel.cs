using GameTranslator.Application.Ocr;

namespace GameTranslator.UI.ViewModels;

public sealed class OcrDebugTextBlockViewModel
{
    public OcrDebugTextBlockViewModel(OcrTextBlock textBlock, bool isVisibleOnCapturePreview = true)
        : this(
            (textBlock ?? throw new ArgumentNullException(nameof(textBlock))).Text,
            textBlock.Bounds,
            isVisibleOnCapturePreview)
    {
    }

    public OcrDebugTextBlockViewModel(
        string text,
        BoundingBox bounds,
        bool isVisibleOnCapturePreview = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        Text = text;
        X = bounds.X;
        Y = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;
        IsVisibleOnCapturePreview = isVisibleOnCapturePreview;
    }

    public string Text { get; }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public bool IsVisibleOnCapturePreview { get; }

    public string CoordinatesSummary => $"X {X}  Y {Y}  W {Width}  H {Height}";

    public string DebugLabel => $"{CoordinatesSummary} | {Text}";
}
