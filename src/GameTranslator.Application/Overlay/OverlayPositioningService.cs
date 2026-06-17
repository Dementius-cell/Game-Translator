using GameTranslator.Application.Ocr;

namespace GameTranslator.Application.Overlay;

/// <summary>
/// Maps OCR frame-relative text blocks into screen-space overlay text items.
/// </summary>
public sealed class OverlayPositioningService
{
    public OverlaySnapshot CreateSnapshot(OcrResult result, DateTimeOffset shownAt)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new OverlaySnapshot(
            result.TextBlocks.Select(block => CreateTextItem(result, block)),
            shownAt);
    }

    private static OverlayTextItem CreateTextItem(OcrResult result, OcrTextBlock block)
    {
        var scaleX = result.Region.Width / (double)result.InputWidth;
        var scaleY = result.Region.Height / (double)result.InputHeight;

        return new OverlayTextItem(
            block.Text,
            checked(result.Region.X + ScaleCoordinate(block.Bounds.X, scaleX)),
            checked(result.Region.Y + ScaleCoordinate(block.Bounds.Y, scaleY)),
            ScaleSize(block.Bounds.Width, scaleX),
            ScaleSize(block.Bounds.Height, scaleY));
    }

    private static int ScaleCoordinate(int value, double scale)
    {
        return checked((int)Math.Round(value * scale, MidpointRounding.AwayFromZero));
    }

    private static int ScaleSize(int value, double scale)
    {
        return Math.Max(1, ScaleCoordinate(value, scale));
    }
}
