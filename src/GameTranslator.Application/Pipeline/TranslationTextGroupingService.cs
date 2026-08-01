using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Pipeline;

public static class TranslationTextGroupingService
{
    private const double SameReadingLineCenterToleranceFactor = 0.8;
    private const double SameReadingLineOverlapRatio = 0.15;
    private const double AdaptiveMergeMinorSideMultiplier = 1.75;
    private const double MinimumAdaptiveMergeThresholdPixels = 18;

    public static OcrResult CreateTranslationSourceResult(OcrResult sourceResult, OcrZone zone)
    {
        ArgumentNullException.ThrowIfNull(sourceResult);
        ArgumentNullException.ThrowIfNull(zone);

        return zone.TranslationGroupingMode switch
        {
            TranslationGroupingMode.WholeZone => CreateWholeZoneResult(sourceResult),
            TranslationGroupingMode.NearbyBlocks => CreateNearbyBlocksResult(sourceResult, zone.TextGrouping ?? OcrZoneTextGroupingSettings.Default),
            _ => sourceResult,
        };
    }

    private static OcrResult CreateWholeZoneResult(OcrResult sourceResult)
    {
        if (sourceResult.TextBlocks.Count <= 1)
        {
            return sourceResult;
        }

        var orderedBlocks = OrderBlocksByReadingPosition(sourceResult.TextBlocks);
        var source = CreateSourceFromGroup(sourceResult, orderedBlocks);
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
            new[] { new OcrTextBlock(joinedText, source.SemanticBounds) },
            sourceResult.RecognizedAt,
            new[] { source },
            sourceResult.Words);
    }

    private static OcrResult CreateNearbyBlocksResult(
        OcrResult sourceResult,
        OcrZoneTextGroupingSettings textGrouping)
    {
        if (sourceResult.TextBlocks.Count <= 1)
        {
            return sourceResult;
        }

        var mergeDistancePercent = Math.Clamp(
            textGrouping.MergeDistancePercent,
            OcrZoneTextGroupingSettings.MinimumMergeDistancePercent,
            OcrZoneTextGroupingSettings.MaximumMergeDistancePercent);
        var requestedThresholdPixels = Math.Max(sourceResult.InputWidth, sourceResult.InputHeight) * mergeDistancePercent / 100d;
        var thresholdPixels = Math.Min(
            requestedThresholdPixels,
            CalculateAdaptiveMergeThreshold(sourceResult.TextBlocks));
        var groups = OrderGroupsByReadingPosition(
            ClusterNearbyBlocks(sourceResult.TextBlocks, thresholdPixels)
                .Select(OrderBlocksByReadingPosition)
                .ToArray());
        var groupedBlocks = groups
            .Select(group => CreateTextBlockFromGroup(sourceResult, group))
            .Where(group => group is not null)
            .Cast<TranslationTextGroup>()
            .ToArray();

        return groupedBlocks.Length == 0
            ? sourceResult
            : new OcrResult(
                sourceResult.Request,
                groupedBlocks.Select(group => group.TextBlock),
                sourceResult.RecognizedAt,
                groupedBlocks.Select(group => group.Source),
                sourceResult.Words);
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

    private static IReadOnlyList<OcrTextBlock> OrderBlocksByReadingPosition(IReadOnlyList<OcrTextBlock> blocks)
    {
        if (blocks.Count <= 1)
        {
            return blocks;
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

    private static IReadOnlyList<IReadOnlyList<OcrTextBlock>> OrderGroupsByReadingPosition(
        IReadOnlyList<IReadOnlyList<OcrTextBlock>> groups)
    {
        if (groups.Count <= 1)
        {
            return groups;
        }

        var rows = new List<List<IReadOnlyList<OcrTextBlock>>>();
        foreach (var group in groups.OrderBy(group => CreateCombinedBounds(group).Y).ThenBy(group => CreateCombinedBounds(group).X))
        {
            var row = rows.FirstOrDefault(existingRow => IsSameGroupReadingLine(existingRow, group));
            if (row is null)
            {
                rows.Add(new List<IReadOnlyList<OcrTextBlock>> { group });
                continue;
            }

            row.Add(group);
        }

        return rows
            .OrderBy(row => row.Min(group => CreateCombinedBounds(group).Y))
            .ThenBy(row => row.Min(group => CreateCombinedBounds(group).X))
            .SelectMany(row => row.OrderBy(group => CreateCombinedBounds(group).X).ThenBy(group => CreateCombinedBounds(group).Y))
            .ToArray();
    }

    private static bool IsSameGroupReadingLine(
        IReadOnlyList<IReadOnlyList<OcrTextBlock>> row,
        IReadOnlyList<OcrTextBlock> group)
    {
        var rowBounds = row.Select(CreateCombinedBounds).ToArray();
        var groupBounds = CreateCombinedBounds(group);
        var rowTop = rowBounds.Min(bounds => bounds.Y);
        var rowBottom = rowBounds.Max(bounds => bounds.Bottom);
        var rowAverageHeight = rowBounds.Average(bounds => bounds.Height);
        var overlap = Math.Min(rowBottom, groupBounds.Bottom) - Math.Max(rowTop, groupBounds.Y);
        if (overlap > 0)
        {
            var minimumHeight = Math.Min(rowAverageHeight, groupBounds.Height);
            if (overlap >= minimumHeight * SameReadingLineOverlapRatio)
            {
                return true;
            }
        }

        var rowCenterY = rowBounds.Average(GetCenterY);
        var tolerance = Math.Max(
            2d,
            Math.Max(rowAverageHeight, groupBounds.Height) * SameReadingLineCenterToleranceFactor);

        return Math.Abs(rowCenterY - GetCenterY(groupBounds)) <= tolerance;
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

    private static TranslationTextGroup? CreateTextBlockFromGroup(
        OcrResult sourceResult,
        IReadOnlyList<OcrTextBlock> blocks)
    {
        var joinedText = string.Join(
            ' ',
            blocks
                .Select(block => OcrTextNormalizer.NormalizeForComparison(block.Text))
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        if (string.IsNullOrWhiteSpace(joinedText))
        {
            return null;
        }

        var source = CreateSourceFromGroup(sourceResult, blocks);
        return new TranslationTextGroup(
            new OcrTextBlock(joinedText, source.SemanticBounds),
            source);
    }

    private static OcrTextBlockSource CreateSourceFromGroup(
        OcrResult sourceResult,
        IReadOnlyList<OcrTextBlock> blocks)
    {
        var sources = blocks
            .Select(block => GetSourceForBlock(sourceResult, block))
            .ToArray();
        var memberBounds = sources
            .SelectMany(source => source.MemberBounds)
            .ToArray();
        var semanticBounds = CreateCombinedBounds(memberBounds);

        return new OcrTextBlockSource(
            semanticBounds,
            memberBounds,
            ResolveGroupOrientation(sourceResult, sources));
    }

    private static OcrTextBlockSource GetSourceForBlock(OcrResult sourceResult, OcrTextBlock block)
    {
        for (var index = 0; index < sourceResult.TextBlocks.Count; index++)
        {
            if (ReferenceEquals(sourceResult.TextBlocks[index], block))
            {
                return sourceResult.TextBlockSources[index];
            }
        }

        return new OcrTextBlockSource(
            block.Bounds,
            new[] { block.Bounds },
            sourceResult.Request.OrientationMode);
    }

    private static OcrOrientationMode ResolveGroupOrientation(
        OcrResult sourceResult,
        IReadOnlyList<OcrTextBlockSource> sources)
    {
        if (sources.Any(source => source.OrientationMode is OcrOrientationMode.Vertical))
        {
            return OcrOrientationMode.Vertical;
        }

        if (sources.Count > 0 && sources.All(source => source.OrientationMode is OcrOrientationMode.Horizontal))
        {
            return OcrOrientationMode.Horizontal;
        }

        return OcrOrientationMode.Auto;
    }

    private static double CalculateDistance(BoundingBox first, BoundingBox second)
    {
        var horizontalGap = Math.Max(0, Math.Max(first.X - second.Right, second.X - first.Right));
        var verticalGap = Math.Max(0, Math.Max(first.Y - second.Bottom, second.Y - first.Bottom));

        return Math.Sqrt((horizontalGap * horizontalGap) + (verticalGap * verticalGap));
    }

    private static double CalculateAdaptiveMergeThreshold(IReadOnlyList<OcrTextBlock> blocks)
    {
        var minorSides = blocks
            .Select(block => Math.Min(block.Bounds.Width, block.Bounds.Height))
            .Where(side => side > 0)
            .Order()
            .ToArray();
        if (minorSides.Length == 0)
        {
            return MinimumAdaptiveMergeThresholdPixels;
        }

        var median = minorSides.Length % 2 == 1
            ? minorSides[minorSides.Length / 2]
            : (minorSides[minorSides.Length / 2 - 1] + minorSides[minorSides.Length / 2]) / 2d;

        return Math.Max(
            MinimumAdaptiveMergeThresholdPixels,
            median * AdaptiveMergeMinorSideMultiplier);
    }

    private static double GetCenterY(BoundingBox bounds)
    {
        return bounds.Y + bounds.Height / 2d;
    }

    private static BoundingBox CreateCombinedBounds(IReadOnlyList<OcrTextBlock> blocks)
    {
        var left = blocks.Min(block => block.Bounds.X);
        var top = blocks.Min(block => block.Bounds.Y);
        var right = blocks.Max(block => block.Bounds.Right);
        var bottom = blocks.Max(block => block.Bounds.Bottom);

        return new BoundingBox(left, top, right - left, bottom - top);
    }

    private static BoundingBox CreateCombinedBounds(IReadOnlyList<BoundingBox> bounds)
    {
        var left = bounds.Min(bound => bound.X);
        var top = bounds.Min(bound => bound.Y);
        var right = bounds.Max(bound => bound.Right);
        var bottom = bounds.Max(bound => bound.Bottom);

        return new BoundingBox(left, top, right - left, bottom - top);
    }

    private sealed record TranslationTextGroup(OcrTextBlock TextBlock, OcrTextBlockSource Source);
}
