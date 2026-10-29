using GameTranslator.Application.Ocr;
using GameTranslator.Application.Capture;

namespace GameTranslator.Application.Pipeline;

// Fixed discovery evidence only. The private bounded mask never retains source frames.
internal sealed class MenuGlyphCoreEvidence
{
    private readonly BoundingBox bounds;
    private readonly byte[] mask;

    private MenuGlyphCoreEvidence(BoundingBox bounds, byte[] mask)
    {
        this.bounds = bounds;
        this.mask = mask;
    }

    internal static MenuGlyphCoreEvidence? TryCreate(TextCandidateRegion region) => Create(region, null);

    internal static MenuGlyphCoreEvidence? TryCreateWithSource(TextCandidateRegion region, CapturedFrame source,
        IReadOnlyList<TextCandidate> neighbors) => Create(region, () => HasCompleteCoreBorder(region, source, neighbors));

    private static MenuGlyphCoreEvidence? Create(TextCandidateRegion region, Func<bool>? completeBorder)
    {
        var b = region.Candidate.Bounds;
        var frame = region.Frame;
        if (region.Candidate.SourceCandidateCount != 1
            || region.Candidate.SourceCandidateBounds[0] != b
            || frame.PixelFormat != "Bgra32" || frame.Width != b.Width || frame.Height != b.Height
            || b.Height is < 12 or > 192 || b.Width < 3L * b.Height
            || (long)b.Width * b.Height > 131_072) return null;
        var pixels = frame.PixelData.Span;
        var levels = new byte[b.Width * b.Height];
        int left = b.Width, right = -1, top = b.Height, bottom = -1, possible = 0, core = 0;
        for (var y = 0; y < b.Height; y++)
        for (var x = 0; x < b.Width; x++)
        {
            var offset = y * frame.Stride + x * 4;
            var luminance = pixels[offset] * .0722 + pixels[offset + 1] * .7152 + pixels[offset + 2] * .2126;
            if (luminance <= 180) continue;
            possible++;
            levels[y * b.Width + x] = luminance > 210 ? (byte)2 : (byte)1;
            if (luminance <= 210) continue;
            core++;
            left = Math.Min(left, x); right = Math.Max(right, x);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        if (core < 64 || possible > levels.Length * .5 || possible - core > possible * .15
            || right - left + 1 < b.Width * .5 || bottom - top + 1 < b.Height * .35) return null;
        if ((left < 1 || top < 1 || right >= b.Width - 1 || bottom >= b.Height - 1)
            && (completeBorder is null || !completeBorder())) return null;
        var width = right - left + 1;
        var height = bottom - top + 1;
        var trimmed = new byte[width * height];
        for (var y = 0; y < height; y++)
            levels.AsSpan((top + y) * b.Width + left, width).CopyTo(trimmed.AsSpan(y * width, width));
        return new(new(b.X + left, b.Y + top, width, height), trimmed);
    }

    private static bool HasCompleteCoreBorder(TextCandidateRegion region, CapturedFrame source,
        IReadOnlyList<TextCandidate> neighbors)
    {
        var b = region.Candidate.Bounds;
        var crop = region.Frame;
        // Border evidence belongs to this exact source observation, not a recent neighbor frame.
        if (source.Origin is null || source.Origin != crop.Origin || source.CapturedAt != crop.CapturedAt
            || source.IsCachedReuse != crop.IsCachedReuse || source.PixelFormat != "Bgra32"
            || crop.Region.X != source.Region.X + b.X || crop.Region.Y != source.Region.Y + b.Y) return false;
        var padding = Math.Max(2, (int)Math.Ceiling(b.Height * .05));
        if (b.X < padding || b.Y < padding) return false;
        var outer = new BoundingBox(b.X - padding, b.Y - padding, b.Width + 2 * padding, b.Height + 2 * padding);
        if (!outer.IsWithin(source.Width, source.Height)
            || (long)outer.Width * outer.Height > 131_072
            || neighbors.Any(n => !ReferenceEquals(n, region.Candidate)
                && n.Bounds.X < outer.Right && n.Bounds.Right > outer.X
                && n.Bounds.Y < outer.Bottom && n.Bounds.Bottom > outer.Y)) return false;
        var pixels = source.PixelData.Span;
        // Verify provenance and crop bytes before using pixels outside the detector rectangle.
        for (var y = 0; y < b.Height; y++)
            if (!pixels.Slice((b.Y + y) * source.Stride + b.X * 4, b.Width * 4)
                .SequenceEqual(crop.PixelData.Span.Slice(y * crop.Stride, b.Width * 4))) return false;
        for (var y = outer.Y; y < outer.Bottom; y++)
        for (var x = outer.X; x < outer.Right; x++)
        {
            if (x >= b.X && x < b.Right && y >= b.Y && y < b.Bottom) continue;
            var offset = y * source.Stride + x * 4;
            var luminance = pixels[offset] * .0722 + pixels[offset + 1] * .7152 + pixels[offset + 2] * .2126;
            // No bright stroke may extend beyond the crop. The outer ring must also
            // exclude possible ink; weak antialiasing is allowed only in the inner band.
            if (luminance > 210 || (luminance > 180
                && (x == outer.X || x == outer.Right - 1 || y == outer.Y || y == outer.Bottom - 1))) return false;
        }
        return true;
    }

    internal static bool Matches(BoundingBox anchor, MenuGlyphCoreEvidence? a,
        BoundingBox current, MenuGlyphCoreEvidence? b)
    {
        if (a is null || b is null || a.bounds != b.bounds) return false;
        var tolerance = Math.Min(anchor.Height, current.Height) * .5;
        if (Math.Abs(anchor.X - current.X) > tolerance || Math.Abs(anchor.Y - current.Y) > tolerance
            || Math.Abs(anchor.Right - current.Right) > tolerance || Math.Abs(anchor.Bottom - current.Bottom) > tolerance)
            return false;
        var width = Math.Min(anchor.Right, current.Right) - Math.Max(anchor.X, current.X);
        var height = Math.Min(anchor.Bottom, current.Bottom) - Math.Max(anchor.Y, current.Y);
        if (width <= 0 || height <= 0) return false;
        var intersection = (long)width * height;
        if (intersection / (double)((long)anchor.Width * anchor.Height + (long)current.Width * current.Height - intersection) < .75)
            return false;
        // Symmetric containment in the same absolute coordinates, never mask alignment.
        // A bright stroke cannot disappear into background or appear from background.
        for (var i = 0; i < a.mask.Length; i++)
            if ((a.mask[i] == 2 && b.mask[i] == 0) || (b.mask[i] == 2 && a.mask[i] == 0)) return false;
        return true;
    }
}
