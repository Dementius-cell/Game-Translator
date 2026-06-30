using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Pipeline;

/// <summary>
/// Builds translation-ready OCR blocks while preserving separate source blocks for masking.
/// </summary>
public static class TranslationTextGroupingService
{
    private const double SameReadingLineCenterToleranceFactor = 0.8;
    private const double SameReadingLineOverlapRatio = 0.15;
    private const double VerticalReadingColumnCenterToleranceFactor = 0.75;
    private const double VerticalReadingColumnOverlapRatio = 0.35;
    private const int VerticalCjkNoiseSamplePadding = 12;
    private const int VerticalCjkNoiseSampleStep = 2;
    private const double VerticalCjkMinimumLightPixelRatio = 0.25;
    private const double VerticalCjkMaximumDarkPixelRatio = 0.55;
    private const double VerticalCjkMinimumGroupLightPixelRatio = 0.62;
    private const double VerticalCjkMaximumGroupMidPixelRatio = 0.3;
    private const double VerticalCjkSameColumnGapMultiplier = 1.25;
    private const double VerticalCjkAdjacentColumnOverlapRatio = 0.35;
    private const double VerticalCjkMaximumSemanticWidthToHeightRatio = 1.75;
    private const double VerticalCjkMinimumSemanticHeightToWidthRatio = 0.75;
    private const double VerticalCjkWideHorizontalNoiseRatio = 4d;
    private const int VerticalCjkWideHorizontalNoiseMinimumWidth = 48;
    private const double VerticalCjkMaximumColumnWidthRatio = 4d;
    private const double LightLuminanceThreshold = 220d;
    private const double DarkLuminanceThreshold = 80d;

    public static OcrResult CreateTranslationSourceResult(OcrResult sourceResult, OcrZone zone)
    {
        return CreateTextGroupingResult(sourceResult, zone).TranslationSourceResult;
    }

    /// <summary>
    /// Creates both the semantic translation source and the raw source blocks accepted for masking.
    /// </summary>
    public static TranslationTextGroupingResult CreateTextGroupingResult(OcrResult sourceResult, OcrZone zone)
    {
        ArgumentNullException.ThrowIfNull(sourceResult);
        ArgumentNullException.ThrowIfNull(zone);

        var maskSourceResult = CreateMaskSourceResult(sourceResult);
        if (zone.TranslationGroupingMode is TranslationGroupingMode.NearbyBlocks
            && ShouldUseVerticalCjkSemanticGrouping(maskSourceResult))
        {
            return CreateVerticalCjkNearbyBlocksResult(
                maskSourceResult,
                zone.TextGrouping ?? OcrZoneTextGroupingSettings.Default);
        }

        var translationSourceResult = zone.TranslationGroupingMode switch
        {
            TranslationGroupingMode.WholeZone => CreateWholeZoneResult(maskSourceResult),
            TranslationGroupingMode.NearbyBlocks => CreateNearbyBlocksResult(maskSourceResult, zone.TextGrouping ?? OcrZoneTextGroupingSettings.Default),
            _ => maskSourceResult,
        };

        return new TranslationTextGroupingResult(translationSourceResult, maskSourceResult);
    }

    private static OcrResult CreateWholeZoneResult(OcrResult sourceResult)
    {
        if (sourceResult.TextBlocks.Count <= 1)
        {
            return sourceResult;
        }

        var orderedBlocks = OrderBlocksByReadingPosition(sourceResult.TextBlocks, sourceResult.Request.OrientationMode);
        var joinedText = string.Join(
            ' ',
            orderedBlocks
                .Select(block => OcrTextNormalizer.NormalizeForComparison(block.Text))
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        if (string.IsNullOrWhiteSpace(joinedText))
        {
            return sourceResult;
        }

        return new OcrResult(
            sourceResult.Request,
            new[] { new OcrTextBlock(joinedText, CreateCombinedBounds(orderedBlocks)) },
            sourceResult.RecognizedAt);
    }

    private static OcrResult CreateNearbyBlocksResult(
        OcrResult sourceResult,
        OcrZoneTextGroupingSettings textGrouping)
    {
        if (sourceResult.TextBlocks.Count <= 1)
        {
            return sourceResult;
        }

        var isVertical = sourceResult.Request.OrientationMode is OcrOrientationMode.Vertical;
        var mergeDistancePercent = Math.Clamp(
            textGrouping.MergeDistancePercent,
            OcrZoneTextGroupingSettings.MinimumMergeDistancePercent,
            OcrZoneTextGroupingSettings.MaximumMergeDistancePercent);
        var thresholdPixels = Math.Max(sourceResult.InputWidth, sourceResult.InputHeight) * mergeDistancePercent / 100d;
        var groups = ClusterNearbyBlocks(sourceResult.TextBlocks, thresholdPixels)
            .Select(group => OrderBlocksByReadingPosition(group, sourceResult.Request.OrientationMode))
            .OrderBy(group => CreateGroupSortKey(group, isVertical).Primary)
            .ThenBy(group => CreateGroupSortKey(group, isVertical).Secondary)
            .ToArray();
        var groupedBlocks = groups
            .Select(CreateTextBlockFromGroup)
            .Where(block => block is not null)
            .Cast<OcrTextBlock>()
            .ToArray();

        return groupedBlocks.Length == 0
            ? sourceResult
            : new OcrResult(sourceResult.Request, groupedBlocks, sourceResult.RecognizedAt);
    }

    private static IReadOnlyList<IReadOnlyList<OcrTextBlock>> ClusterNearbyBlocks(
        IReadOnlyList<OcrTextBlock> blocks,
        double thresholdPixels)
    {
        var remaining = blocks.ToList();
        var groups = new List<IReadOnlyList<OcrTextBlock>>();

        while (remaining.Count > 0)
        {
            var group = new List<OcrTextBlock> { remaining[0] };
            remaining.RemoveAt(0);

            var added = true;
            while (added)
            {
                added = false;
                for (var index = remaining.Count - 1; index >= 0; index--)
                {
                    var candidate = remaining[index];
                    if (!group.Any(block => CalculateDistance(block.Bounds, candidate.Bounds) <= thresholdPixels))
                    {
                        continue;
                    }

                    group.Add(candidate);
                    remaining.RemoveAt(index);
                    added = true;
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    private static TranslationTextGroupingResult CreateVerticalCjkNearbyBlocksResult(
        OcrResult sourceResult,
        OcrZoneTextGroupingSettings textGrouping)
    {
        if (sourceResult.TextBlocks.Count <= 1)
        {
            return new TranslationTextGroupingResult(sourceResult, sourceResult);
        }

        var mergeDistancePercent = Math.Clamp(
            textGrouping.MergeDistancePercent,
            OcrZoneTextGroupingSettings.MinimumMergeDistancePercent,
            OcrZoneTextGroupingSettings.MaximumMergeDistancePercent);
        var thresholdPixels = Math.Max(sourceResult.InputWidth, sourceResult.InputHeight) * mergeDistancePercent / 100d;
        var semanticGroups = ClusterVerticalCjkBlocks(sourceResult.TextBlocks, thresholdPixels)
            .Select(group => OrderBlocksByReadingPosition(group, sourceResult.Request.OrientationMode))
            .Where(group => IsSemanticVerticalCjkGroup(sourceResult, group))
            .OrderBy(group => CreateGroupSortKey(group, isVertical: true).Primary)
            .ThenBy(group => CreateGroupSortKey(group, isVertical: true).Secondary)
            .ToArray();

        if (semanticGroups.Length == 0)
        {
            return new TranslationTextGroupingResult(sourceResult, sourceResult);
        }

        var translationBlocks = semanticGroups
            .Select(CreateTextBlockFromGroup)
            .Where(block => block is not null)
            .Cast<OcrTextBlock>()
            .ToArray();
        var maskBlocks = semanticGroups
            .SelectMany(group => group)
            .Distinct()
            .ToArray();

        if (translationBlocks.Length == 0 || maskBlocks.Length == 0)
        {
            return new TranslationTextGroupingResult(sourceResult, sourceResult);
        }

        return new TranslationTextGroupingResult(
            new OcrResult(sourceResult.Request, translationBlocks, sourceResult.RecognizedAt),
            new OcrResult(sourceResult.Request, maskBlocks, sourceResult.RecognizedAt));
    }

    private static IReadOnlyList<IReadOnlyList<OcrTextBlock>> ClusterVerticalCjkBlocks(
        IReadOnlyList<OcrTextBlock> blocks,
        double thresholdPixels)
    {
        var remaining = blocks.ToList();
        var groups = new List<IReadOnlyList<OcrTextBlock>>();

        while (remaining.Count > 0)
        {
            var group = new List<OcrTextBlock> { remaining[0] };
            remaining.RemoveAt(0);

            var added = true;
            while (added)
            {
                added = false;
                for (var index = remaining.Count - 1; index >= 0; index--)
                {
                    var candidate = remaining[index];
                    if (!group.Any(block => ShouldMergeVerticalCjkBlocks(block.Bounds, candidate.Bounds, thresholdPixels)))
                    {
                        continue;
                    }

                    group.Add(candidate);
                    remaining.RemoveAt(index);
                    added = true;
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    private static bool ShouldMergeVerticalCjkBlocks(BoundingBox first, BoundingBox second, double thresholdPixels)
    {
        var horizontalOverlap = CalculateOverlap(first.X, first.Right, second.X, second.Right);
        var minimumWidth = Math.Min(first.Width, second.Width);
        var verticalGap = Math.Max(0, Math.Max(first.Y - second.Bottom, second.Y - first.Bottom));
        var sameColumn = horizontalOverlap >= minimumWidth * 0.35
            || Math.Abs(GetCenterX(first) - GetCenterX(second)) <= Math.Max(8d, minimumWidth * 0.75);

        if (sameColumn
            && HaveComparableColumnWidths(first, second)
            && verticalGap <= thresholdPixels * VerticalCjkSameColumnGapMultiplier)
        {
            return true;
        }

        if (IsWideHorizontalNoiseShape(first) || IsWideHorizontalNoiseShape(second))
        {
            return false;
        }

        var horizontalGap = Math.Max(0, Math.Max(first.X - second.Right, second.X - first.Right));
        if (horizontalGap > thresholdPixels)
        {
            return false;
        }

        var verticalOverlap = CalculateOverlap(first.Y, first.Bottom, second.Y, second.Bottom);
        var minimumHeight = Math.Min(first.Height, second.Height);
        return verticalOverlap > 0
            && verticalOverlap >= minimumHeight * VerticalCjkAdjacentColumnOverlapRatio;
    }

    private static bool IsSemanticVerticalCjkGroup(OcrResult sourceResult, IReadOnlyList<OcrTextBlock> blocks)
    {
        var normalizedText = string.Join(
            string.Empty,
            blocks.Select(block => OcrTextNormalizer.NormalizeForComparison(block.Text)));
        var cjkOrDigitCount = normalizedText.Count(character => char.IsDigit(character) || IsCjkCharacter(character));
        if (cjkOrDigitCount < 2)
        {
            return false;
        }

        var bounds = CreateCombinedBounds(blocks);
        if (blocks.Count == 1)
        {
            return bounds.Height >= bounds.Width * VerticalCjkMinimumSemanticHeightToWidthRatio
                && HasVerticalCjkGroupBackground(sourceResult, bounds);
        }

        return bounds.Width <= bounds.Height * VerticalCjkMaximumSemanticWidthToHeightRatio
            && HasVerticalCjkGroupBackground(sourceResult, bounds);
    }

    private static bool HasVerticalCjkGroupBackground(OcrResult sourceResult, BoundingBox bounds)
    {
        if (!TryMeasureLightAndDarkPixelRatios(sourceResult, bounds, out var lightPixelRatio, out var darkPixelRatio))
        {
            return true;
        }

        // Speech bubbles and labels are usually light; halftone/body texture has a larger midtone share.
        var midPixelRatio = Math.Max(0d, 1d - lightPixelRatio - darkPixelRatio);
        return lightPixelRatio >= VerticalCjkMinimumGroupLightPixelRatio
            && midPixelRatio <= VerticalCjkMaximumGroupMidPixelRatio;
    }

    private static bool HaveComparableColumnWidths(BoundingBox first, BoundingBox second)
    {
        var minimumWidth = Math.Max(1, Math.Min(first.Width, second.Width));
        var maximumWidth = Math.Max(first.Width, second.Width);
        return maximumWidth <= minimumWidth * VerticalCjkMaximumColumnWidthRatio
            || (!IsWideHorizontalNoiseShape(first) && !IsWideHorizontalNoiseShape(second));
    }

    private static bool IsWideHorizontalNoiseShape(BoundingBox bounds)
    {
        return bounds.Width >= VerticalCjkWideHorizontalNoiseMinimumWidth
            && bounds.Width >= bounds.Height * VerticalCjkWideHorizontalNoiseRatio;
    }

    private static IReadOnlyList<OcrTextBlock> OrderBlocksByReadingPosition(
        IReadOnlyList<OcrTextBlock> blocks,
        OcrOrientationMode orientationMode)
    {
        if (blocks.Count <= 1)
        {
            return blocks;
        }

        if (orientationMode is OcrOrientationMode.Vertical)
        {
            return OrderVerticalBlocksByReadingPosition(blocks);
        }

        var rows = new List<List<OcrTextBlock>>();
        foreach (var block in blocks.OrderBy(block => GetCenterY(block.Bounds)).ThenBy(block => block.Bounds.X))
        {
            var row = rows.FirstOrDefault(existingRow => IsSameReadingLine(existingRow, block));
            if (row is null)
            {
                rows.Add(new List<OcrTextBlock> { block });
                continue;
            }

            row.Add(block);
        }

        return rows
            .OrderBy(row => row.Min(block => block.Bounds.Y))
            .ThenBy(row => row.Min(block => block.Bounds.X))
            .SelectMany(row => row.OrderBy(block => block.Bounds.X).ThenBy(block => block.Bounds.Y))
            .ToArray();
    }

    private static IReadOnlyList<OcrTextBlock> OrderVerticalBlocksByReadingPosition(IReadOnlyList<OcrTextBlock> blocks)
    {
        var columns = new List<List<OcrTextBlock>>();
        foreach (var block in blocks.OrderByDescending(block => GetCenterX(block.Bounds)).ThenBy(block => block.Bounds.Y))
        {
            var column = columns.FirstOrDefault(existingColumn => IsSameReadingColumn(existingColumn, block));
            if (column is null)
            {
                columns.Add(new List<OcrTextBlock> { block });
                continue;
            }

            column.Add(block);
        }

        return columns
            .OrderByDescending(column => column.Average(block => GetCenterX(block.Bounds)))
            .ThenBy(column => column.Min(block => block.Bounds.Y))
            .SelectMany(column => column.OrderBy(block => block.Bounds.Y).ThenByDescending(block => GetCenterX(block.Bounds)))
            .ToArray();
    }

    private static bool IsSameReadingColumn(IReadOnlyList<OcrTextBlock> column, OcrTextBlock block)
    {
        var columnLeft = column.Min(item => item.Bounds.X);
        var columnRight = column.Max(item => item.Bounds.Right);
        var columnAverageWidth = column.Average(item => item.Bounds.Width);
        var overlap = Math.Min(columnRight, block.Bounds.Right) - Math.Max(columnLeft, block.Bounds.X);
        if (overlap > 0)
        {
            var minimumWidth = Math.Min(columnAverageWidth, block.Bounds.Width);
            if (overlap >= minimumWidth * VerticalReadingColumnOverlapRatio)
            {
                return true;
            }
        }

        var columnCenterX = column.Average(item => GetCenterX(item.Bounds));
        var tolerance = Math.Max(
            8d,
            Math.Max(columnAverageWidth, block.Bounds.Width) * VerticalReadingColumnCenterToleranceFactor);

        return Math.Abs(columnCenterX - GetCenterX(block.Bounds)) <= tolerance;
    }

    private static bool IsSameReadingLine(IReadOnlyList<OcrTextBlock> row, OcrTextBlock block)
    {
        var rowTop = row.Min(item => item.Bounds.Y);
        var rowBottom = row.Max(item => item.Bounds.Bottom);
        var rowAverageHeight = row.Average(item => item.Bounds.Height);
        var overlap = Math.Min(rowBottom, block.Bounds.Bottom) - Math.Max(rowTop, block.Bounds.Y);
        if (overlap > 0)
        {
            var minimumHeight = Math.Min(rowAverageHeight, block.Bounds.Height);
            if (overlap >= minimumHeight * SameReadingLineOverlapRatio)
            {
                return true;
            }
        }

        var rowCenterY = row.Average(item => GetCenterY(item.Bounds));
        var tolerance = Math.Max(
            2d,
            Math.Max(rowAverageHeight, block.Bounds.Height) * SameReadingLineCenterToleranceFactor);

        return Math.Abs(rowCenterY - GetCenterY(block.Bounds)) <= tolerance;
    }

    private static OcrTextBlock? CreateTextBlockFromGroup(IReadOnlyList<OcrTextBlock> blocks)
    {
        var joinedText = string.Join(
            ' ',
            blocks
                .Select(block => OcrTextNormalizer.NormalizeForComparison(block.Text))
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        return string.IsNullOrWhiteSpace(joinedText)
            ? null
            : new OcrTextBlock(joinedText, CreateCombinedBounds(blocks));
    }

    private static double CalculateDistance(BoundingBox first, BoundingBox second)
    {
        var horizontalGap = Math.Max(0, Math.Max(first.X - second.Right, second.X - first.Right));
        var verticalGap = Math.Max(0, Math.Max(first.Y - second.Bottom, second.Y - first.Bottom));

        return Math.Sqrt((horizontalGap * horizontalGap) + (verticalGap * verticalGap));
    }

    private static double GetCenterY(BoundingBox bounds)
    {
        return bounds.Y + bounds.Height / 2d;
    }

    private static double GetCenterX(BoundingBox bounds)
    {
        return bounds.X + bounds.Width / 2d;
    }

    private static int CalculateOverlap(int firstStart, int firstEnd, int secondStart, int secondEnd)
    {
        return Math.Max(0, Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart));
    }

    private static (double Primary, double Secondary) CreateGroupSortKey(IReadOnlyList<OcrTextBlock> group, bool isVertical)
    {
        var bounds = CreateCombinedBounds(group);

        return isVertical
            ? (-GetCenterX(bounds), bounds.Y)
            : (bounds.Y, bounds.X);
    }

    private static OcrResult CreateMaskSourceResult(OcrResult sourceResult)
    {
        if (!ShouldApplyVerticalCjkNoiseFilter(sourceResult))
        {
            return sourceResult;
        }

        var filteredBlocks = sourceResult.TextBlocks
            .Where(block => ShouldKeepVerticalCjkBlock(sourceResult, block))
            .ToArray();

        return filteredBlocks.Length == 0 || filteredBlocks.Length == sourceResult.TextBlocks.Count
            ? sourceResult
            : new OcrResult(sourceResult.Request, filteredBlocks, sourceResult.RecognizedAt);
    }

    private static bool ShouldApplyVerticalCjkNoiseFilter(OcrResult sourceResult)
    {
        return sourceResult.Request.OrientationMode is OcrOrientationMode.Vertical
            && sourceResult.TextBlocks.Count > 1
            && (IsCjkOcrLanguage(sourceResult.Language) || sourceResult.TextBlocks.Any(block => ContainsCjkOrDigit(block.Text)));
    }

    private static bool ShouldUseVerticalCjkSemanticGrouping(OcrResult sourceResult)
    {
        return sourceResult.Request.OrientationMode is OcrOrientationMode.Vertical
            && sourceResult.TextBlocks.Count > 1
            && (IsCjkOcrLanguage(sourceResult.Language) || sourceResult.TextBlocks.Any(block => ContainsCjkOrDigit(block.Text)));
    }

    private static bool ShouldKeepVerticalCjkBlock(OcrResult sourceResult, OcrTextBlock block)
    {
        var normalizedText = OcrTextNormalizer.NormalizeForComparison(block.Text);
        if (!ContainsCjkOrDigit(normalizedText))
        {
            return false;
        }

        return !TryMeasureLightAndDarkPixelRatios(sourceResult, block.Bounds, out var lightPixelRatio, out var darkPixelRatio)
            || (lightPixelRatio >= VerticalCjkMinimumLightPixelRatio
                && darkPixelRatio <= VerticalCjkMaximumDarkPixelRatio);
    }

    private static bool TryMeasureLightAndDarkPixelRatios(
        OcrResult sourceResult,
        BoundingBox bounds,
        out double lightPixelRatio,
        out double darkPixelRatio)
    {
        lightPixelRatio = 0d;
        darkPixelRatio = 0d;

        var frame = sourceResult.Request.Frame;
        if (!string.Equals(frame.PixelFormat, "Bgra32", StringComparison.OrdinalIgnoreCase)
            || frame.Stride < frame.Width * 4)
        {
            return false;
        }

        var left = Math.Max(0, bounds.X - VerticalCjkNoiseSamplePadding);
        var top = Math.Max(0, bounds.Y - VerticalCjkNoiseSamplePadding);
        var right = Math.Min(frame.Width, bounds.Right + VerticalCjkNoiseSamplePadding);
        var bottom = Math.Min(frame.Height, bounds.Bottom + VerticalCjkNoiseSamplePadding);
        if (right <= left || bottom <= top)
        {
            return false;
        }

        var pixels = frame.PixelData.Span;
        var lightPixels = 0;
        var darkPixels = 0;
        var sampledPixels = 0;
        for (var y = top; y < bottom; y += VerticalCjkNoiseSampleStep)
        {
            var rowOffset = y * frame.Stride;
            for (var x = left; x < right; x += VerticalCjkNoiseSampleStep)
            {
                var offset = rowOffset + x * 4;
                if (offset + 2 >= pixels.Length)
                {
                    continue;
                }

                var blue = pixels[offset];
                var green = pixels[offset + 1];
                var red = pixels[offset + 2];
                var luminance = red * 0.2126d + green * 0.7152d + blue * 0.0722d;
                if (luminance >= LightLuminanceThreshold)
                {
                    lightPixels++;
                }
                else if (luminance <= DarkLuminanceThreshold)
                {
                    darkPixels++;
                }

                sampledPixels++;
            }
        }

        if (sampledPixels == 0)
        {
            return false;
        }

        lightPixelRatio = lightPixels / (double)sampledPixels;
        darkPixelRatio = darkPixels / (double)sampledPixels;
        return true;
    }

    private static bool IsCjkOcrLanguage(string language)
    {
        return language.Contains("zh", StringComparison.OrdinalIgnoreCase)
            || language.Contains("chi", StringComparison.OrdinalIgnoreCase)
            || language.Contains("ja", StringComparison.OrdinalIgnoreCase)
            || language.Contains("jpn", StringComparison.OrdinalIgnoreCase)
            || language.Contains("ko", StringComparison.OrdinalIgnoreCase)
            || language.Contains("kor", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsCjkOrDigit(string text)
    {
        return text.Any(character => char.IsDigit(character) || IsCjkCharacter(character));
    }

    private static bool IsCjkCharacter(char character)
    {
        return character is
            (>= '\u3400' and <= '\u4dbf')
            or (>= '\u4e00' and <= '\u9fff')
            or (>= '\uf900' and <= '\ufaff')
            or (>= '\u3040' and <= '\u30ff')
            or (>= '\u31f0' and <= '\u31ff')
            or (>= '\uac00' and <= '\ud7af');
    }

    private static BoundingBox CreateCombinedBounds(IReadOnlyList<OcrTextBlock> blocks)
    {
        var left = blocks.Min(block => block.Bounds.X);
        var top = blocks.Min(block => block.Bounds.Y);
        var right = blocks.Max(block => block.Bounds.Right);
        var bottom = blocks.Max(block => block.Bounds.Bottom);

        return new BoundingBox(left, top, right - left, bottom - top);
    }
}
