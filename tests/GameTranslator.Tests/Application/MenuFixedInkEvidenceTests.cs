using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;

namespace GameTranslator.Tests.Application;

public sealed class MenuFixedInkEvidenceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void DarkPaddingMayChangeOnlyAroundExactlyStationaryTwoLevelInk(int scale)
    {
        var anchor = Region(new(22, 15, 116, 32), scale);
        var padding = Region(new(20, 10, 118, 37), scale, background: 55);
        Assert.False(MenuGeometryMatcher.Matches(anchor.Candidate.Bounds, anchor.Candidate.SourceCandidateBounds,
            padding.Candidate.Bounds, padding.Candidate.SourceCandidateBounds));
        Assert.True(Match(anchor, padding));
        Assert.True(Match(padding, anchor));
        Assert.False(Match(anchor, Region(new(20, 10, 118, 37), scale, shift: 1)));
        Assert.False(Match(anchor, Region(new(20, 10, 118, 37), scale, changed: true)));
        Assert.False(Match(anchor, Region(new(18, 5, 120, 42), scale))); // no rolling expansion
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("solid")]
    [InlineData("clipped")]
    [InlineData("multiple")]
    [InlineData("wrong-member")]
    [InlineData("short")]
    [InlineData("oversize")]
    public void UnsupportedCropsDoNotProduceEvidence(string condition)
    {
        var bounds = condition == "short" ? new BoundingBox(22, 15, 70, 32)
            : condition == "clipped" ? new(30, 20, 116, 32)
            : condition == "oversize" ? new(0, 0, 2000, 100) : new(22, 15, 116, 32);
        Assert.Null(MenuFixedInkEvidence.TryCreate(Region(bounds, 1, condition: condition)));
    }

    [Fact]
    public void EvidenceDoesNotResolveReciprocalAmbiguityOrPermitMovement()
    {
        var anchor = Region(new(22, 15, 116, 32), 1);
        var padding = Region(new(20, 10, 118, 37), 1);
        Assert.Empty(MenuGeometryMatcher.MatchUnique(new[] { anchor, padding }, new[] { padding }, Match));
        Assert.Empty(MenuGeometryMatcher.MatchUnique(new[] { anchor }, new[] { anchor, padding }, Match));
        var evidence = MenuFixedInkEvidence.TryCreate(anchor);
        Assert.NotNull(evidence);
        Assert.False(MenuFixedInkEvidence.Matches(anchor.Candidate.Bounds, evidence,
            new(22, 115, 116, 32), evidence));
        Assert.False(MenuFixedInkEvidence.Matches(anchor.Candidate.Bounds, null, padding.Candidate.Bounds,
            MenuFixedInkEvidence.TryCreate(padding)));
    }

    private static bool Match(TextCandidateRegion a, TextCandidateRegion b) => MenuFixedInkEvidence.Matches(
        a.Candidate.Bounds, MenuFixedInkEvidence.TryCreate(a), b.Candidate.Bounds, MenuFixedInkEvidence.TryCreate(b));

    internal static TextCandidateRegion Region(BoundingBox b, int scale, int shift = 0, bool changed = false,
        byte background = 5, string condition = "")
    {
        var bounds = new BoundingBox(b.X * scale, b.Y * scale, b.Width * scale, b.Height * scale);
        var pixels = new byte[bounds.Width * bounds.Height * 4];
        for (var y = 0; y < bounds.Height; y++)
        for (var x = 0; x < bounds.Width; x++)
        {
            var xx = b.X + x / scale - shift; var yy = b.Y + y / scale;
            var ink = xx >= 30 && xx < 130 && yy >= 20 && yy < 40 && xx % 10 < 4;
            var value = ink ? (byte)(xx % 2 == 0 ? 145 : 110) : background;
            if (changed && xx == 30 && yy == 20) value = 110; // changes only upper threshold
            if (condition == "empty") value = 5;
            if (condition == "solid") value = 145;
            var offset = (y * bounds.Width + x) * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
        var candidate = new TextCandidate(bounds, .95)
        { SourceCandidateBounds = condition == "multiple" ? [bounds, bounds] : condition == "wrong-member" ? [new(0, 0, 10, 10)] : [bounds] };
        return new(candidate, new(new(bounds.X, bounds.Y, bounds.Width, bounds.Height), bounds.Width, bounds.Height,
            bounds.Width * 4, "Bgra32", pixels, DateTimeOffset.UnixEpoch));
    }
}
