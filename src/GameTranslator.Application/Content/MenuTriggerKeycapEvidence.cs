using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Content;

/// <summary>Source-pixel evidence for an isolated, tapered two-glyph controller keycap.</summary>
internal static class MenuTriggerKeycapEvidence
{
    internal static bool Matches(OcrResult result)
    {
        var request = result.Request;
        var frame = request.Frame;
        if (request.ContentLayoutMode != ContentLayoutMode.StaticMenu
            || request.OrientationMode != OcrOrientationMode.Horizontal
            || request.DetectorLineBounds.Count != 1 || result.TextBlocks.Count != 1
            || frame.PixelFormat != "Bgra32" || frame.Width is < 12 or > 256 || frame.Height is < 12 or > 256
            || frame.Width < frame.Height * .65 || frame.Width > frame.Height * 1.1
            || (long)request.DetectorLineBounds[0].Width * request.DetectorLineBounds[0].Height < frame.Width * frame.Height * .8) return false;
        var text = result.TextBlocks[0].Text.Trim();
        if (text.Length is < 1 or > 4) return false;
        // Permit one OCR punctuation artifact, but never use a particular misread as evidence.
        var letters = text[^1] is ',' or '.' or ';' or ':' ? text[..^1] : text;
        if (letters.Length is < 1 or > 3 || letters.Any(c => !char.IsAsciiLetter(c))) return false;

        var width = frame.Width;
        var height = frame.Height;
        var area = width * height;
        var dark = new bool[area];
        var left = Enumerable.Repeat(width, height).ToArray();
        var right = Enumerable.Repeat(-1, height).ToArray();
        var brightCount = 0;
        var pixels = frame.PixelData.Span;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = y * frame.Stride + x * 4;
            var luminance = pixels[offset] * .0722 + pixels[offset + 1] * .7152 + pixels[offset + 2] * .2126;
            dark[y * width + x] = luminance < 70;
            if (luminance <= 180) continue;
            brightCount++;
            left[y] = Math.Min(left[y], x);
            right[y] = x;
        }
        if (brightCount < area * .5 || brightCount > area * .85
            || !HasTaper(left, right, width, height)) return false;

        var queue = new Queue<int>();
        var glyphs = new List<BoundingBox>();
        for (var seed = 0; seed < area; seed++)
        {
            if (!dark[seed]) continue;
            dark[seed] = false;
            queue.Enqueue(seed);
            int count = 0, x0 = width, y0 = height, x1 = 0, y1 = 0;
            while (queue.TryDequeue(out var index))
            {
                var x = index % width;
                var y = index / width;
                count++;
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x);
                y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                for (var ny = Math.Max(0, y - 1); ny <= Math.Min(height - 1, y + 1); ny++)
                for (var nx = Math.Max(0, x - 1); nx <= Math.Min(width - 1, x + 1); nx++)
                {
                    var next = ny * width + nx;
                    if (!dark[next]) continue;
                    dark[next] = false;
                    queue.Enqueue(next);
                }
            }
            if (x0 == 0 || y0 == 0 || x1 == width - 1 || y1 == height - 1 || count < area * .005) continue;
            var glyph = new BoundingBox(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
            if (count < area * .015 || count > area * .18
                || glyph.Width < width * .1 || glyph.Width > width * .35
                || glyph.Height < height * .25 || glyph.Height > height * .55
                || y0 < height * .2 || y1 > height * .8) return false;
            // Both glyphs must be fully inside the light silhouette, away from its sides.
            for (var y = y0; y <= y1; y++)
                if (x0 - left[y] < width * .06 || right[y] - x1 < width * .06) return false;
            glyphs.Add(glyph);
            if (glyphs.Count > 2) return false;
        }
        if (glyphs.Count != 2) return false;
        glyphs.Sort((a, b) => a.X.CompareTo(b.X));
        var first = glyphs[0]; var second = glyphs[1];
        return Math.Abs(first.Y - second.Y) <= height * .08
            && Math.Abs(first.Bottom - second.Bottom) <= height * .08
            && second.X > first.Right && second.X - first.Right <= width * .2
            && second.Right - first.X <= width * .65;
    }

    private static bool HasTaper(int[] left, int[] right, int width, int height)
    {
        var start = (int)Math.Ceiling(height * .25);
        var end = (int)Math.Floor(height * .9);
        var rows = Enumerable.Range(start, end - start + 1).ToArray();
        if (rows.Any(y => right[y] - left[y] + 1 < width * .35)
            || right[start] - left[start] + 1 < width * .8) return false;
        var steadyLeft = rows.Max(y => left[y]) - rows.Min(y => left[y]) <= width * .08;
        var steadyRight = rows.Max(y => right[y]) - rows.Min(y => right[y]) <= width * .08;
        var narrowing = steadyLeft ? right[start] - right[end] : left[end] - left[start];
        if (steadyLeft == steadyRight || narrowing < width * .2 || narrowing > width * .55) return false;
        return rows.Skip(1).All(y => steadyLeft
            ? right[y] <= right[y - 1] + width * .04
            : left[y] >= left[y - 1] - width * .04);
    }
}
