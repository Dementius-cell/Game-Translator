using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;

namespace GameTranslator.Infrastructure.Ocr;

// A single-pass recognition preparation, never evidence that displayed text is unchanged.
internal static class MenuChromaticTexture
{
    internal static CapturedFrame? TryCreate(OcrRequest request, CancellationToken token = default)
    {
        if (!MenuRecognitionBorder.CanPrepareSingleLine(request)) return null;
        var frame = request.Frame;
        var count = frame.Width * frame.Height;
        var minimum = new byte[count];
        var chroma = new byte[count];
        var minimumHistogram = new int[256];
        var chromaHistogram = new int[256];
        var borderHistogram = new int[256];
        var borderCount = 0;
        var source = frame.PixelData.Span;
        for (var y = 0; y < frame.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < frame.Width; x++)
            {
                var offset = y * frame.Stride + x * 4;
                var low = Math.Min(source[offset], Math.Min(source[offset + 1], source[offset + 2]));
                var high = Math.Max(source[offset], Math.Max(source[offset + 1], source[offset + 2]));
                var i = y * frame.Width + x;
                minimum[i] = low;
                chroma[i] = (byte)(high - low);
                minimumHistogram[low]++;
                chromaHistogram[high - low]++;
                if (x == 0 || y == 0 || x == frame.Width - 1 || y == frame.Height - 1)
                {
                    borderHistogram[low]++;
                    borderCount++;
                }
            }
        }
        var level = Quantile(chromaHistogram, count, .9);
        // A prominent colored mark (including punctuation) is not background texture.
        if (level == 0 || chromaHistogram.Skip(level * 2 + 1).Any(n => n > 0)) return null;
        var core = Quantile(minimumHistogram, count, .99);
        var lowLevel = Quantile(minimumHistogram, count, .1);
        if (Quantile(borderHistogram, borderCount, .5) >= (lowLevel + core) / 2d) return null;
        var coreCount = 0;
        var neutralCount = 0;
        var coloredCount = 0;
        var threshold = level * .8;
        for (var i = 0; i < count; i++)
        {
            if (minimum[i] >= core)
            {
                coreCount++;
                if (chroma[i] <= (minimum[i] + chroma[i]) * .2) neutralCount++;
            }
            if (chroma[i] > threshold) coloredCount++;
        }
        if (neutralCount < coreCount * .95 || coloredCount < count * .08 || coloredCount > count * .4)
            return null;

        var queue = new int[count];
        var components = 0;
        for (var start = 0; start < count; start++)
        {
            if (chroma[start] <= threshold) continue;
            token.ThrowIfCancellationRequested();
            components++;
            var head = 0;
            var tail = 1;
            queue[0] = start;
            chroma[start] = 0;
            var left = start % frame.Width;
            var right = left;
            var top = start / frame.Width;
            var bottom = top;
            while (head < tail)
            {
                var next = queue[head++];
                var x = next % frame.Width;
                var y = next / frame.Width;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                // Long colored strokes may be actual letters, not the small texture cells.
                if (right - left + 1 > frame.Height * .25 || bottom - top + 1 > frame.Height * .25)
                    return null;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || nx >= frame.Width || ny < 0 || ny >= frame.Height) continue;
                    var neighbor = ny * frame.Width + nx;
                    if (chroma[neighbor] <= threshold) continue;
                    chroma[neighbor] = 0;
                    queue[tail++] = neighbor;
                }
            }
        }
        if (components < 8) return null;
        var pixels = frame.PixelData.ToArray();
        for (var y = 0; y < frame.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < frame.Width; x++)
            {
                var offset = y * frame.Stride + x * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = minimum[y * frame.Width + x];
            }
        }
        return new(frame.Region, frame.Width, frame.Height, frame.Stride, frame.PixelFormat,
            pixels, frame.CapturedAt, frame.Origin, frame.IsCachedReuse);
    }

    private static int Quantile(int[] histogram, int count, double fraction)
    {
        var rank = (int)Math.Ceiling(count * fraction);
        for (var i = 0; i < histogram.Length; i++)
        {
            rank -= histogram[i];
            if (rank <= 0) return i;
        }
        return 255;
    }
}
