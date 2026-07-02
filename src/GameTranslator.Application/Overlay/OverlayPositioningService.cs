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
    private const double SameReadingLineCenterToleranceFactor = 0.8;
    private const double SameReadingLineOverlapRatio = 0.15;
    private const int ExpandedTextHorizontalPadding = 8;
    private const int ExpandedTextVerticalPadding = 4;
    private const int ExpandedTextVerticalSafetyPadding = 10;
    private const int MinimumExpandedTextWidth = 96;
    private const int ExpandedTextMaxWidth = 960;
    private const double ExpandedTextSourceWidthMultiplier = 1.35;
    private const double VerticalExpandedTextSourceHeightMultiplier = 1.25;
    private const double VerticalSourceAspectRatioThreshold = 1.1;
    private const int AdditionalHorizontalLineYOffsetPixels = -8;
    private const double RightOverflowDampeningRatio = 0.5;
    private const int CompactVerticalSemanticWidthThreshold = 100;
    private const double VerticalMaxOverlayAreaRatio = 1.10;

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
        var positionedItems = AvoidCoveringOtherSemanticGroups(
            result,
            result.TextBlocks
            .Select((block, index) => CreatePositionedTextItem(
                result,
                block,
                result.TextBlockSources[index],
                effectiveTextStyle))
            .ToArray());
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
        OcrTextBlockSource source,
        OcrZoneTextStyle textStyle)
    {
        var sourceBounds = ScaleBounds(result, source.SemanticBounds);
        var memberBounds = source.MemberBounds
            .Select(bounds => ScaleBounds(result, bounds))
            .ToArray();
        var isVerticalSource = IsVerticalSource(result, source, sourceBounds.Width, sourceBounds.Height);
        var layoutBounds = CreateLayoutBounds(
            sourceBounds.X,
            sourceBounds.Y,
            sourceBounds.Width,
            sourceBounds.Height,
            textStyle,
            isVerticalSource);

        if (textStyle.LayoutMode == OverlayTextLayoutMode.ExpandFromSourceCenter)
        {
            return new OverlayPositionedTextItem(
                ApplySemanticPlacementRules(
                    CreateExpandedTextItem(result, block.Text, sourceBounds, textStyle, isVerticalSource),
                    sourceBounds,
                    memberBounds,
                    isVerticalSource),
                new OverlayMaskBounds(sourceBounds.X, sourceBounds.Y, sourceBounds.Width, sourceBounds.Height),
                sourceBounds);
        }

        return new OverlayPositionedTextItem(
            ApplySemanticPlacementRules(
                new OverlayTextItem(
                    block.Text,
                    layoutBounds.X,
                    layoutBounds.Y,
                    layoutBounds.Width,
                    layoutBounds.Height,
                    textStyle),
                sourceBounds,
                memberBounds,
                isVerticalSource),
            new OverlayMaskBounds(sourceBounds.X, sourceBounds.Y, sourceBounds.Width, sourceBounds.Height),
            sourceBounds);
    }

    private static OverlayLayoutBounds CreateLayoutBounds(
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        OcrZoneTextStyle textStyle,
        bool isVerticalSource)
    {
        if (!isVerticalSource)
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
        OcrZoneTextStyle textStyle,
        bool isVerticalSource)
    {
        if (isVerticalSource)
        {
            return CreateVerticalExpandedTextItem(text, sourceBounds, textStyle);
        }

        var centerX = sourceBounds.X + sourceBounds.Width / 2d;
        var centerY = sourceBounds.Y + sourceBounds.Height / 2d;
        var measuredSize = EstimateExpandedTextSize(
            text,
            sourceBounds.Width,
            sourceBounds.Height,
            textStyle,
            Math.Min(ExpandedTextMaxWidth, Math.Max(sourceBounds.Width, result.Region.Width)),
            isVerticalSource);
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

    private static OverlayTextItem CreateVerticalExpandedTextItem(
        string text,
        OverlayLayoutBounds sourceBounds,
        OcrZoneTextStyle textStyle)
    {
        var fontSize = Math.Max(OcrZoneTextStyle.MinimumFontSize, textStyle.FontSize);
        var semanticArea = checked(sourceBounds.Width * sourceBounds.Height);
        var maxOverlayArea = semanticArea * VerticalMaxOverlayAreaRatio;
        var width = Math.Max(1, sourceBounds.Width);
        var maxHeight = Math.Max(
            1,
            Math.Min(
                sourceBounds.Height,
                (int)Math.Floor(maxOverlayArea / width)));
        var fittedFontSize = fontSize;
        var desiredHeight = EstimateExpandedTextHeight(text, width, fittedFontSize, textStyle.IsBold);

        while (desiredHeight > maxHeight && fittedFontSize > OcrZoneTextStyle.MinimumFontSize)
        {
            fittedFontSize = Math.Max(OcrZoneTextStyle.MinimumFontSize, fittedFontSize - 1);
            desiredHeight = EstimateExpandedTextHeight(text, width, fittedFontSize, textStyle.IsBold);
        }

        var height = Math.Max(1, Math.Min(maxHeight, desiredHeight));
        var centerX = sourceBounds.X + sourceBounds.Width / 2d;
        var centerY = sourceBounds.Y + sourceBounds.Height / 2d;
        var initialHeight = Math.Max(1, Math.Min(maxHeight, EstimateSingleLineTextHeight(textStyle)));
        var initialBottom = Math.Min(
            sourceBounds.Bottom,
            Math.Max(
                sourceBounds.Y + initialHeight,
                (int)Math.Round(centerY + initialHeight / 2d, MidpointRounding.AwayFromZero)));
        var bottom = height >= sourceBounds.Height
            ? sourceBounds.Bottom
            : Math.Min(sourceBounds.Bottom, Math.Max(sourceBounds.Y + height, initialBottom));
        var x = ClampToScreenOrigin(
            (int)Math.Round(centerX - width / 2d, MidpointRounding.AwayFromZero));
        var y = ClampToScreenOrigin(bottom - height);
        var fittedTextStyle = fittedFontSize.Equals(fontSize)
            ? textStyle
            : textStyle with { FontSize = fittedFontSize };

        return new OverlayTextItem(
            text,
            x,
            y,
            width,
            height,
            fittedTextStyle);
    }

    private static ExpandedTextSize EstimateExpandedTextSize(
        string text,
        int sourceWidth,
        int sourceHeight,
        OcrZoneTextStyle textStyle,
        int maxWidth,
        bool isVerticalSource)
    {
        var normalizedText = text.Trim();
        var characterCount = Math.Max(1, normalizedText.Length);
        var glyphWidthFactor = textStyle.IsBold ? BoldGlyphWidthFactor : AverageGlyphWidthFactor;
        var fontSize = Math.Max(OcrZoneTextStyle.MinimumFontSize, textStyle.FontSize);
        var singleLineWidth = (int)Math.Ceiling(
            characterCount * fontSize * glyphWidthFactor + ExpandedTextHorizontalPadding * 2);
        var sourceWidthBasis = isVerticalSource
            ? Math.Max(sourceWidth * ExpandedTextSourceWidthMultiplier, sourceHeight * VerticalExpandedTextSourceHeightMultiplier)
            : sourceWidth * ExpandedTextSourceWidthMultiplier;
        var readableLimit = Math.Max(
            MinimumExpandedTextWidth,
            (int)Math.Ceiling(sourceWidthBasis));
        var layoutLimit = Math.Max(1, Math.Min(maxWidth, Math.Min(ExpandedTextMaxWidth, readableLimit)));
        var width = Math.Max(Math.Min(sourceWidth, layoutLimit), Math.Min(singleLineWidth, layoutLimit));
        var contentWidth = Math.Max(1, width - ExpandedTextHorizontalPadding * 2);
        var lineCount = EstimateWrappedLineCount(normalizedText, contentWidth, fontSize, glyphWidthFactor);
        var minimumHeight = isVerticalSource
            ? EstimateSingleLineTextHeight(textStyle)
            : sourceHeight;
        var height = Math.Max(
            minimumHeight,
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

    private static int EstimateExpandedTextHeight(
        string text,
        int width,
        double fontSize,
        bool isBold)
    {
        var glyphWidthFactor = isBold ? BoldGlyphWidthFactor : AverageGlyphWidthFactor;
        var contentWidth = Math.Max(1, width - ExpandedTextHorizontalPadding * 2);
        var lineCount = EstimateWrappedLineCount(text.Trim(), contentWidth, fontSize, glyphWidthFactor);

        return Math.Max(
            1,
            (int)Math.Ceiling(
                fontSize * LineHeightFactor * lineCount
                + ExpandedTextVerticalPadding * 2
                + ExpandedTextVerticalSafetyPadding));
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

    private static OverlayLayoutBounds ScaleBounds(OcrResult result, BoundingBox bounds)
    {
        var scaleX = result.Region.Width / (double)result.InputWidth;
        var scaleY = result.Region.Height / (double)result.InputHeight;
        var sourceX = checked(result.Region.X + ScaleCoordinate(bounds.X, scaleX));
        var sourceY = checked(result.Region.Y + ScaleCoordinate(bounds.Y, scaleY));
        var sourceWidth = ScaleSize(bounds.Width, scaleX);
        var sourceHeight = ScaleSize(bounds.Height, scaleY);

        return new OverlayLayoutBounds(sourceX, sourceY, sourceWidth, sourceHeight);
    }

    private static OverlayTextItem ApplySemanticPlacementRules(
        OverlayTextItem item,
        OverlayLayoutBounds semanticBounds,
        IReadOnlyList<OverlayLayoutBounds> memberBounds,
        bool isVerticalSource)
    {
        var lineCount = EstimateSourceLineCount(memberBounds);
        var x = item.X;
        var y = item.Y;
        var lineOffsetApplied = false;

        if (!isVerticalSource && lineCount > 1)
        {
            y = ClampToScreenOrigin(y + (lineCount - 1) * AdditionalHorizontalLineYOffsetPixels);
            lineOffsetApplied = true;
        }

        var rightOverflow = Math.Max(0, (x + item.Width) - semanticBounds.Right);
        var dampeningApplied = rightOverflow > 0
            && (!isVerticalSource || semanticBounds.Width >= CompactVerticalSemanticWidthThreshold);
        if (dampeningApplied)
        {
            x = ClampToScreenOrigin(x - (int)Math.Round(rightOverflow * RightOverflowDampeningRatio, MidpointRounding.AwayFromZero));
        }

        if (!isVerticalSource && lineOffsetApplied && dampeningApplied && y > semanticBounds.Y)
        {
            y = semanticBounds.Y;
        }

        return x == item.X && y == item.Y
            ? item
            : new OverlayTextItem(item.Text, x, y, item.Width, item.Height, item.TextStyle);
    }

    private static OverlayPositionedTextItem[] AvoidCoveringOtherSemanticGroups(
        OcrResult result,
        IReadOnlyList<OverlayPositionedTextItem> items)
    {
        if (items.Count <= 0)
        {
            return Array.Empty<OverlayPositionedTextItem>();
        }

        var regionBounds = new OverlayLayoutBounds(
            result.Region.X,
            result.Region.Y,
            result.Region.Width,
            result.Region.Height);
        var adjusted = new OverlayPositionedTextItem[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            var others = items
                .Where((_, otherIndex) => otherIndex != index)
                .Select(item => item.SemanticBounds)
                .ToArray();
            adjusted[index] = AdjustTextItemForSemanticNeighbors(items[index], others, regionBounds);
        }

        return adjusted;
    }

    private static OverlayPositionedTextItem AdjustTextItemForSemanticNeighbors(
        OverlayPositionedTextItem item,
        IReadOnlyList<OverlayLayoutBounds> otherSemanticBounds,
        OverlayLayoutBounds regionBounds)
    {
        var clamped = ClampTextItemToBounds(item.TextItem, regionBounds);
        if (!OverlapsAnySemanticGroup(clamped, otherSemanticBounds))
        {
            return item with { TextItem = clamped };
        }

        var candidates = CreateCandidateTextItems(clamped, item.SemanticBounds, otherSemanticBounds, regionBounds)
            .Where(candidate => !OverlapsAnySemanticGroup(candidate, otherSemanticBounds))
            .OrderBy(candidate => CalculateCenterDistance(candidate, item.SemanticBounds))
            .ThenBy(candidate => Math.Abs(candidate.X - clamped.X) + Math.Abs(candidate.Y - clamped.Y))
            .ToArray();

        return item with { TextItem = candidates.FirstOrDefault() ?? clamped };
    }

    private static IEnumerable<OverlayTextItem> CreateCandidateTextItems(
        OverlayTextItem item,
        OverlayLayoutBounds semanticBounds,
        IReadOnlyList<OverlayLayoutBounds> otherSemanticBounds,
        OverlayLayoutBounds regionBounds)
    {
        var xCandidates = new HashSet<int>
        {
            item.X,
            semanticBounds.X,
            semanticBounds.Right - item.Width,
            regionBounds.X,
            regionBounds.Right - item.Width,
        };
        var yCandidates = new HashSet<int>
        {
            item.Y,
            semanticBounds.Y,
            semanticBounds.Bottom - item.Height,
            regionBounds.Y,
            regionBounds.Bottom - item.Height,
        };

        foreach (var bounds in otherSemanticBounds)
        {
            xCandidates.Add(bounds.X - item.Width - 2);
            xCandidates.Add(bounds.Right + 2);
            yCandidates.Add(bounds.Y - item.Height - 2);
            yCandidates.Add(bounds.Bottom + 2);
        }

        foreach (var x in xCandidates.Select(candidate => ClampOriginToBounds(candidate, item.Width, regionBounds)).Distinct())
        {
            yield return CreateMovedTextItem(item, x, item.Y, regionBounds);
        }

        foreach (var y in yCandidates.Select(candidate => ClampOriginToBounds(candidate, item.Height, regionBounds.Y, regionBounds.Bottom)).Distinct())
        {
            yield return CreateMovedTextItem(item, item.X, y, regionBounds);
        }

        foreach (var x in xCandidates.Select(candidate => ClampOriginToBounds(candidate, item.Width, regionBounds)).Distinct())
        {
            foreach (var y in yCandidates.Select(candidate => ClampOriginToBounds(candidate, item.Height, regionBounds.Y, regionBounds.Bottom)).Distinct())
            {
                yield return CreateMovedTextItem(item, x, y, regionBounds);
            }
        }
    }

    private static OverlayTextItem ClampTextItemToBounds(OverlayTextItem item, OverlayLayoutBounds bounds)
    {
        var width = Math.Min(item.Width, bounds.Width);
        var height = Math.Min(item.Height, bounds.Height);

        return new OverlayTextItem(
            item.Text,
            ClampOriginToBounds(item.X, width, bounds),
            ClampOriginToBounds(item.Y, height, bounds.Y, bounds.Bottom),
            width,
            height,
            item.TextStyle);
    }

    private static OverlayTextItem CreateMovedTextItem(
        OverlayTextItem item,
        int x,
        int y,
        OverlayLayoutBounds bounds)
    {
        return new OverlayTextItem(
            item.Text,
            ClampOriginToBounds(x, item.Width, bounds),
            ClampOriginToBounds(y, item.Height, bounds.Y, bounds.Bottom),
            item.Width,
            item.Height,
            item.TextStyle);
    }

    private static int ClampOriginToBounds(int origin, int size, OverlayLayoutBounds bounds)
    {
        return ClampOriginToBounds(origin, size, bounds.X, bounds.Right);
    }

    private static int ClampOriginToBounds(int origin, int size, int minimum, int maximum)
    {
        return Math.Clamp(origin, minimum, Math.Max(minimum, maximum - size));
    }

    private static bool OverlapsAnySemanticGroup(
        OverlayTextItem item,
        IReadOnlyList<OverlayLayoutBounds> semanticBounds)
    {
        return semanticBounds.Any(bounds => HasMeaningfulIntersection(item, bounds));
    }

    private static bool HasMeaningfulIntersection(OverlayTextItem item, OverlayLayoutBounds bounds)
    {
        var width = Math.Min(item.X + item.Width, bounds.Right) - Math.Max(item.X, bounds.X);
        var height = Math.Min(item.Y + item.Height, bounds.Bottom) - Math.Max(item.Y, bounds.Y);

        return width > 2 && height > 2;
    }

    private static double CalculateCenterDistance(OverlayTextItem item, OverlayLayoutBounds semanticBounds)
    {
        var itemCenterX = item.X + item.Width / 2d;
        var itemCenterY = item.Y + item.Height / 2d;
        var deltaX = itemCenterX - semanticBounds.CenterX;
        var deltaY = itemCenterY - semanticBounds.CenterY;

        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static int EstimateSourceLineCount(IReadOnlyList<OverlayLayoutBounds> memberBounds)
    {
        if (memberBounds.Count <= 1)
        {
            return Math.Max(1, memberBounds.Count);
        }

        var rows = new List<List<OverlayLayoutBounds>>();
        foreach (var bounds in memberBounds.OrderBy(bounds => bounds.CenterY).ThenBy(bounds => bounds.X))
        {
            var row = rows.FirstOrDefault(existingRow => IsSameReadingLine(existingRow, bounds));
            if (row is null)
            {
                rows.Add(new List<OverlayLayoutBounds> { bounds });
                continue;
            }

            row.Add(bounds);
        }

        return Math.Max(1, rows.Count);
    }

    private static bool IsSameReadingLine(IReadOnlyList<OverlayLayoutBounds> row, OverlayLayoutBounds bounds)
    {
        var rowTop = row.Min(item => item.Y);
        var rowBottom = row.Max(item => item.Bottom);
        var rowAverageHeight = row.Average(item => item.Height);
        var overlap = Math.Min(rowBottom, bounds.Bottom) - Math.Max(rowTop, bounds.Y);
        if (overlap > 0)
        {
            var minimumHeight = Math.Min(rowAverageHeight, bounds.Height);
            if (overlap >= minimumHeight * SameReadingLineOverlapRatio)
            {
                return true;
            }
        }

        var rowCenterY = row.Average(item => item.CenterY);
        var tolerance = Math.Max(
            2d,
            Math.Max(rowAverageHeight, bounds.Height) * SameReadingLineCenterToleranceFactor);

        return Math.Abs(rowCenterY - bounds.CenterY) <= tolerance;
    }

    private static bool IsVerticalSource(
        OcrResult result,
        OcrTextBlockSource source,
        int sourceWidth,
        int sourceHeight)
    {
        if (source.OrientationMode is OcrOrientationMode.Vertical)
        {
            return true;
        }

        if (source.OrientationMode is OcrOrientationMode.Horizontal
            || result.Request.OrientationMode is OcrOrientationMode.Horizontal)
        {
            return false;
        }

        return (result.Request.OrientationMode is OcrOrientationMode.Vertical
                || result.Request.OrientationMode is OcrOrientationMode.Auto)
            && sourceHeight >= sourceWidth * VerticalSourceAspectRatioThreshold;
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

    private sealed record OverlayLayoutBounds(int X, int Y, int Width, int Height)
    {
        public int Right => checked(X + Width);

        public int Bottom => checked(Y + Height);

        public double CenterX => X + Width / 2d;

        public double CenterY => Y + Height / 2d;
    }

    private sealed record OverlayMaskBounds(int X, int Y, int Width, int Height);

    private sealed record OverlayPositionedTextItem(
        OverlayTextItem TextItem,
        OverlayMaskBounds MaskBounds,
        OverlayLayoutBounds SemanticBounds);

    private sealed record ExpandedTextSize(int Width, int Height);
}
