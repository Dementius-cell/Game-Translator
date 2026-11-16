using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;

namespace GameTranslator.Tests.Application;

public sealed class MenuProvisionalGeometryTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(4, false)]
    [InlineData(1, true)]
    public void Proposal_UsesRelativeGeometryOnBothAxes(int scale, bool transpose)
    {
        BoundingBox Box(int x, int y, int w, int h) => transpose
            ? new(y * scale, x * scale, h * scale, w * scale)
            : new(x * scale, y * scale, w * scale, h * scale);
        var anchor = Box(100, 100, 120, 24);
        var padded = Box(96, 94, 128, 36);
        Assert.False(MenuGeometryMatcher.Matches(anchor, [anchor], padded, [padded]));
        Assert.True(MenuProvisionalGeometry.Matches(anchor, [anchor], padded, [padded]));
        Assert.True(MenuProvisionalGeometry.Matches(padded, [padded], anchor, [anchor]));
        var moved = Box(160, 100, 120, 24);
        Assert.False(MenuProvisionalGeometry.Matches(anchor, [anchor], moved, [moved]));
    }

    [Fact]
    public void Proposal_RejectsGroupedAndIncompleteMembers()
    {
        var box = new BoundingBox(100, 100, 120, 24);
        var member = new BoundingBox(100, 100, 60, 24);
        Assert.False(MenuProvisionalGeometry.Matches(box, [member], box, [box]));
        Assert.False(MenuProvisionalGeometry.Matches(box, [box], box, [member]));
        Assert.False(MenuProvisionalGeometry.Matches(box, [box, member], box, [box]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Proposal_AmbiguityIncludesStrictMatches(bool reverse)
    {
        var a = new BoundingBox(100, 100, 120, 24);
        var b = new BoundingBox(105, 100, 120, 24);
        var sources = reverse ? new[] { a } : new[] { a, b };
        var targets = reverse ? new[] { a, b } : new[] { a };
        Assert.Empty(MenuGeometryMatcher.MatchUnique(sources, targets,
            (left, right) => MenuProvisionalGeometry.Matches(left, [left], right, [right])));
    }
}
