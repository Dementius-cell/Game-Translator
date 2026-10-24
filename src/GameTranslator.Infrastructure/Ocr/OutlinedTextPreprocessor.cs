using GameTranslator.Application.Capture;

namespace GameTranslator.Infrastructure.Ocr;

// A bounded recovery for detached glyphs surrounded by a thin outline of the same color.
// No font, language, expected text or specific foreground color is assumed.
internal static class OutlinedTextPreprocessor
{
    internal const int MaximumPixels = 1_000_000;

    internal static CapturedFrame? TryPrepare(CapturedFrame frame, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var width = frame.Width;
        var height = frame.Height;
        if (width < 60 || height < 24 || width < (long)height * 2
            || (long)width * height > MaximumPixels || frame.PixelFormat != "Bgra32")
            return null;

        var pixels = frame.PixelData.Span;
        var histogram = new int[32768];
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var offset = y * frame.Stride + x * 4;
                int blue = pixels[offset], green = pixels[offset + 1], red = pixels[offset + 2];
                if (Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) < 60)
                    continue;
                histogram[(red >> 3) * 1024 + (green >> 3) * 32 + (blue >> 3)]++;
            }
        }

        var colors = Enumerable.Range(0, histogram.Length)
            .Where(index => histogram[index] > width * height * .015)
            .OrderByDescending(index => histogram[index]).Take(4);
        foreach (var color in colors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var red = color / 1024 * 8 + 4;
            var green = color / 32 % 32 * 8 + 4;
            var blue = color % 32 * 8 + 4;
            var labels = new int[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var offset = y * frame.Stride + x * 4;
                labels[y * width + x] = Math.Abs(pixels[offset] - blue) <= 45
                    && Math.Abs(pixels[offset + 1] - green) <= 45
                    && Math.Abs(pixels[offset + 2] - red) <= 45 ? -1 : 0;
            }

            var components = LabelComponents(labels, width, height, cancellationToken);
            foreach (var outline in components.Where(component => component.Width > width * .7
                && component.Height > height * .6
                && (double)component.Count / (component.Width * component.Height) < .35))
            {
                var interior = components.Where(component => component.Id != outline.Id
                    && component.X > outline.X && component.Right < outline.Right
                    && component.Y > outline.Y && component.Bottom < outline.Bottom).ToArray();
                var glyphs = interior.Where(component => component.Height > height * .25
                    && component.Width < width * .3 && component.Count > height * 2).ToArray();
                if (glyphs.Length < 3 || glyphs.Sum(component => component.Count) < width * height * .08
                    || glyphs.Max(component => component.Right) - glyphs.Min(component => component.X) < width * .6)
                    continue;

                // Small interior marks may be dots, accents or punctuation; retain them too.
                var keep = interior.Select(component => component.Id).ToHashSet();
                var result = new byte[width * height * 4];
                for (var index = 0; index < labels.Length; index++)
                {
                    var value = keep.Contains(labels[index]) ? (byte)0 : (byte)255;
                    result[index * 4] = result[index * 4 + 1] = result[index * 4 + 2] = value;
                    result[index * 4 + 3] = 255;
                }
                return new CapturedFrame(frame.Region, width, height, width * 4,
                    frame.PixelFormat, result, frame.CapturedAt, frame.Origin, frame.IsCachedReuse);
            }
        }
        return null;
    }

    private static List<Component> LabelComponents(int[] labels, int width, int height,
        CancellationToken cancellationToken)
    {
        var queue = new int[labels.Length];
        var components = new List<Component>();
        for (var index = 0; index < labels.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (labels[index] != -1) continue;
            // Highly fragmented illustration/noise is outside this recovery's text-like scope.
            if (components.Count >= 4096) return new List<Component>();
            var id = components.Count + 1;
            var head = 0;
            var tail = 1;
            queue[0] = index;
            labels[index] = id;
            int left = width, top = height, right = 0, bottom = 0;
            while (head < tail)
            {
                if ((head & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                var current = queue[head++];
                var x = current % width;
                var y = current / width;
                left = Math.Min(left, x);
                right = Math.Max(right, x + 1);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y + 1);
                if (x > 0) Visit(current - 1);
                if (x < width - 1) Visit(current + 1);
                if (y > 0) Visit(current - width);
                if (y < height - 1) Visit(current + width);
            }
            components.Add(new Component(id, left, top, right, bottom, tail));

            void Visit(int neighbor)
            {
                if (labels[neighbor] != -1) return;
                labels[neighbor] = id;
                queue[tail++] = neighbor;
            }
        }
        return components;
    }

    private sealed record Component(int Id, int X, int Y, int Right, int Bottom, int Count)
    {
        public int Width => Right - X;
        public int Height => Bottom - Y;
    }
}
