using System.Security.Cryptography;
using GameTranslator.Application.Ocr;

namespace GameTranslator.Application.Pipeline;

// Immutable, bounded evidence of stationary glyph cores. Never retains a captured frame.
internal sealed record MenuFixedInkEvidence(BoundingBox InkBounds, string Signature)
{
    internal static MenuFixedInkEvidence? TryCreate(TextCandidateRegion region)
    {
        var b = region.Candidate.Bounds;
        var frame = region.Frame;
        if (region.Candidate.SourceCandidateCount != 1
            || region.Candidate.SourceCandidateBounds[0] != b
            || frame.PixelFormat != "Bgra32" || frame.Width != b.Width || frame.Height != b.Height
            || b.Height is < 12 or > 192 || b.Width < 3L * b.Height
            || (long)b.Width * b.Height > 131_072) return null;
        var mask = new byte[b.Width * b.Height];
        var pixels = frame.PixelData.Span;
        int left = b.Width, right = -1, top = b.Height, bottom = -1, lowCount = 0, highCount = 0;
        for (var y = 0; y < b.Height; y++)
        for (var x = 0; x < b.Width; x++)
        {
            var offset = y * frame.Stride + x * 4;
            var luminance = pixels[offset] * .0722 + pixels[offset + 1] * .7152 + pixels[offset + 2] * .2126;
            if (luminance <= 96) continue;
            mask[y * b.Width + x] = luminance > 128 ? (byte)2 : (byte)1;
            lowCount++;
            if (luminance > 128) highCount++;
            left = Math.Min(left, x); right = Math.Max(right, x);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        // Empty, solid, clipped, tiny or short marks are not stationary text evidence.
        if (lowCount < 32 || highCount < 16 || lowCount > mask.Length * .5
            || left < 1 || top < 1 || right >= b.Width - 1 || bottom >= b.Height - 1
            || right - left + 1 < b.Width * .5 || bottom - top + 1 < b.Height * .35) return null;
        var width = right - left + 1;
        var height = bottom - top + 1;
        var trimmed = new byte[width * height];
        for (var y = 0; y < height; y++)
            mask.AsSpan((top + y) * b.Width + left, width).CopyTo(trimmed.AsSpan(y * width, width));
        return new(new(b.X + left, b.Y + top, width, height), Convert.ToHexString(SHA256.HashData(trimmed)));
    }

    internal static bool Matches(BoundingBox anchor, MenuFixedInkEvidence? fixedInk,
        BoundingBox current, MenuFixedInkEvidence? currentInk)
    {
        if (fixedInk is null || currentInk is null || fixedInk != currentInk) return false;
        var tolerance = Math.Min(anchor.Height, current.Height) * .25;
        if (Math.Abs(anchor.X - current.X) > tolerance || Math.Abs(anchor.Y - current.Y) > tolerance
            || Math.Abs(anchor.Right - current.Right) > tolerance || Math.Abs(anchor.Bottom - current.Bottom) > tolerance)
            return false;
        var width = Math.Min(anchor.Right, current.Right) - Math.Max(anchor.X, current.X);
        var height = Math.Min(anchor.Bottom, current.Bottom) - Math.Max(anchor.Y, current.Y);
        if (width <= 0 || height <= 0) return false;
        var intersection = (long)width * height;
        var union = (long)anchor.Width * anchor.Height + (long)current.Width * current.Height - intersection;
        return intersection / (double)union >= .75;
    }
}
