using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Overlay;

/// <summary>
/// Maps OCR frame-relative text blocks into screen-space overlay text items.
/// </summary>
public sealed class OverlayPositioningService
{
    private const int DefaultJitterTolerancePixels = 4;
    private const double AverageGlyphWidthFactor = 0.58;
    private const double BoldGlyphWidthFactor = 0.62;
    private const double LineHeightFactor = 1.35;
    private const int ExpandedTextHorizontalPadding = 8;
    private const int ExpandedTextVerticalPadding = 4;
    private const int ExpandedTextMaxWidth = 960;
    private const double VerticalSourceAspectRatioThreshold = 1.4;

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
        return CreateSnapshot(result, shownAt, previousSnapshot, OcrZoneTextStyle.Default, overlaySettings);
    }

    public OverlaySnapshot CreateSnapshot(
        OcrResult result,
        DateTimeOffset shownAt,
        OverlaySnapshot? previousSnapshot,
        OcrZoneTextStyle? textStyle,
        OverlaySettings? overlaySettings = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var effectiveTextStyle = NormalizeTextStyle(textStyle);
        var textItems = result.TextBlocks
            .Select(block => CreateTextItem(result, block, effectiveTextStyle))
            .ToArray();

        if (previousSnapshot is not null)
        {
            textItems = StabilizeTextItems(textItems, previousSnapshot.TextItems);
        }

        var settings = overlaySettings ?? previousSnapshot?.OverlaySettings ?? OverlaySettings.Default;
        var maskItems = textItems
            .Select(textItem => CreateMaskItem(textItem, settings))
            .ToArray();

        return new OverlaySnapshot(
            textItems,
            shownAt,
            settings,
            maskItems);
    }

    private static OverlayMaskItem CreateMaskItem(OverlayTextItem textItem, OverlaySettings settings)
    {
        var padding = Math.Max(0, (int)Math.Round(settings.Padding, MidpointRounding.AwayFromZero));
        var x = Math.Max(0, textItem.X - padding);
        var y = Math.Max(0, textItem.Y - padding);
        var width = checked(textItem.Width + padding * 2);
        var height = checked(textItem.Height + padding * 2);

        return new OverlayMaskItem(
            settings.MaskMode,
            settings.MaskColor,
            settings.Opacity,
            x,
            y,
            width,
            height);
    }

    private static OverlayTextItem CreateTextItem(
        OcrResult result,
        OcrTextBlock block,
        OcrZoneTextStyle textStyle)
    {
        var scaleX = result.Region.Width / (double)result.InputWidth;
        var scaleY = result.Region.Height / (double)result.InputHeight;
        var sourceX = checked(result.Region.X + ScaleCoordinate(block.Bounds.X, scaleX));
        var sourceY = checked(result.Region.Y + ScaleCoordinate(block.Bounds.Y, scaleY));
        var sourceWidth = ScaleSize(block.Bounds.Width, scaleX);
        var sourceHeight = ScaleSize(block.Bounds.Height, scaleY);
        var layoutBounds = CreateLayoutBounds(result, sourceX, sourceY, sourceWidth, sourceHeight, textStyle);

        if (textStyle.LayoutMode == OverlayTextLayoutMode.ExpandFromSourceCenter)
        {
            return CreateExpandedTextItem(
                block.Text,
                layoutBounds.X,
                layoutBounds.Y,
                layoutBounds.Width,
                layoutBounds.Height,
                textStyle);
        }

        return new OverlayTextItem(
            block.Text,
            layoutBounds.X,
            layoutBounds.Y,
            layoutBounds.Width,
            layoutBounds.Height,
            textStyle);
    }

    private static OverlayLayoutBounds CreateLayoutBounds(
        OcrResult result,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        OcrZoneTextStyle textStyle)
    {
        if (result.Request.OrientationMode is not OcrOrientationMode.Vertical
            || sourceHeight < sourceWidth * VerticalSourceAspectRatioThreshold)
        {
            return new OverlayLayoutBounds(sourceX, sourceY, sourceWidth, sourceHeight);
        }

        var centerX = sourceX + sourceWidth / 2d;
        var centerY = sourceY + sourceHeight / 2d;
        var width = Math.Min(ExpandedTextMaxWidth, Math.Max(sourceHeight, sourceWidth));
        var height = Math.Max(sourceWidth, EstimateSingleLineTextHeight(textStyle));
        var x = Math.Max(0, (int)Math.Round(centerX - width / 2d, MidpointRounding.AwayFromZero));
        var y = Math.Max(0, (int)Math.Round(centerY - height / 2d, MidpointRounding.AwayFromZero));

        return new OverlayLayoutBounds(x, y, width, height);
    }

    private static OverlayTextItem CreateExpandedTextItem(
        string text,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        OcrZoneTextStyle textStyle)
    {
        var measuredSize = EstimateExpandedTextSize(text, sourceWidth, sourceHeight, textStyle);
        var centerX = sourceX + sourceWidth / 2d;
        var centerY = sourceY + sourceHeight / 2d;
        var x = Math.Max(0, (int)Math.Round(centerX - measuredSize.Width / 2d, MidpointRounding.AwayFromZero));
        var y = Math.Max(0, (int)Math.Round(centerY - measuredSize.Height / 2d, MidpointRounding.AwayFromZero));

        return new OverlayTextItem(
            text,
            x,
            y,
            measuredSize.Width,
            measuredSize.Height,
            textStyle);
    }

    private static ExpandedTextSize EstimateExpandedTextSize(
        string text,
        int sourceWidth,
        int sourceHeight,
        OcrZoneTextStyle textStyle)
    {
        var characterCount = Math.Max(1, text.Trim().Length);
        var glyphWidthFactor = textStyle.IsBold ? BoldGlyphWidthFactor : AverageGlyphWidthFactor;
        var singleLineWidth = (int)Math.Ceiling(
            characterCount * Math.Max(OcrZoneTextStyle.MinimumFontSize, textStyle.FontSize) * glyphWidthFactor
            + ExpandedTextHorizontalPadding * 2);
        var width = Math.Max(sourceWidth, Math.Min(ExpandedTextMaxWidth, singleLineWidth));
        var lineCount = Math.Max(1, (int)Math.Ceiling(singleLineWidth / (double)Math.Max(1, width)));
        var height = Math.Max(
            sourceHeight,
            (int)Math.Ceiling(textStyle.FontSize * LineHeightFactor * lineCount + ExpandedTextVerticalPadding * 2));

        return new ExpandedTextSize(width, height);
    }

    private static int EstimateSingleLineTextHeight(OcrZoneTextStyle textStyle)
    {
        return (int)Math.Ceiling(textStyle.FontSize * LineHeightFactor + ExpandedTextVerticalPadding * 2);
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
                ? new OverlayTextItem(current.Text, previous!.X, previous.Y, previous.Width, previous.Height, current.TextStyle)
                : current;
        }

        return stabilizedItems;
    }

    private static bool CanReusePreviousBounds(OverlayTextItem current, OverlayTextItem? previous)
    {
        if (previous is null
            || !string.Equals(current.Text, previous.Text, StringComparison.Ordinal)
            || current.TextStyle != previous.TextStyle)
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

    private static OcrZoneTextStyle NormalizeTextStyle(OcrZoneTextStyle? textStyle)
    {
        var value = textStyle ?? OcrZoneTextStyle.Default;
        return value with
        {
            FontFamily = string.IsNullOrWhiteSpace(value.FontFamily)
                ? OcrZoneTextStyle.DefaultFontFamily
                : value.FontFamily.Trim(),
            FontSize = Math.Clamp(
                value.FontSize,
                OcrZoneTextStyle.MinimumFontSize,
                OcrZoneTextStyle.MaximumFontSize),
            LayoutMode = Enum.IsDefined(value.LayoutMode)
                ? value.LayoutMode
                : OverlayTextLayoutMode.FitToSourceBounds,
        };
    }

    private sealed record OverlayLayoutBounds(int X, int Y, int Width, int Height);

    private sealed record ExpandedTextSize(int Width, int Height);
}
