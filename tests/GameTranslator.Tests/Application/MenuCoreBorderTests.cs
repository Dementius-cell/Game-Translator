using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;

namespace GameTranslator.Tests.Application;

public sealed class MenuCoreBorderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void CompleteCoreAtCropEdgeRequiresIndependentSameFrameBorder(int scale)
    {
        var source = Source(scale);
        var b = new BoundingBox(10 * scale, 20 * scale, 128 * scale, 27 * scale);
        var region = Region(source, b);
        var anchor = Region(source, new(22 * scale, 15 * scale, 116 * scale, 32 * scale));
        Assert.Null(MenuGlyphCoreEvidence.TryCreate(region));
        var core = MenuGlyphCoreEvidence.TryCreateWithSource(region, source, [region.Candidate]);
        Assert.NotNull(core);
        Assert.True(MenuGlyphCoreEvidence.Matches(anchor.Candidate.Bounds, MenuGlyphCoreEvidence.TryCreate(anchor), b, core));
        Assert.True(MenuGlyphCoreEvidence.Matches(b, core, anchor.Candidate.Bounds, MenuGlyphCoreEvidence.TryCreate(anchor)));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("timestamp")]
    [InlineData("unknown")]
    [InlineData("pixels")]
    [InlineData("offset")]
    [InlineData("neighbor")]
    [InlineData("bright-outside")]
    [InlineData("weak-outer")]
    [InlineData("zone-edge")]
    public void IncompleteOrUnrelatedContextCannotRescueClippedCrop(string condition)
    {
        var source = Source(1);
        var b = condition == "zone-edge" ? new BoundingBox(0, 20, 134, 20) : new(22, 20, 116, 20);
        var region = Region(source, b);
        var origin = source.Origin!;
        var pixels = source.PixelData.ToArray();
        if (condition == "source") origin = origin with { SourceId = Guid.NewGuid() };
        if (condition == "timestamp") origin = origin with { SourceTimestamp = TimeSpan.FromSeconds(2) };
        if (condition == "unknown") origin = null;
        if (condition == "pixels") pixels[(25 * source.Width + 31) * 4] = 0;
        if (condition is "bright-outside" or "weak-outer")
        {
            var y = condition == "bright-outside" ? 19 : 18;
            var offset = (y * source.Width + 31) * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = condition == "bright-outside" ? (byte)230 : (byte)200;
        }
        var context = new CapturedFrame(new(condition == "offset" ? 1 : 0, 0, source.Width, source.Height),
            source.Width, source.Height, source.Stride, source.PixelFormat, pixels, source.CapturedAt, origin);
        var neighbors = condition == "neighbor"
            ? new[] { region.Candidate, new TextCandidate(new(20, 18, 10, 2), .9) } : new[] { region.Candidate };
        Assert.Null(MenuGlyphCoreEvidence.TryCreateWithSource(region, context, neighbors));
    }

    [Fact]
    public void InnerAntialiasingIsAllowedButActualCoreClippingIsRejected()
    {
        var source = Source(1);
        var pixels = source.PixelData.ToArray();
        var offset = (19 * source.Width + 31) * 4;
        pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 200;
        source = new(source.Region, source.Width, source.Height, source.Stride, source.PixelFormat,
            pixels, source.CapturedAt, source.Origin);
        var full = Region(source, new(22, 20, 116, 20));
        Assert.NotNull(MenuGlyphCoreEvidence.TryCreateWithSource(full, source, [full.Candidate]));
        var clipped = Region(source, new(22, 21, 116, 19));
        Assert.Null(MenuGlyphCoreEvidence.TryCreateWithSource(clipped, source, [clipped.Candidate]));
    }

    private static CapturedFrame Source(int scale)
    {
        var f = MenuGlyphCoreEvidenceTests.Region(new(0, 0, 144, 64), scale).Frame;
        return new(f.Region, f.Width, f.Height, f.Stride, f.PixelFormat, f.PixelData, f.CapturedAt,
            new(Guid.Parse("f601d25d-5987-4a62-814e-074c190fa835"), TimeSpan.FromSeconds(1), f.CapturedAt));
    }

    private static TextCandidateRegion Region(CapturedFrame source, BoundingBox b) =>
        new(new(b, .95), TextCandidateRegionOcrService.CreateCroppedFrame(source, b));
}
