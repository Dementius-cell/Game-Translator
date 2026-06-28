using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Overlay;

/// <summary>
/// Maps OCR frame-relative text blocks into screen-space overlay text items.
/// </summary>
public sealed class OverlayPositioningService
{
    private const int DefaultJitterTolerancePixels = 4;
    private const double AverageGlyphWidthFactor = 0.62;
    private const double BoldGlyphWidthFactor = 0.68;
    private const double LineHeightFactor = 1.45;
    private const int ExpandedTextHorizontalPadding = 8;
    private const int ExpandedTextVerticalPadding = 4;
    private const int ExpandedTextVerticalSafetyPadding = 10;
    private const int MinimumExpandedTextWidth = 96;
    private const int ExpandedTextMaxWidth = 960;
    private const double ExpandedTextSourceWidthMultiplier = 2.5;
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
        var positionedItems = result.TextBlocks
            .Select(block => CreatePositionedTextItem(result, block, effectiveTextStyle))
            .ToArray();
        var textItems = positionedItems
            .Select(item => item.TextItem)
            .ToArray();

        if (previousSnapshot is not null)
        {
            textItems = StabilizeTextItems(textItems, previousSnapshot.TextItems);
        }

        var settings = overlaySettings ?? previousSnapshot?.OverlaySettings ?? OverlaySettings.Default;
        var maskItems = positionedItems
            .Select(item => CreateMaskItem(item.MaskBounds, settings))
            .ToArray();

        if (previousSnapshot is not null)
        {
            maskItems = StabilizeMaskItems(maskItems, textItems, previousSnapshot);
        }

        return new OverlaySnapshot(
            textItems,
            shownAt,
            settings,
            maskItems);
    }

    private static OverlayMaskItem CreateMaskItem(OverlayMaskBounds maskBounds, OverlaySettings settings)
    {
        var padding = Math.Max(0, (int)Math.Round(settings.Padding, MidpointRounding.AwayFromZero));
        var x = Math.Max(0, maskBounds.X - padding);
        var y = Math.Max(0, maskBounds.Y - padding);
        var width = checked(maskBounds.Width + padding * 2);
        var height = checked(maskBounds.Height + padding * 2);

        return new OverlayMaskItem(
            settings.MaskMode,
            settings.MaskColor,
            settings.Opacity,
            x,
            y,
            width,
            height);
    }

    private static OverlayPositionedTextItem CreatePositionedTextItem(
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
        var sourceBounds = new OverlayLayoutBounds(sourceX, sourceY, sourceWidth, sourceHeight);
        var layoutBounds = CreateLayoutBounds(result, sourceX, sourceY, sourceWidth, sourceHeight, textStyle);

        if (textStyle.LayoutMode == OverlayTextLayoutMode.ExpandFromSourceCenter)
        {
            return new OverlayPositionedTextItem(
                CreateExpandedTextItem(result, block.Text, sourceBounds, textStyle),
                new OverlayMaskBounds(sourceX, sourceY, sourceWidth, sourceHeight));
        }

        return new OverlayPositionedTextItem(
            new OverlayTextItem(
                block.Text,
                layoutBounds.X,
                layoutBounds.Y,
                layoutBounds.Width,
                layoutBounds.Height,
                textStyle),
            new OverlayMaskBounds(sourceX, sourceY, sourceWidth, sourceHeight));
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
        OcrResult result,
        string text,
        OverlayLayoutBounds sourceBounds,
        OcrZoneTextStyle textStyle)
    {
        var centerX = sourceBounds.X + sourceBounds.Width / 2d;
        var centerY = sourceBounds.Y + sourceBounds.Height / 2d;
        var measuredSize = EstimateExpandedTextSize(
            text,
            sourceBounds.Width,
            sourceBounds.Height,
            textStyle,
            Math.Min(ExpandedTextMaxWidth, Math.Max(sourceBounds.Width, result.Region.Width)));
        var x = ClampToScreenOrigin(
            (int)Math.Round(centerX - measuredSize.Width / 2d, MidpointRounding.AwayFromZero));
        var y = ClampToScreenOrigin(
            (int)Math.Round(centerY - measuredSize.Height / 2d, MidpointRounding.AwayFromZero));

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
        OcrZoneTextStyle textStyle,
        int maxWidth)
    {
        var normalizedText = text.Trim();
        var characterCount = Math.Max(1, normalizedText.Length);
        var glyphWidthFactor = textStyle.IsBold ? BoldGlyphWidthFactor : AverageGlyphWidthFactor;
        var fontSize = Math.Max(OcrZoneTextStyle.MinimumFontSize, textStyle.FontSize);
        var singleLineWidth = (int)Math.Ceiling(
            characterCount * fontSize * glyphWidthFactor + ExpandedTextHorizontalPadding * 2);
        var readableLimit = Math.Max(
            MinimumExpandedTextWidth,
            (int)Math.Ceiling(sourceWidth * ExpandedTextSourceWidthMultiplier));
        var layoutLimit = Math.Max(1, Math.Min(maxWidth, Math.Min(ExpandedTextMaxWidth, readableLimit)));
        var width = Math.Max(Math.Min(sourceWidth, layoutLimit), Math.Min(singleLineWidth, layoutLimit));
        var contentWidth = Math.Max(1, width - ExpandedTextHorizontalPadding * 2);
        var lineCount = EstimateWrappedLineCount(normalizedText, contentWidth, fontSize, glyphWidthFactor);
        var height = Math.Max(
            sourceHeight,
            (int)Math.Ceiling(
                fontSize * LineHeightFactor * lineCount
                + ExpandedTextVerticalPadding * 2
                + ExpandedTextVerticalSafetyPadding));

        return new ExpandedTextSize(width, height);
    }

    private static int EstimateWrappedLineCount(
        string text,
        int maxContentWidth,
        double fontSize,
        double glyphWidthFactor)
    {
        var normalizedText = text.Trim();
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return 1;
        }

        if (!normalizedText.Any(char.IsWhiteSpace))
        {
            return Math.Max(1, (int)Math.Ceiling(EstimateTextWidth(normalizedText, fontSize, glyphWidthFactor) / maxContentWidth));
        }

        var lineCount = 1;
        var currentLineWidth = 0d;
        var spaceWidth = EstimateTextWidth(" ", fontSize, glyphWidthFactor);
        foreach (var word in normalizedText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var wordWidth = EstimateTextWidth(word, fontSize, glyphWidthFactor);
            if (wordWidth > maxContentWidth)
            {
                if (currentLineWidth > 0)
                {
                    lineCount++;
                    currentLineWidth = 0;
                }

                lineCount += Math.Max(1, (int)Math.Ceiling(wordWidth / maxContentWidth)) - 1;
                currentLineWidth = wordWidth % maxContentWidth;
                continue;
            }

            var nextWidth = currentLineWidth <= 0
                ? wordWidth
                : currentLineWidth + spaceWidth + wordWidth;
            if (nextWidth <= maxContentWidth)
            {
                currentLineWidth = nextWidth;
                continue;
            }

            lineCount++;
            currentLineWidth = wordWidth;
        }

        return Math.Max(1, lineCount);
    }

    private static double EstimateTextWidth(string text, double fontSize, double glyphWidthFactor)
    {
        return Math.Max(1, text.Length) * fontSize * glyphWidthFactor;
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

    private static OverlayMaskItem[] StabilizeMaskItems(
        IReadOnlyList<OverlayMaskItem> currentItems,
        IReadOnlyList<OverlayTextItem> currentTextItems,
        OverlaySnapshot previousSnapshot)
    {
        var stabilizedItems = new OverlayMaskItem[currentItems.Count];

        for (var index = 0; index < currentItems.Count; index++)
        {
            var current = currentItems[index];
            var previous = index < previousSnapshot.MaskItems.Count
                ? previousSnapshot.MaskItems[index]
                : null;
            var currentText = currentTextItems[index];
            var previousText = index < previousSnapshot.TextItems.Count
                ? previousSnapshot.TextItems[index]
                : null;

            stabilizedItems[index] = CanReusePreviousMask(current, previous, currentText, previousText)
                ? previous!
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

    private static bool CanReusePreviousMask(
        OverlayMaskItem current,
        OverlayMaskItem? previous,
        OverlayTextItem currentText,
        OverlayTextItem? previousText)
    {
        if (previous is null
            || previousText is null
            || !string.Equals(currentText.Text, previousText.Text, StringComparison.Ordinal)
            || currentText.TextStyle != previousText.TextStyle
            || current.Mode != previous.Mode
            || !string.Equals(current.Color, previous.Color, StringComparison.Ordinal)
            || !current.Opacity.Equals(previous.Opacity))
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

    private static int ClampToScreenOrigin(int origin)
    {
        return Math.Max(0, origin);
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

    private sealed record OverlayMaskBounds(int X, int Y, int Width, int Height);

    private sealed record OverlayPositionedTextItem(OverlayTextItem TextItem, OverlayMaskBounds MaskBounds);

    private sealed record ExpandedTextSize(int Width, int Height);
}
