using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Content;

/// <summary>Pixel evidence for a decorated, already-known key name inside a compact light rectangle.</summary>
internal static class MenuNamedKeycapEvidence
{
    internal static bool Matches(OcrResult result)
    {
        var request = result.Request;
        var frame = request.Frame;
        if (request.ContentLayoutMode != ContentLayoutMode.StaticMenu
            || request.OrientationMode != OcrOrientationMode.Horizontal
            || request.DetectorLineBounds.Count != 1 || result.TextBlocks.Count != 1
            || frame.PixelFormat != "Bgra32" || frame.Width is < 12 or > 256 || frame.Height is < 12 or > 256
            || frame.Width < frame.Height * .8 || frame.Width > frame.Height * 1.3
            || (long)request.DetectorLineBounds[0].Width * request.DetectorLineBounds[0].Height < frame.Width * frame.Height * .8) return false;

        var text = result.TextBlocks[0].Text.Trim();
        if (text.Length is < 3 or > 7) return false;
        var name = text.AsSpan();
        if (IsEdge(name[0])) name = name[1..].TrimStart();
        if (!name.IsEmpty && IsEdge(name[^1])) name = name[..^1].TrimEnd();
        if (name.Length == text.Length || name.Length is < 2 or > 3) return false;
        foreach (var c in name)
            if (!char.IsAsciiLetter(c)) return false;
        // Use the existing exact key vocabulary, never a list of observed OCR mistakes.
        if (!MenuTranslationIntegrity.IsStandaloneControlHint(name.ToString())) return false;

        int width = frame.Width, height = frame.Height, area = width * height;
        var dark = new bool[area];
        var left = Enumerable.Repeat(width, height).ToArray();
        var right = Enumerable.Repeat(-1, height).ToArray();
        var bright = 0;
        var pixels = frame.PixelData.Span;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = y * frame.Stride + x * 4;
            var luminance = pixels[offset] * .0722 + pixels[offset + 1] * .7152 + pixels[offset + 2] * .2126;
            dark[y * width + x] = luminance < 70;
            if (luminance <= 180) continue;
            if (x == 0 || y == 0 || x == width - 1 || y == height - 1) return false;
            bright++;
            left[y] = Math.Min(left[y], x);
            right[y] = x;
        }
        if (bright < area * .6 || bright > area * .9) return false;
        // A rectangular silhouette must surround the letters; a taper or plain bright text is insufficient.
        var start = (int)Math.Ceiling(height * .15);
        var end = (int)Math.Floor(height * .85);
        for (var y = start; y <= end; y++)
            if (right[y] - left[y] + 1 < width * .8
                || Math.Abs(left[y] - left[start]) > width * .04
                || Math.Abs(right[y] - right[start]) > width * .04) return false;

        var queue = new Queue<int>();
        var glyphs = new List<BoundingBox>();
        for (var seed = 0; seed < area; seed++)
        {
            if (!dark[seed]) continue;
            dark[seed] = false;
            queue.Enqueue(seed);
            int count = 0, x0 = width, y0 = height, x1 = 0, y1 = 0;
            var entersInterior = false;
            while (queue.TryDequeue(out var index))
            {
                int x = index % width, y = index / width;
                count++;
                entersInterior |= y >= start && y <= end
                    && x - left[y] >= width * .04 && right[y] - x >= width * .04;
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
            if (x0 == 0 || y0 == 0 || x1 == width - 1 || y1 == height - 1)
            {
                // Do not mistake a clipped/connected extra letter for outer background.
                if (entersInterior) return false;
                continue;
            }
            if (count < area * .005) continue;
            var glyph = new BoundingBox(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
            if (count < area * .015 || count > area * .18
                || glyph.Width < width * .08 || glyph.Width > width * .4
                || glyph.Height < height * .25 || glyph.Height > height * .6
                || y0 < height * .15 || height - 1 - y1 < height * .15) return false;
            for (var y = y0; y <= y1; y++)
                if (x0 - left[y] < width * .04 || right[y] - x1 < width * .04) return false;
            glyphs.Add(glyph);
            if (glyphs.Count > name.Length) return false;
        }
        if (glyphs.Count != name.Length) return false;
        glyphs.Sort((a, b) => a.X.CompareTo(b.X));
        for (var i = 1; i < glyphs.Count; i++)
            if (glyphs[i].X <= glyphs[i - 1].Right || glyphs[i].X - glyphs[i - 1].Right > width * .2
                || Math.Abs(glyphs[i].Y - glyphs[0].Y) > height * .08
                || Math.Abs(glyphs[i].Bottom - glyphs[0].Bottom) > height * .08) return false;
        return true;
    }

    private static bool IsEdge(char c) => c is '[' or ']' or '|' or '/' or '\\';
}
