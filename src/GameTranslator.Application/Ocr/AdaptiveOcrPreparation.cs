using GameTranslator.Application.Capture;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Ocr;

// Per-crop pixel analysis only. Never inherits manual filters or retries another engine.
internal static class AdaptiveOcrPreparation
{
    internal static OcrPreprocessingSettings Select(CapturedFrame frame, IReadOnlyList<BoundingBox> lines, CancellationToken token,
        bool allowBrightMenuText = false)
    {
        if (frame.PixelFormat != "Bgra32") return OcrPreprocessingSettings.Default;
        var pixels = frame.PixelData.Span;
        var border = new List<double>();
        double interiorSum = 0;
        int count = 0, colored = 0, nearWhite = 0;
        var histogram = allowBrightMenuText ? new int[256] : null;
        var step = Math.Max(1, Math.Max(frame.Width, frame.Height) / 128);
        for (var y = 0; y < frame.Height; y += step)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < frame.Width; x += step)
            {
                var offset = y * frame.Stride + x * 4;
                int b = pixels[offset], g = pixels[offset + 1], r = pixels[offset + 2];
                var luminance = b * .0722 + g * .7152 + r * .2126;
                interiorSum += luminance;
                count++;
                if (luminance >= 245) nearWhite++;
                if (histogram is not null) histogram[Math.Clamp((int)luminance, 0, 255)]++;
                if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > 45) colored++;
                if (x < step || y < step || x >= frame.Width - step || y >= frame.Height - step) border.Add(luminance);
            }
        }
        border.Sort();
        var low = border[border.Count / 10];
        var high = border[border.Count * 9 / 10];
        var invert = high < 85 && interiorSum / count > high + 10;
        var grayscale = colored < count * .05;
        var adaptive = grayscale && high - low > 45 && (long)frame.Width * frame.Height <= SauvolaThreshold.MaximumPixels;
        // Bright glyph cores over a colored scene need a different polarity/threshold
        // than ordinary UI text. Restrict this to a single compact horizontal menu crop.
        if (histogram is not null && lines.Count == 1
            && frame.Width >= frame.Height * 1.5 && frame.Width <= frame.Height * 12
            && nearWhite >= count * .01 && nearWhite <= count * .25 && colored >= count * .25
            && Percentile(histogram, count, .9) - Percentile(histogram, count, .1) >= 80)
        {
            return new OcrPreprocessingSettings
            {
                IsEnabled = true, Mode = OcrPreprocessingMode.Manual,
                InvertColors = true, ThresholdingEnabled = true, Threshold = 10,
                AddWhiteBorder = true,
            };
        }
        var sizes = lines.Where(b => Math.Max(b.Width, b.Height) >= Math.Min(b.Width, b.Height) * 2)
            .Select(b => Math.Min(b.Width, b.Height)).Order().ToArray();
        var size = sizes.Length > 0 ? sizes[sizes.Length / 2] : 32;
        var scale = size < 20 ? 1.5 : size > 96 ? .5 : 1d;
        if ((long)frame.Width * frame.Height * scale * scale > 4_000_000) scale = 1;
        return new OcrPreprocessingSettings
        {
            IsEnabled = true, Mode = OcrPreprocessingMode.Manual,
            InvertColors = invert, AdaptiveThresholding = adaptive,
            Scale = scale, AddWhiteBorder = lines.Count > 0,
        };
    }

    private static int Percentile(int[] histogram, int count, double fraction)
    {
        var rank = Math.Max(1, (int)Math.Ceiling(count * fraction));
        for (var value = 0; value < histogram.Length; value++)
        {
            rank -= histogram[value];
            if (rank <= 0) return value;
        }
        return 255;
    }
}
