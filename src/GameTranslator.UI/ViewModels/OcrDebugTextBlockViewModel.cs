using GameTranslator.Application.Ocr;

namespace GameTranslator.UI.ViewModels;

public sealed class OcrDebugTextBlockViewModel
{
    public OcrDebugTextBlockViewModel(OcrTextBlock textBlock)
    {
        ArgumentNullException.ThrowIfNull(textBlock);

        Text = textBlock.Text;
        X = textBlock.Bounds.X;
        Y = textBlock.Bounds.Y;
        Width = textBlock.Bounds.Width;
        Height = textBlock.Bounds.Height;
    }

    public string Text { get; }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public string CoordinatesSummary => $"X {X}  Y {Y}  W {Width}  H {Height}";

    public string DebugLabel => $"{CoordinatesSummary} | {Text}";
}
