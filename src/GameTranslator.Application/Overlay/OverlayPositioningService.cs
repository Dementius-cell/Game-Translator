using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Overlay;

/// <summary>
/// Maps OCR frame-relative text blocks into screen-space overlay text items.
/// </summary>
public sealed class OverlayPositioningService
{
    private const int DefaultJitterTolerancePixels = 4;

    public OverlaySnapshot CreateSnapshot(
        OcrResult result,
        DateTimeOffset shownAt,
        OverlaySettings? overlaySettings = null)
    {
        return CreateSnapshot(result, shownAt, previousSnapshot: null, overlaySettings);
    }

    public OverlaySnapshot CreateSnapshot(
        OcrResult result,
        DateTimeOffset shownAt,
        OverlaySnapshot? previousSnapshot,
        OverlaySettings? overlaySettings = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var textItems = result.TextBlocks
            .Select(block => CreateTextItem(result, block))
            .ToArray();

        if (previousSnapshot is not null)
        {
            textItems = StabilizeTextItems(textItems, previousSnapshot.TextItems);
        }

        return new OverlaySnapshot(
            textItems,
            shownAt,
            overlaySettings ?? previousSnapshot?.OverlaySettings);
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

    private static OverlayTextItem[] StabilizeTextItems(
        IReadOnlyList<OverlayTextItem> currentItems,
        IReadOnlyList<OverlayTextItem> previousItems)
    {
        var stabilizedItems = new OverlayTextItem[currentItems.Count];

        for (var index = 0; index < currentItems.Count; index++)
        {
            var current = currentItems[index];
            var previous = index < previousItems.Count
                ? previousItems[index]
                : null;

            stabilizedItems[index] = CanReusePreviousBounds(current, previous)
                ? new OverlayTextItem(current.Text, previous!.X, previous.Y, previous.Width, previous.Height)
                : current;
        }

        return stabilizedItems;
    }

    private static bool CanReusePreviousBounds(OverlayTextItem current, OverlayTextItem? previous)
    {
        if (previous is null || !string.Equals(current.Text, previous.Text, StringComparison.Ordinal))
        {
            return false;
        }

        return IsWithinJitterTolerance(current.X, previous.X)
            && IsWithinJitterTolerance(current.Y, previous.Y)
            && IsWithinJitterTolerance(current.Width, previous.Width)
            && IsWithinJitterTolerance(current.Height, previous.Height);
    }

    private static int ScaleCoordinate(int value, double scale)
    {
        return checked((int)Math.Round(value * scale, MidpointRounding.AwayFromZero));
    }

    private static int ScaleSize(int value, double scale)
    {
        return Math.Max(1, ScaleCoordinate(value, scale));
    }

    private static bool IsWithinJitterTolerance(int current, int previous)
    {
        return Math.Abs(current - previous) <= DefaultJitterTolerancePixels;
    }
}
