using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;

namespace GameTranslator.Application.Pipeline;

/// <summary>Bounded, fixed-coordinate evidence that detector padding changed without a source change.</summary>
internal sealed class CandidatePixelContext
{
    private readonly CapturedFrame pixels;
    private readonly CaptureRegion sourceRegion;
    private readonly BoundingBox bounds;

    private CandidatePixelContext(CapturedFrame source, BoundingBox bounds)
    {
        sourceRegion = source.Region;
        this.bounds = bounds;
        pixels = TextCandidateRegionOcrService.CreateCroppedFrame(source, bounds);
    }

    internal static CandidatePixelContext? Create(CapturedFrame source, BoundingBox candidate)
    {
        var x = Math.Max(0, candidate.X - 2);
        var y = Math.Max(0, candidate.Y - 2);
        var right = Math.Min(source.Width, candidate.Right + 2);
        var bottom = Math.Min(source.Height, candidate.Bottom + 2);
        if (source.PixelFormat != "Bgra32" || source.Stride < (long)source.Width * 4
            || right <= x || bottom <= y || (long)(right - x) * (bottom - y) > 131072)
            return null;
        return new CandidatePixelContext(source, new BoundingBox(x, y, right - x, bottom - y));
    }

    internal bool Matches(CapturedFrame current, BoundingBox candidate)
    {
        if (current.Region != sourceRegion || current.PixelFormat != pixels.PixelFormat
            || current.Origin?.SourceId != pixels.Origin?.SourceId
            || current.Stride < (long)current.Width * 4
            || bounds.Right > current.Width || bounds.Bottom > current.Height
            || candidate.X < bounds.X || candidate.Y < bounds.Y
            || candidate.Right > bounds.Right || candidate.Bottom > bounds.Bottom)
            return false;

        for (var row = 0; row < bounds.Height; row++)
            if (!pixels.PixelData.Span.Slice(row * pixels.Stride, bounds.Width * 4).SequenceEqual(
                current.PixelData.Span.Slice((bounds.Y + row) * current.Stride + bounds.X * 4, bounds.Width * 4)))
                return false;
        return true;
    }
}
