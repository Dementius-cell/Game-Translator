using GameTranslator.Application.Capture;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Ocr;

internal static class MenuRecognitionContext
{
    internal static CapturedFrame? Create(OcrRequest request, TextCandidate candidate,
        IReadOnlyList<TextCandidate> neighbors, CancellationToken token)
    {
        var frame = request.Frame;
        var bounds = candidate.Bounds;
        var lines = candidate.SourceCandidateBounds.OrderBy(b => b.Y).ToArray();
        // This is source-pixel context for a clipped description, not a filter or
        // a change to detector identity. Textured borders retain the original crop.
        if (request.ContentLayoutMode != ContentLayoutMode.StaticMenu
            || request.OrientationMode != OcrOrientationMode.Horizontal
            || request.PreprocessingSettings.ResolveMode() != OcrPreprocessingMode.None
            || request.WatermarkFilter != WatermarkFilterMode.Off
            || frame.PixelFormat != "Bgra32" || lines.Length is < 1 or > 8
            || (lines.Length == 1 && bounds.Width < bounds.Height * 12)
            || (long)bounds.Width * bounds.Height > 1_000_000
            || lines.Any(b => b.Width < b.Height * 3)
            || lines.Zip(lines.Skip(1)).Any(pair => pair.First.Bottom > pair.Second.Y)) return null;

        var height = lines.Min(b => b.Height);
        if (lines.Max(b => b.Height) > height * 2) return null;
        // A nearby icon may contaminate the largest margin while a smaller one
        // still encloses the complete glyph. Select pixels before the sole OCR pass.
        for (var padding = (int)Math.Ceiling(height * .15); padding >= 1; padding--)
        {
            var context = TryCreateContext(frame, candidate, neighbors, lines.Length, height, padding, token);
            if (context is not null) return context;
            if (lines.Length > 1) break;
        }
        return null;
    }

    private static CapturedFrame? TryCreateContext(CapturedFrame frame, TextCandidate candidate,
        IReadOnlyList<TextCandidate> neighbors, int lineCount, int height, int padding, CancellationToken token)
    {
        var bounds = candidate.Bounds;
        var left = Math.Max(0, bounds.X - padding);
        var top = Math.Max(0, bounds.Y - padding);
        var right = Math.Min(frame.Width, bounds.Right + padding);
        var bottom = Math.Min(frame.Height, bounds.Bottom + padding);
        var expanded = new BoundingBox(left, top, right - left, bottom - top);
        if (expanded == bounds || neighbors.Any(n => !ReferenceEquals(n, candidate)
            && n.Bounds.X < expanded.Right && n.Bounds.Right > expanded.X
            && n.Bounds.Y < expanded.Bottom && n.Bounds.Bottom > expanded.Y)) return null;

        // A clipped single line can have dense ink along its original edge.
        // Require a quiet outer border and actual ink crossing the detector edge.
        var border = lineCount == 1 ? expanded : bounds;
        var band = lineCount == 1 ? 1 : Math.Max(1, (int)Math.Ceiling(height * .08));
        var histogram = new int[256];
        int count = 0;
        var pixels = frame.PixelData.Span;
        for (var y = border.Y; y < border.Bottom; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = border.X; x < border.Right; x++)
            {
                if (x >= border.X + band && x < border.Right - band
                    && y >= border.Y + band && y < border.Bottom - band) continue;
                var offset = y * frame.Stride + x * 4;
                var luminance = (int)(pixels[offset] * .0722 + pixels[offset + 1] * .7152 + pixels[offset + 2] * .2126);
                histogram[luminance]++;
                count++;
            }
        }
        var low = Percentile(histogram, count, .1);
        var high = Percentile(histogram, count, .9);
        if (high > 64 || high - low > 8) return null;
        // Do not introduce a clipped neighbor/icon or another clipped glyph at
        // the new edge; a single-line context must end in background pixels.
        if (lineCount == 1 && histogram.Skip(high + 32).Any(n => n > 0)) return null;
        if (lineCount == 1
            ? !InkCrossesBoundary(frame, bounds, high + 32, token)
            : !histogram.Skip(high + 32).Any(n => n > 0)) return null;
        return TextCandidateRegionOcrService.CreateCroppedFrame(frame, expanded);
    }

    private static bool InkCrossesBoundary(CapturedFrame frame, BoundingBox bounds, int threshold, CancellationToken token)
    {
        var pixels = frame.PixelData.Span;
        for (var x = bounds.X; x < bounds.Right; x++)
        {
            token.ThrowIfCancellationRequested();
            var top = bounds.Y * frame.Stride + x * 4;
            var bottom = (bounds.Bottom - 1) * frame.Stride + x * 4;
            if ((bounds.Y > 0 && IsInk(pixels, top, threshold) && IsInk(pixels, top - frame.Stride, threshold))
                || (bounds.Bottom < frame.Height && IsInk(pixels, bottom, threshold) && IsInk(pixels, bottom + frame.Stride, threshold))) return true;
        }
        for (var y = bounds.Y; y < bounds.Bottom; y++)
        {
            token.ThrowIfCancellationRequested();
            var left = y * frame.Stride + bounds.X * 4;
            var right = y * frame.Stride + (bounds.Right - 1) * 4;
            if ((bounds.X > 0 && IsInk(pixels, left, threshold) && IsInk(pixels, left - 4, threshold))
                || (bounds.Right < frame.Width && IsInk(pixels, right, threshold) && IsInk(pixels, right + 4, threshold))) return true;
        }
        return false;
    }

    private static bool IsInk(ReadOnlySpan<byte> pixels, int offset, int threshold) =>
        pixels[offset] * .0722 + pixels[offset + 1] * .7152 + pixels[offset + 2] * .2126 >= threshold;

    private static int Percentile(int[] histogram, int count, double fraction)
    {
        var rank = Math.Max(1, (int)Math.Ceiling(count * fraction));
        for (var value = 0; value < histogram.Length; value++)
            if ((rank -= histogram[value]) <= 0) return value;
        return 255;
    }
}
