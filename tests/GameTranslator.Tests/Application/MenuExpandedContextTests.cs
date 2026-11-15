using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;

namespace GameTranslator.Tests.Application;

public sealed class MenuExpandedContextTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Complete_UsesRelativeSearchAndPreservesDetectorBounds(int scale)
    {
        var source = Source(scale);
        var bounds = Box(scale);
        var crop = Crop(source, bounds);
        var result = MenuExpandedContext.Complete(crop, source, [crop.Candidate]);
        var context = Assert.IsType<CapturedFrame>(result.RecognitionContextFrame);
        Assert.NotNull(result.ExpandedMenuCore);
        Assert.Same(crop.Frame, result.Frame);
        Assert.Same(crop.Candidate, result.Candidate);
        Assert.Equal(source.Origin, context.Origin);
        Assert.InRange(context.Height - bounds.Height, 4 * scale, (int)Math.Ceiling(bounds.Height * .25) * 2);
        var anchor = Crop(source, new(18 * scale, 38 * scale, 270 * scale, 24 * scale));
        Assert.True(MenuGlyphCoreEvidence.Matches(anchor.Candidate.Bounds,
            MenuGlyphCoreEvidence.TryCreateWithSource(anchor, source, [anchor.Candidate]), bounds, result.ExpandedMenuCore));
    }

    [Theory]
    [InlineData("neighbor")]
    [InlineData("source")]
    [InlineData("bytes")]
    [InlineData("edge")]
    [InlineData("multiple")]
    public void Complete_RejectsUntrustedOrAmbiguousContext(string condition)
    {
        var source = Source(1);
        var b = condition == "edge" ? new BoundingBox(0, 36, 270, 24) : Box(1);
        var crop = Crop(source, b);
        var neighbors = new List<TextCandidate> { crop.Candidate };
        if (condition == "neighbor") neighbors.Add(new(new(b.Right, b.Y, 10, b.Height), .9));
        if (condition == "multiple") crop = new(crop.Candidate with { SourceCandidateBounds = [b, b] }, crop.Frame);
        if (condition == "source") source = new(source.Region, source.Width, source.Height, source.Stride,
            source.PixelFormat, source.PixelData, source.CapturedAt, source.Origin! with { SourceId = Guid.NewGuid() });
        if (condition == "bytes")
        {
            var bytes = source.PixelData.ToArray(); bytes[(b.Y * source.Width + b.X) * 4]++;
            source = new(source.Region, source.Width, source.Height, source.Stride, source.PixelFormat,
                bytes, source.CapturedAt, source.Origin);
        }
        Assert.Same(crop, MenuExpandedContext.Complete(crop, source, neighbors));
    }

    internal static BoundingBox Box(int s) => new(16 * s, 36 * s, 270 * s, 24 * s);
    internal static CapturedFrame Source(int scale)
    {
        var width = 400 * scale; var height = 120 * scale;
        var bytes = Pixels(scale);
        var time = DateTimeOffset.UnixEpoch;
        return new(new(0, 0, width, height), width, height, width * 4, "Bgra32", bytes, time,
            new(Guid.Parse("8569ed47-84e2-409c-8d3c-7f8f962517b2"), TimeSpan.FromSeconds(1), time));
    }
    internal static byte[] Pixels(int scale)
    {
        var width = 400 * scale; var height = 120 * scale;
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var ink = x / scale >= 24 && x / scale < 277 && (x / scale - 24) % 10 < 3
                && y / scale >= 40 && y / scale < 62;
            var offset = (y * width + x) * 4;
            bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = ink ? (byte)240 : (byte)8;
            bytes[offset + 3] = 255;
        }
        return bytes;
    }
    private static TextCandidateRegion Crop(CapturedFrame source, BoundingBox b) =>
        new(new(b, .99), TextCandidateRegionOcrService.CreateCroppedFrame(source, b));
}
