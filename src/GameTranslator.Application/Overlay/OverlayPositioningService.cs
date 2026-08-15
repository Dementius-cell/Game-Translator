using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Overlay;

/// <summary>
/// Maps OCR frame-relative text blocks into screen-space overlay text items.
/// </summary>
public sealed class OverlayPositioningService
{
    private const int DefaultJitterTolerancePixels = 4;
    private const double SameReadingLineCenterToleranceFactor = 0.8;
    private const double SameReadingLineOverlapRatio = 0.15;
    private const int ExpandedTextHorizontalPadding = 8;
    private const int ExpandedTextVerticalPadding = 4;
    private const int ExpandedTextVerticalSafetyPadding = 2;
    private const int InitialTranslationFramePadding = 4;
    private const int MinimumExpandedTextWidth = 96;
    private const int ExpandedTextMaxWidth = 960;
    private const double DefaultSessionVerticalSourceWidthMultiplier = 2.0;
    private const double MinimumSessionVerticalSourceWidthMultiplier = 1.0;
    private const double MaximumSessionVerticalSourceWidthMultiplier = 2.5;
    private const double CenteredExpansionStepRatio = 0.15;
    private const int MinimumCenteredExpansionStep = 8;
    private const double VerticalSourceAspectRatioThreshold = 1.1;
    private const int AdditionalHorizontalLineYOffsetPixels = -8;
    private const string OverlayFitWarningPrefix = "Overlay fit warning:";

    private readonly IOverlayTextMeasurer textMeasurer;
    private readonly object sessionLayoutTuningSync = new();
    private double sessionVerticalSourceWidthMultiplier = DefaultSessionVerticalSourceWidthMultiplier;

    public OverlayPositioningService()
        : this(LegacyOverlayTextMeasurer.Instance)
    {
    }

    public OverlayPositioningService(IOverlayTextMeasurer textMeasurer)
    {
        this.textMeasurer = textMeasurer ?? throw new ArgumentNullException(nameof(textMeasurer));
    }

    /// <summary>
    /// Gets the session-only starting-width multiplier for vertical-source translation text.
    /// </summary>
    public double SessionVerticalSourceWidthMultiplier
    {
        get
        {
            lock (sessionLayoutTuningSync)
            {
                return sessionVerticalSourceWidthMultiplier;
            }
        }
    }

    /// <summary>
    /// Updates the session-only starting-width multiplier for vertical-source translation text.
    /// This value is intentionally not persisted in profiles or settings.
    /// </summary>
    public double SetSessionVerticalSourceWidthMultiplier(double multiplier)
    {
        if (!double.IsFinite(multiplier))
        {
            throw new ArgumentOutOfRangeException(nameof(multiplier), multiplier, "The vertical source-width multiplier must be finite.");
        }

        var normalizedMultiplier = Math.Clamp(
            multiplier,
            MinimumSessionVerticalSourceWidthMultiplier,
            MaximumSessionVerticalSourceWidthMultiplier);

        lock (sessionLayoutTuningSync)
        {
            sessionVerticalSourceWidthMultiplier = normalizedMultiplier;
        }

        return normalizedMultiplier;
    }

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
        OverlaySettings? overlaySettings = null,
        OverlayPlacementConstraints? placementConstraints = null)
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
                effectiveTextStyle,
                placementConstraints))
            .ToArray(),
            placementConstraints);
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

        var debugMetricLines = positionedItems
            .SelectMany(item => item.DebugMetricLines)
            .ToArray();

        return new OverlaySnapshot(
            textItems,
            shownAt,
            settings,
            maskItems,
            debugMetricLines: debugMetricLines,
            placementConstraints: placementConstraints);
    }

    /// <summary>
    /// Combines independently published snapshots and reflows only transient candidate callouts
    /// that would otherwise cover an already published translated item.
    /// </summary>
    public static OverlaySnapshot CombineCandidateSnapshots(
        IReadOnlyList<OverlaySnapshot> snapshots,
        DateTimeOffset shownAt,
        OverlaySettings overlaySettings)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(overlaySettings);

        var textItems = new List<OverlayTextItem>();
        var maskItems = new List<OverlayMaskItem>();
        var debugItems = new List<OverlayDebugItem>();
        var debugMetricLines = new List<string>();

        foreach (var snapshot in snapshots)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var constraints = snapshot.PlacementConstraints;
            for (var index = 0; index < snapshot.TextItems.Count; index++)
            {
                var item = snapshot.TextItems[index];
                if (constraints is null || !item.UseCalloutPresentation)
                {
                    textItems.Add(item);
                    continue;
                }

                var sourceBounds = index < snapshot.MaskItems.Count
                    ? CreateLayoutBounds(snapshot.MaskItems[index])
                    : CreateLayoutBounds(item);
                var reflowed = ReflowCandidateTextItem(
                    item,
                    sourceBounds,
                    constraints,
                    textItems.Select(CreateLayoutBounds).ToArray());
                textItems.Add(reflowed.TextItem);
                debugMetricLines.AddRange(reflowed.DebugMetricLines);
            }

            maskItems.AddRange(snapshot.MaskItems);
            debugItems.AddRange(snapshot.DebugItems);
            debugMetricLines.AddRange(snapshot.DebugMetricLines);
        }

        return new OverlaySnapshot(
            textItems,
            shownAt,
            overlaySettings,
            maskItems,
            debugItems,
            debugMetricLines);
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

    private OverlayPositionedTextItem CreatePositionedTextItem(
        OcrResult result,
        OcrTextBlock block,
        OcrTextBlockSource source,
        OcrZoneTextStyle textStyle,
        OverlayPlacementConstraints? placementConstraints)
    {
        var sourceBounds = ScaleBounds(result, source.SemanticBounds);
        var memberBounds = source.MemberBounds
            .Select(bounds => ScaleBounds(result, bounds))
            .ToArray();
        var isVerticalSource = IsVerticalSource(result, source, sourceBounds.Width, sourceBounds.Height);
        var layoutBounds = CreateLayoutBounds(
            block.Text,
            sourceBounds.X,
            sourceBounds.Y,
            sourceBounds.Width,
            sourceBounds.Height,
            textStyle,
            isVerticalSource);

        if (textStyle.LayoutMode == OverlayTextLayoutMode.ExpandFromSourceCenter)
        {
            var placement = CreateExpandedTextItem(
                result,
                block.Text,
                sourceBounds,
                textStyle,
                isVerticalSource,
                placementConstraints?.PlacementRegion);
            var textItem = placementConstraints is null
                ? placement.TextItem
                : new OverlayTextItem(
                    placement.TextItem.Text,
                    placement.TextItem.X,
                    placement.TextItem.Y,
                    placement.TextItem.Width,
                    placement.TextItem.Height,
                    placement.TextItem.TextStyle,
                    useCalloutPresentation: true);

            return new OverlayPositionedTextItem(
                ApplySemanticPlacementRules(
                    textItem,
                    sourceBounds,
                    memberBounds,
                    isVerticalSource),
                new OverlayMaskBounds(sourceBounds.X, sourceBounds.Y, sourceBounds.Width, sourceBounds.Height),
                sourceBounds,
                placement.DebugMetricLines);
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
            sourceBounds,
            Array.Empty<string>());
    }

    private OverlayLayoutBounds CreateLayoutBounds(
        string text,
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
        var height = Math.Max(sourceWidth, MeasureSingleLineTextHeight(text, textStyle, width));
        var x = Math.Max(0, (int)Math.Round(centerX - width / 2d, MidpointRounding.AwayFromZero));
        var y = Math.Max(0, (int)Math.Round(centerY - height / 2d, MidpointRounding.AwayFromZero));

        return new OverlayLayoutBounds(x, y, width, height);
    }

    private OverlayTextPlacement CreateExpandedTextItem(
        OcrResult result,
        string text,
        OverlayLayoutBounds sourceBounds,
        OcrZoneTextStyle textStyle,
        bool isVerticalSource,
        CaptureRegion? placementRegion)
    {
        var effectivePlacementRegion = placementRegion ?? result.Region;
        var regionBounds = new OverlayLayoutBounds(
            effectivePlacementRegion.X,
            effectivePlacementRegion.Y,
            effectivePlacementRegion.Width,
            effectivePlacementRegion.Height);
        var maximumWidth = Math.Max(1, Math.Min(ExpandedTextMaxWidth, regionBounds.Width));
        var maximumHeight = Math.Max(1, regionBounds.Height);
        var initialWidth = isVerticalSource
            ? GetInitialVerticalTranslationWidth(sourceBounds.Width, maximumWidth)
            : GetInitialHorizontalTranslationWidth(sourceBounds.Width, maximumWidth);
        var initialHeight = Math.Max(
            1,
            checked(sourceBounds.Height + InitialTranslationFramePadding * 2));
        var minimumSourceHeightForMeasuredLine = checked(textStyle.FontSize + ExpandedTextVerticalPadding * 2);
        if (isVerticalSource && sourceBounds.Height < minimumSourceHeightForMeasuredLine)
        {
            initialHeight = Math.Max(
                initialHeight,
                MeasureMinimumExpandedLineHeight(text, textStyle, initialWidth));
        }

        initialHeight = Math.Min(maximumHeight, initialHeight);

        return CreateCenteredExpandedTextItem(
            text,
            sourceBounds,
            regionBounds,
            textStyle,
            initialWidth,
            initialHeight,
            maximumWidth,
            maximumHeight,
            isVerticalSource);
    }

    private OverlayTextPlacement CreateCenteredExpandedTextItem(
        string text,
        OverlayLayoutBounds sourceBounds,
        OverlayLayoutBounds regionBounds,
        OcrZoneTextStyle textStyle,
        int initialWidth,
        int initialHeight,
        int maximumWidth,
        int maximumHeight,
        bool isVerticalSource)
    {
        var normalizedText = text.Trim();
        var originalFontSize = Math.Max(OcrZoneTextStyle.MinimumFontSize, textStyle.FontSize);
        var finalWidth = initialWidth;
        var finalHeight = initialHeight;
        var finalFontSize = originalFontSize;
        var fits = false;

        var fontSize = originalFontSize;
        while (true)
        {
            var candidateTextStyle = fontSize.Equals(originalFontSize)
                ? textStyle
                : textStyle with { FontSize = fontSize };
            var width = initialWidth;
            var heightLimit = initialHeight;

            while (true)
            {
                var desiredHeight = MeasureExpandedTextHeight(normalizedText, width, candidateTextStyle);
                if (desiredHeight <= heightLimit)
                {
                    finalWidth = width;
                    finalHeight = desiredHeight;
                    finalFontSize = fontSize;
                    fits = true;
                    break;
                }

                finalWidth = width;
                finalHeight = Math.Min(maximumHeight, desiredHeight);
                finalFontSize = fontSize;

                if (width < maximumWidth)
                {
                    var widthExpansionStep = Math.Max(
                        MinimumCenteredExpansionStep,
                        (int)Math.Ceiling(width * CenteredExpansionStepRatio));
                    width = Math.Min(maximumWidth, checked(width + widthExpansionStep));

                    if (!isVerticalSource)
                    {
                        heightLimit = Math.Min(maximumHeight, checked(heightLimit + widthExpansionStep));
                    }

                    continue;
                }

                if (heightLimit >= maximumHeight)
                {
                    break;
                }

                var heightExpansionStep = Math.Max(
                    MinimumCenteredExpansionStep,
                    (int)Math.Ceiling(heightLimit * CenteredExpansionStepRatio));
                heightLimit = Math.Min(maximumHeight, checked(heightLimit + heightExpansionStep));
            }

            if (fits)
            {
                break;
            }

            if (fontSize <= OcrZoneTextStyle.MinimumFontSize)
            {
                break;
            }

            fontSize = Math.Max(OcrZoneTextStyle.MinimumFontSize, fontSize - 1);
        }

        var fittedTextStyle = finalFontSize.Equals(originalFontSize)
            ? textStyle
            : textStyle with { FontSize = finalFontSize };
        var x = ClampOriginToBounds(
            (int)Math.Round(sourceBounds.CenterX - finalWidth / 2d, MidpointRounding.AwayFromZero),
            finalWidth,
            regionBounds);
        var y = ClampOriginToBounds(
            (int)Math.Round(sourceBounds.CenterY - finalHeight / 2d, MidpointRounding.AwayFromZero),
            finalHeight,
            regionBounds.Y,
            regionBounds.Bottom);
        var debugMetricLines = fits
            ? Array.Empty<string>()
            : new[]
            {
                CreateFitWarning(
                    isVerticalSource ? "vertical translation clipped" : "translation clipped",
                    finalWidth,
                    finalHeight,
                    finalFontSize,
                    regionBounds),
            };

        return new OverlayTextPlacement(
            new OverlayTextItem(
                text,
                x,
                y,
                finalWidth,
                finalHeight,
                fittedTextStyle),
            debugMetricLines);
    }

    private int GetInitialVerticalTranslationWidth(int sourceWidth, int maximumWidth)
    {
        var desiredWidth = Math.Max(
            1,
            (int)Math.Ceiling(sourceWidth * SessionVerticalSourceWidthMultiplier));
        var paddedSourceWidth = checked(sourceWidth + InitialTranslationFramePadding * 2);
        var minimumReadableWidth = Math.Min(MinimumExpandedTextWidth, maximumWidth);

        return Math.Clamp(Math.Max(desiredWidth, paddedSourceWidth), minimumReadableWidth, maximumWidth);
    }

    private static int GetInitialHorizontalTranslationWidth(int sourceWidth, int maximumWidth)
    {
        var paddedSourceWidth = checked(sourceWidth + InitialTranslationFramePadding * 2);

        return Math.Clamp(paddedSourceWidth, 1, maximumWidth);
    }

    private int MeasureExpandedTextHeight(
        string text,
        int width,
        OcrZoneTextStyle textStyle)
    {
        var contentWidth = Math.Max(1, width - ExpandedTextHorizontalPadding * 2);
        var measured = MeasureText(text.Trim(), textStyle, contentWidth);

        return Math.Max(
            1,
            checked(measured.Height + ExpandedTextVerticalPadding * 2 + ExpandedTextVerticalSafetyPadding));
    }

    private int MeasureMinimumExpandedLineHeight(
        string text,
        OcrZoneTextStyle textStyle,
        int maxWidth)
    {
        var measurement = MeasureText(text.Trim(), textStyle, Math.Max(1, maxWidth - ExpandedTextHorizontalPadding * 2));
        var lineHeight = measurement.Lines.FirstOrDefault()?.Height ?? measurement.Height;

        return Math.Max(
            1,
            checked(lineHeight + ExpandedTextVerticalPadding * 2 + ExpandedTextVerticalSafetyPadding));
    }

    private int MeasureSingleLineTextHeight(
        string text,
        OcrZoneTextStyle textStyle,
        int maxWidth)
    {
        var measurement = MeasureText(text, textStyle, Math.Max(1, maxWidth));

        return Math.Max(
            1,
            checked((measurement.Lines.FirstOrDefault()?.Height ?? measurement.Height) + ExpandedTextVerticalPadding * 2));
    }

    private OverlayTextMeasurement MeasureText(
        string text,
        OcrZoneTextStyle textStyle,
        int maxWidth)
    {
        return textMeasurer.Measure(new OverlayTextMeasurementRequest(
            text,
            textStyle,
            Math.Max(1, maxWidth)));
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
                ? new OverlayTextItem(
                    current.Text,
                    previous!.X,
                    previous.Y,
                    previous.Width,
                    previous.Height,
                    current.TextStyle,
                    current.UseCalloutPresentation)
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

        if (!isVerticalSource && lineCount > 1)
        {
            y = ClampToScreenOrigin(y + (lineCount - 1) * AdditionalHorizontalLineYOffsetPixels);
        }

        return x == item.X && y == item.Y
            ? item
            : new OverlayTextItem(item.Text, x, y, item.Width, item.Height, item.TextStyle, item.UseCalloutPresentation);
    }

    private static OverlayPositionedTextItem[] AvoidCoveringOtherSemanticGroups(
        OcrResult result,
        IReadOnlyList<OverlayPositionedTextItem> items,
        OverlayPlacementConstraints? placementConstraints = null)
    {
        if (items.Count <= 0)
        {
            return Array.Empty<OverlayPositionedTextItem>();
        }

        var effectivePlacementRegion = placementConstraints?.PlacementRegion ?? result.Region;
        var regionBounds = new OverlayLayoutBounds(
            effectivePlacementRegion.X,
            effectivePlacementRegion.Y,
            effectivePlacementRegion.Width,
            effectivePlacementRegion.Height);
        var occupiedBounds = placementConstraints?.OccupiedRegions
            .Select(region => new OverlayLayoutBounds(region.X, region.Y, region.Width, region.Height))
            .ToArray()
            ?? Array.Empty<OverlayLayoutBounds>();
        var adjusted = new OverlayPositionedTextItem[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            var others = items
                .Where((_, otherIndex) => otherIndex != index)
                .Select(item => item.SemanticBounds)
                .Concat(occupiedBounds)
                .ToArray();
            var placedTranslationBounds = adjusted
                .Take(index)
                .Select(item => CreateLayoutBounds(item.TextItem))
                .ToArray();

            adjusted[index] = AdjustTextItemForSemanticNeighbors(
                items[index],
                others,
                placedTranslationBounds,
                regionBounds);
        }

        return adjusted;
    }

    private static OverlayPositionedTextItem AdjustTextItemForSemanticNeighbors(
        OverlayPositionedTextItem item,
        IReadOnlyList<OverlayLayoutBounds> otherSemanticBounds,
        IReadOnlyList<OverlayLayoutBounds> placedTranslationBounds,
        OverlayLayoutBounds regionBounds)
    {
        var clamped = ClampTextItemToBounds(item.TextItem, regionBounds);
        var debugMetricLines = item.DebugMetricLines;
        if (clamped.Width != item.TextItem.Width || clamped.Height != item.TextItem.Height)
        {
            debugMetricLines = AppendDebugMetricLine(
                debugMetricLines,
                CreateFitWarning(
                    "translation bounds clipped",
                    clamped.Width,
                    clamped.Height,
                    item.TextItem.TextStyle.FontSize,
                    regionBounds));
        }

        var obstacleBounds = otherSemanticBounds
            .Concat(placedTranslationBounds)
            .ToArray();

        if (!OverlapsAnyBounds(clamped, obstacleBounds))
        {
            return item with { TextItem = clamped, DebugMetricLines = debugMetricLines };
        }

        var candidates = CreateCandidateTextItems(clamped, item.SemanticBounds, obstacleBounds, regionBounds)
            .Where(candidate => !OverlapsAnyBounds(candidate, obstacleBounds))
            .OrderBy(candidate => CalculateCenterDistance(candidate, item.SemanticBounds))
            .ThenBy(candidate => Math.Abs(candidate.X - clamped.X) + Math.Abs(candidate.Y - clamped.Y))
            .ToArray();
        var selected = candidates.FirstOrDefault();
        if (selected is null)
        {
            debugMetricLines = AppendDebugMetricLine(
                debugMetricLines,
                $"{OverlayFitWarningPrefix} translation still overlaps neighboring OCR/text bounds after placement.");
        }

        return item with { TextItem = selected ?? clamped, DebugMetricLines = debugMetricLines };
    }

    private static CandidateReflowResult ReflowCandidateTextItem(
        OverlayTextItem item,
        OverlayLayoutBounds sourceBounds,
        OverlayPlacementConstraints constraints,
        IReadOnlyList<OverlayLayoutBounds> placedTranslationBounds)
    {
        var regionBounds = new OverlayLayoutBounds(
            constraints.PlacementRegion.X,
            constraints.PlacementRegion.Y,
            constraints.PlacementRegion.Width,
            constraints.PlacementRegion.Height);
        var clamped = ClampTextItemToBounds(item, regionBounds);
        var obstacleBounds = constraints.OccupiedRegions
            .Select(region => new OverlayLayoutBounds(region.X, region.Y, region.Width, region.Height))
            .Concat(placedTranslationBounds)
            .ToArray();

        if (!OverlapsAnyBounds(clamped, obstacleBounds))
        {
            return new CandidateReflowResult(clamped, Array.Empty<string>());
        }

        var selected = CreateCandidateTextItems(clamped, sourceBounds, obstacleBounds, regionBounds)
            .Where(candidate => !OverlapsAnyBounds(candidate, obstacleBounds))
            .OrderBy(candidate => CalculateCenterDistance(candidate, sourceBounds))
            .ThenBy(candidate => Math.Abs(candidate.X - clamped.X) + Math.Abs(candidate.Y - clamped.Y))
            .FirstOrDefault();
        if (selected is not null)
        {
            return new CandidateReflowResult(selected, Array.Empty<string>());
        }

        return new CandidateReflowResult(
            clamped,
            new[] { $"{OverlayFitWarningPrefix} candidate translation still overlaps a published translated item after combined reflow." });
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
            item.TextStyle,
            item.UseCalloutPresentation);
    }

    private static OverlayLayoutBounds CreateLayoutBounds(OverlayTextItem item)
    {
        return new OverlayLayoutBounds(item.X, item.Y, item.Width, item.Height);
    }

    private static OverlayLayoutBounds CreateLayoutBounds(OverlayMaskItem item)
    {
        return new OverlayLayoutBounds(item.X, item.Y, item.Width, item.Height);
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
            item.TextStyle,
            item.UseCalloutPresentation);
    }

    private static int ClampOriginToBounds(int origin, int size, OverlayLayoutBounds bounds)
    {
        return ClampOriginToBounds(origin, size, bounds.X, bounds.Right);
    }

    private static int ClampOriginToBounds(int origin, int size, int minimum, int maximum)
    {
        return Math.Clamp(origin, minimum, Math.Max(minimum, maximum - size));
    }

    private static bool OverlapsAnyBounds(
        OverlayTextItem item,
        IReadOnlyList<OverlayLayoutBounds> bounds)
    {
        return bounds.Any(candidate => HasMeaningfulIntersection(item, candidate));
    }

    private static IReadOnlyList<string> AppendDebugMetricLine(
        IReadOnlyList<string> debugMetricLines,
        string line)
    {
        if (debugMetricLines.Count == 0)
        {
            return new[] { line };
        }

        return debugMetricLines
            .Concat(new[] { line })
            .ToArray();
    }

    private static string CreateFitWarning(
        string reason,
        int width,
        int height,
        double fontSize,
        OverlayLayoutBounds regionBounds)
    {
        return FormattableString.Invariant(
            $"{OverlayFitWarningPrefix} {reason} to {width}x{height} at {fontSize:0.#}px inside OCR zone {regionBounds.Width}x{regionBounds.Height}.");
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
        OverlayLayoutBounds SemanticBounds,
        IReadOnlyList<string> DebugMetricLines);

    private sealed record CandidateReflowResult(
        OverlayTextItem TextItem,
        IReadOnlyList<string> DebugMetricLines);

    private sealed record OverlayTextPlacement(
        OverlayTextItem TextItem,
        IReadOnlyList<string> DebugMetricLines);

}
