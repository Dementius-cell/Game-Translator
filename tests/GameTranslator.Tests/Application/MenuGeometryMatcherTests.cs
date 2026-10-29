using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;

namespace GameTranslator.Tests.Application;

public sealed class MenuGeometryMatcherTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void OneLongLineJitterRequiresStableParagraphAndSibling(int scale)
    {
        BoundingBox B(int x, int y, int w, int h) => new(x * scale, y * scale, w * scale, h * scale);
        var outer = B(20, 40, 480, 58);
        var movedOuter = B(22, 42, 480, 56);
        var first = B(20, 40, 480, 23);
        var jitter = B(22, 42, 480, 24);
        var second = B(22, 74, 220, 24);
        Assert.True(MenuGeometryMatcher.Matches(outer, [first, second], movedOuter, [jitter, second]));
        Assert.True(MenuGeometryMatcher.Matches(movedOuter, [jitter, second], outer, [first, second]));
        Assert.False(MenuGeometryMatcher.Matches(first, [first], jitter, [jitter]));
        Assert.False(MenuGeometryMatcher.Matches(outer, [first, second], movedOuter, [jitter]));
        Assert.False(MenuGeometryMatcher.Matches(outer, [first, second], movedOuter, [jitter, B(24, 76, 220, 25)]));
        Assert.False(MenuGeometryMatcher.Matches(outer, [first, second], movedOuter, [B(24, 44, 480, 24), second]));
        Assert.False(MenuGeometryMatcher.Matches(outer, [first, second], movedOuter, [B(22, 42, 460, 24), second]));
        Assert.False(MenuGeometryMatcher.Matches(outer, [first, second], movedOuter, [B(22, 42, 480, 30), second]));
        Assert.False(MenuGeometryMatcher.Matches(outer, [first, second], B(22, 74, 480, 56), [B(22, 74, 480, 24), second]));
    }

    [Fact]
    public void SupportedParagraphJitterStillRejectsAmbiguityAndRollingDrift()
    {
        BoundingBox anchor = new(20, 40, 480, 58), jitter = new(22, 42, 480, 56);
        BoundingBox[] original = [new(20, 40, 480, 23), new(22, 74, 220, 24)];
        BoundingBox[] changed = [new(22, 42, 480, 24), original[1]];
        var sources = new[] { (anchor, original), (jitter, changed) };
        bool Compatible((BoundingBox bounds, BoundingBox[] members) a, (BoundingBox bounds, BoundingBox[] members) b)
            => MenuGeometryMatcher.Matches(a.bounds, a.members, b.bounds, b.members);
        Assert.Empty(MenuGeometryMatcher.MatchUnique(sources, new[] { sources[1] }, Compatible));
        Assert.Empty(MenuGeometryMatcher.MatchUnique(new[] { sources[0] }, sources, Compatible));
        Assert.False(MenuGeometryMatcher.Matches(anchor, original, new(24, 44, 480, 54), [new(24, 44, 480, 24), original[1]]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void NestedLineEdgeContractionPreservesIdentityWithoutAllowingMotion(int scale)
    {
        BoundingBox B(int x, int y, int w, int h) => new(x * scale, y * scale, w * scale, h * scale);
        var outer = B(20, 40, 120, 32);
        var inner = B(22, 44, 116, 26);
        Assert.True(MenuGeometryMatcher.Matches(outer, [outer], inner, [inner]));
        Assert.True(MenuGeometryMatcher.Matches(inner, [inner], outer, [outer]));
        Assert.False(Match(outer, B(22, 46, 116, 26))); // displaced center
        Assert.False(Match(outer, B(32, 44, 96, 26))); // missing word edges
        Assert.False(Match(outer, B(22, 46, 116, 20))); // excessive contraction
        Assert.False(Match(outer, B(22, 74, 116, 26))); // adjacent row
        Assert.False(MenuGeometryMatcher.Matches(outer, [outer], inner, []));
        Assert.Empty(MenuGeometryMatcher.MatchUnique(new[] { outer, inner }, new[] { inner }, Match));
        Assert.Empty(MenuGeometryMatcher.MatchUnique(new[] { outer }, new[] { outer, inner }, Match));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void DetectorHeightJitterPreservesIdentityAtEveryScale(int scale)
    {
        var anchor = new BoundingBox(14 * scale, 318 * scale, 206 * scale, 30 * scale);
        var jitter = new BoundingBox(15 * scale, 319 * scale, 204 * scale, 27 * scale);
        Assert.True(MenuGeometryMatcher.Matches(anchor, [anchor], jitter, [jitter]));
        Assert.True(MenuGeometryMatcher.Matches(jitter, [jitter], anchor, [anchor]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void HeightJitterDoesNotTransferAuthorityToAdjacentRow(int scale)
    {
        var anchor = new BoundingBox(0, 0, 206 * scale, 30 * scale);
        var neighbor = new BoundingBox(0, 32 * scale, 206 * scale, 30 * scale);
        var jitter = new BoundingBox(scale, scale, 204 * scale, 27 * scale);
        var result = MenuGeometryMatcher.MatchUnique(new[] { anchor, neighbor }, new[] { jitter, neighbor }, Match);
        Assert.Equal(0, result[0]);
        Assert.Equal(1, result[1]);
        Assert.False(Match(anchor, neighbor));
    }

    private static BoundingBox Box(int x, int scale = 1) => new(x * scale, 0, 100 * scale, 40 * scale);
    private static bool Match(BoundingBox a, BoundingBox b) => MenuGeometryMatcher.Matches(a, [], b, []);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void TwoAnchorsForOneFrame_RejectsEvenExactIdAndNearestMatch(int scale)
    {
        Assert.True(Match(Box(0, scale), Box(1, scale)));
        var result = MenuGeometryMatcher.MatchUnique(new[] { Box(0, scale), Box(1, scale) }, new[] { Box(0, scale) }, Match);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void OneAnchorForTwoFrames_RejectsSplitAndPreservesIndependentNeighbor(int scale)
    {
        var result = MenuGeometryMatcher.MatchUnique(new[] { Box(0, scale), Box(200, scale) },
            new[] { Box(0, scale), Box(1, scale), Box(201, scale) }, Match);
        Assert.Single(result);
        Assert.Equal(1, result[2]);
    }

    [Fact]
    public void AmbiguityIsReciprocal_EvenIfOneTargetHasOnlyOneChoice()
    {
        // A->X,Y; B->Y. X must not steal A just because X has only one possible source.
        var result = MenuGeometryMatcher.MatchUnique(new[] { "A", "B" }, new[] { "X", "Y" },
            (source, target) => source == "A" || target == "Y");
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void FixedAnchorStopsGradualWalkTowardNeighbor(int scale)
    {
        var anchor = Box(0, scale);
        for (var offset = 1; offset <= 10; offset++)
        {
            Assert.True(Match(Box(offset - 1, scale), Box(offset, scale)));
            Assert.Equal(offset <= 6, Match(anchor, Box(offset, scale)));
        }
    }

    [Fact]
    public void MemberSplitOrRelocationCannotReuseOuterBoxAuthority()
    {
        var outer = Box(0);
        BoundingBox[] members = [new(5, 2, 80, 12), new(5, 24, 80, 12)];
        Assert.True(MenuGeometryMatcher.Matches(outer, members, outer, members.Reverse().ToArray()));
        Assert.False(MenuGeometryMatcher.Matches(outer, members, outer, [members[0]]));
        Assert.False(MenuGeometryMatcher.Matches(outer, members, outer, [members[0], new(10, 24, 80, 12)]));
    }

    [Fact]
    public void InputOrderDoesNotChangeUniqueGeometricIdentity()
    {
        var result = MenuGeometryMatcher.MatchUnique(new[] { Box(200), Box(0) }, new[] { Box(1), Box(201) }, Match);
        Assert.Equal(1, result[0]);
        Assert.Equal(0, result[1]);
    }
}
