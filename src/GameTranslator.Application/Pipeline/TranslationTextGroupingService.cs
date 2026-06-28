using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Pipeline;

public static class TranslationTextGroupingService
{
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

        var orderedBlocks = sourceResult.TextBlocks
            .OrderBy(block => block.Bounds.Y)
            .ThenBy(block => block.Bounds.X)
            .ToArray();
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

        var mergeDistancePercent = Math.Clamp(
            textGrouping.MergeDistancePercent,
            OcrZoneTextGroupingSettings.MinimumMergeDistancePercent,
            OcrZoneTextGroupingSettings.MaximumMergeDistancePercent);
        var thresholdPixels = Math.Max(sourceResult.InputWidth, sourceResult.InputHeight) * mergeDistancePercent / 100d;
        var groups = ClusterNearbyBlocks(sourceResult.TextBlocks, thresholdPixels)
            .Select(OrderBlocksByReadingPosition)
            .OrderBy(group => CreateCombinedBounds(group).Y)
            .ThenBy(group => CreateCombinedBounds(group).X)
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

    private static IReadOnlyList<OcrTextBlock> OrderBlocksByReadingPosition(IReadOnlyList<OcrTextBlock> blocks)
    {
        return blocks
            .OrderBy(block => block.Bounds.Y)
            .ThenBy(block => block.Bounds.X)
            .ToArray();
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

    private static BoundingBox CreateCombinedBounds(IReadOnlyList<OcrTextBlock> blocks)
    {
        var left = blocks.Min(block => block.Bounds.X);
        var top = blocks.Min(block => block.Bounds.Y);
        var right = blocks.Max(block => block.Bounds.Right);
        var bottom = blocks.Max(block => block.Bounds.Bottom);

        return new BoundingBox(left, top, right - left, bottom - top);
    }
}
