using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;

namespace GameTranslator.Tests.Application;

public sealed class MenuGlyphCoreEvidenceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void FixedCoreAllowsBoundedPaddingAndWeakEdgeChangesButNotMotion(int scale)
    {
        var anchor = Region(new(22, 15, 116, 32), scale);
        var padding = Region(new(10, 15, 128, 32), scale, "edge");
        Assert.False(MenuGeometryMatcher.Matches(anchor.Candidate.Bounds, anchor.Candidate.SourceCandidateBounds,
            padding.Candidate.Bounds, padding.Candidate.SourceCandidateBounds));
        Assert.True(Match(anchor, padding));
        Assert.True(Match(padding, anchor));
        Assert.False(Match(anchor, Region(new(10, 15, 128, 32), scale, "move")));
        Assert.False(Match(anchor, Region(new(10, 15, 128, 32), scale, "lost")));
        Assert.False(Match(anchor, Region(new(10, 15, 128, 32), scale, "new")));
        var farther = Region(new(0, 15, 138, 32), scale);
        Assert.True(Match(padding, farther));
        Assert.False(Match(anchor, farther)); // never advance the discovery anchor
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("solid")]
    [InlineData("clipped")]
    [InlineData("multiple")]
    [InlineData("wrong-member")]
    [InlineData("short")]
    [InlineData("oversize")]
    [InlineData("uncertain")]
    public void UnsupportedCropsCannotProvideEvidence(string condition)
    {
        var b = condition == "short" ? new BoundingBox(22, 15, 70, 32)
            : condition == "clipped" ? new(30, 20, 116, 32)
            : condition == "oversize" ? new(0, 0, 2000, 100) : new(22, 15, 116, 32);
        Assert.Null(MenuGlyphCoreEvidence.TryCreate(Region(b, 1, condition)));
    }

    [Fact]
    public void ReciprocalAmbiguityStillRejectsBothDirections()
    {
        var a = Region(new(22, 15, 116, 32), 1);
        var b = Region(new(10, 15, 128, 32), 1);
        Assert.Empty(MenuGeometryMatcher.MatchUnique(new[] { a, b }, new[] { b }, Match));
        Assert.Empty(MenuGeometryMatcher.MatchUnique(new[] { a }, new[] { a, b }, Match));
        Assert.False(MenuGlyphCoreEvidence.Matches(a.Candidate.Bounds, null, b.Candidate.Bounds, MenuGlyphCoreEvidence.TryCreate(b)));
    }

    private static bool Match(TextCandidateRegion a, TextCandidateRegion b) => MenuGlyphCoreEvidence.Matches(
        a.Candidate.Bounds, MenuGlyphCoreEvidence.TryCreate(a), b.Candidate.Bounds, MenuGlyphCoreEvidence.TryCreate(b));

    internal static TextCandidateRegion Region(BoundingBox b, int scale, string condition = "")
    {
        var region = MenuFixedInkEvidenceTests.Region(b, scale, shift: condition == "move" ? 1 : 0, condition: condition);
        var pixels = region.Frame.PixelData.ToArray();
        for (var y = 0; y < region.Frame.Height; y++)
        for (var x = 0; x < region.Frame.Width; x++)
        {
            var offset = y * region.Frame.Stride + x * 4;
            byte value = pixels[offset] > 96 ? (byte)230 : (byte)5;
            var xx = b.X + x / scale; var yy = b.Y + y / scale;
            if (xx == 31 && yy == 25 && condition == "edge") value = 200;
            if (xx == 31 && yy == 25 && condition == "lost") value = 5;
            if (xx == 35 && yy == 25 && condition == "new") value = 230;
            if (value == 230 && condition == "uncertain" && xx % 2 == 0) value = 200;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
        }
        var f = region.Frame;
        return new(region.Candidate, new(f.Region, f.Width, f.Height, f.Stride, f.PixelFormat, pixels, f.CapturedAt));
    }
}
