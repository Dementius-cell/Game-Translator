using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Content;

// Recognizes an isolated row of filled five-point stars, never an OCR spelling.
internal static class MenuStarRatingEvidence
{
    private static readonly (double X, double Y)[] Outline =
    [(.5, 0), (.618, .382), (1, .382), (.691, .618), (.809, 1),
        (.5, .764), (.191, 1), (.309, .618), (0, .382), (.382, .382)];

    internal static bool Matches(OcrResult result)
    {
        var r = result.Request;
        var f = r.Frame;
        if (r.ContentLayoutMode != ContentLayoutMode.StaticMenu || r.OrientationMode != OcrOrientationMode.Horizontal
            || r.DetectorLineBounds.Count != 1 || result.TextBlocks.Count != 1
            || f.PixelFormat != "Bgra32" || f.Height is < 12 or > 192 || f.Width < 2 * f.Height
            || f.Width > 6 * f.Height || (long)f.Width * f.Height > 131_072
            || (long)r.DetectorLineBounds[0].Width * r.DetectorLineBounds[0].Height < f.Width * f.Height * .8) return false;
        var mask = new bool[f.Width * f.Height];
        var occupied = new bool[f.Width];
        var pixels = f.PixelData.Span;
        for (var y = 0; y < f.Height; y++)
        for (var x = 0; x < f.Width; x++)
        {
            var offset = y * f.Stride + x * 4;
            if (pixels[offset] * .0722 + pixels[offset + 1] * .7152 + pixels[offset + 2] * .2126 <= 180) continue;
            mask[y * f.Width + x] = occupied[x] = true;
        }
        var stars = new List<BoundingBox>();
        for (var x = 0; x < f.Width; x++)
        {
            if (!occupied[x]) continue;
            var left = x;
            while (x < f.Width && occupied[x]) x++;
            int top = f.Height, bottom = -1;
            for (var y = 0; y < f.Height; y++)
            for (var xx = left; xx < x; xx++)
                if (mask[y * f.Width + xx]) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            var b = new BoundingBox(left, top, x - left, bottom - top + 1);
            if (b.Width < 8 || b.Height < f.Height * .6 || b.Width < b.Height * .85 || b.Width > b.Height * 1.25
                || !HasStarSilhouette(mask, f.Width, b)) return false;
            stars.Add(b);
            if (stars.Count > 5) return false;
        }
        if (stars.Count < 3) return false;
        var height = stars.Max(b => b.Height);
        if (stars.Min(b => b.Height) < height * .8 || stars.Min(b => b.Width) < stars.Max(b => b.Width) * .8
            || stars.Max(b => b.Y) - stars.Min(b => b.Y) > height * .15
            || stars.Max(b => b.Bottom) - stars.Min(b => b.Bottom) > height * .15) return false;
        var gaps = stars.Zip(stars.Skip(1), (a, b) => b.X - a.Right).ToArray();
        return gaps.Max() <= height * .3 && gaps.Max() - gaps.Min() <= height * .15;
    }

    private static bool HasStarSilhouette(bool[] mask, int stride, BoundingBox b)
    {
        int intersection = 0, union = 0;
        for (var y = 0; y < b.Height; y++)
        for (var x = 0; x < b.Width; x++)
        {
            var px = (x + .5) / b.Width;
            var py = (y + .5) / b.Height;
            var inside = false;
            for (var i = 0; i < Outline.Length; i++)
            {
                var a = Outline[i]; var c = Outline[(i + Outline.Length - 1) % Outline.Length];
                if ((a.Y > py) != (c.Y > py) && px < (c.X - a.X) * (py - a.Y) / (c.Y - a.Y) + a.X)
                    inside = !inside;
            }
            var observed = mask[(b.Y + y) * stride + b.X + x];
            if (inside && observed) intersection++;
            if (inside || observed) union++;
        }
        return union > 0 && intersection / (double)union >= .8;
    }
}
