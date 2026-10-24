using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;

namespace GameTranslator.Tests.Application;

public sealed class MenuGeometryMatcherTests
{
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
