using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;

namespace GameTranslator.Application.Pipeline;

// Geometry evidence only. The complete context must still pass independent OCR authority.
internal static class MenuExpandedContext
{
    internal static TextCandidateRegion Complete(TextCandidateRegion region, CapturedFrame source,
        IReadOnlyList<TextCandidate> neighbors)
    {
        var b = region.Candidate.Bounds;
        var crop = region.Frame;
        if (region.Candidate.SourceCandidateCount != 1 || region.Candidate.SourceCandidateBounds[0] != b
            || b.Width < 3L * b.Height || source.Origin is null || source.Origin != crop.Origin
            || source.CapturedAt != crop.CapturedAt || source.IsCachedReuse != crop.IsCachedReuse
            || source.PixelFormat != "Bgra32" || crop.PixelFormat != "Bgra32"
            || crop.Width != b.Width || crop.Height != b.Height || !b.IsWithin(source.Width, source.Height)
            || crop.Region.X != source.Region.X + b.X || crop.Region.Y != source.Region.Y + b.Y
            || (long)b.Width * b.Height > 131072) return region;
        for (var y = 0; y < b.Height; y++)
            if (!source.PixelData.Span.Slice((b.Y + y) * source.Stride + b.X * 4, b.Width * 4)
                .SequenceEqual(crop.PixelData.Span.Slice(y * crop.Stride, b.Width * 4))) return region;
        if (MenuGlyphCoreEvidence.TryCreateWithSource(region, source, neighbors) is not null) return region;

        // Search only a fraction of the transverse dimension, from the smallest complete border.
        var limit = (int)Math.Ceiling(b.Height * .25);
        for (var pad = 1; pad <= limit; pad++)
        {
            if (b.X < pad || b.Y < pad || b.Right + pad > source.Width || b.Bottom + pad > source.Height)
                return region;
            var outer = new BoundingBox(b.X - pad, b.Y - pad, b.Width + 2 * pad, b.Height + 2 * pad);
            if ((long)outer.Width * outer.Height > 131072 || neighbors.Any(n => !ReferenceEquals(n, region.Candidate)
                && n.Bounds.X < outer.Right && n.Bounds.Right > outer.X
                && n.Bounds.Y < outer.Bottom && n.Bounds.Bottom > outer.Y)) return region;
            var outsideCore = false;
            var quietBorder = true;
            for (var y = outer.Y; y < outer.Bottom; y++)
            for (var x = outer.X; x < outer.Right; x++)
            {
                if (x >= b.X && x < b.Right && y >= b.Y && y < b.Bottom) continue;
                var offset = y * source.Stride + x * 4;
                var pixels = source.PixelData.Span;
                var light = pixels[offset] * .0722 + pixels[offset + 1] * .7152 + pixels[offset + 2] * .2126;
                outsideCore |= light > 210;
                if ((x == outer.X || x == outer.Right - 1 || y == outer.Y || y == outer.Bottom - 1) && light > 180)
                    quietBorder = false;
            }
            if (!outsideCore || !quietBorder) continue;
            var context = TextCandidateRegionOcrService.CreateCroppedFrame(source, outer);
            var expanded = new TextCandidateRegion(new TextCandidate(outer, region.Candidate.Confidence), context);
            var core = MenuGlyphCoreEvidence.TryCreate(expanded);
            if (core is null) continue;
            return new(region.Candidate, crop)
            {
                RecognitionContextFrame = context,
                ExpandedMenuCore = core,
                WatermarkEvidence = region.WatermarkEvidence,
            };
        }
        return region;
    }
}
