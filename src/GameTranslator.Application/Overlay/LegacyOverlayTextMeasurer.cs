using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Overlay;

internal sealed class LegacyOverlayTextMeasurer : IOverlayTextMeasurer
{
    private const double AverageGlyphWidthFactor = 0.62;
    private const double BoldGlyphWidthFactor = 0.68;
    private const double LineHeightFactor = 1.45;

    public static LegacyOverlayTextMeasurer Instance { get; } = new();

    public OverlayTextMeasurement Measure(OverlayTextMeasurementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fontSize = Math.Clamp(
            request.TextStyle.FontSize,
            OcrZoneTextStyle.MinimumFontSize,
            OcrZoneTextStyle.MaximumFontSize);
        var glyphWidthFactor = request.TextStyle.IsBold
            ? BoldGlyphWidthFactor
            : AverageGlyphWidthFactor;
        var lineCount = EstimateWrappedLineCount(
            request.Text,
            request.MaxWidth,
            fontSize,
            glyphWidthFactor);
        var width = Math.Min(
            request.MaxWidth,
            Math.Max(1, (int)Math.Ceiling(EstimateTextWidth(request.Text, fontSize, glyphWidthFactor))));
        var lineHeight = Math.Max(1, (int)Math.Ceiling(fontSize * LineHeightFactor));
        var height = Math.Max(1, lineHeight * lineCount);

        return new OverlayTextMeasurement(
            width,
            height,
            Enumerable.Range(0, lineCount)
                .Select(_ => new OverlayTextLineMeasurement(width, lineHeight, request.Text.Length, hasOverflowed: false)));
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
            return Math.Max(
                1,
                (int)Math.Ceiling(EstimateTextWidth(normalizedText, fontSize, glyphWidthFactor) / maxContentWidth));
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
}
