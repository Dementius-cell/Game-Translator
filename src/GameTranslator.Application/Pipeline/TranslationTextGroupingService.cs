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

    private static BoundingBox CreateCombinedBounds(IReadOnlyList<OcrTextBlock> blocks)
    {
        var left = blocks.Min(block => block.Bounds.X);
        var top = blocks.Min(block => block.Bounds.Y);
        var right = blocks.Max(block => block.Bounds.Right);
        var bottom = blocks.Max(block => block.Bounds.Bottom);

        return new BoundingBox(left, top, right - left, bottom - top);
    }
}
