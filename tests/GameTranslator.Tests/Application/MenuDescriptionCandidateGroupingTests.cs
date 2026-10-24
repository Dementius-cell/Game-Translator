using GameTranslator.Application.Ocr;

namespace GameTranslator.Tests.Application;

public sealed class MenuDescriptionCandidateGroupingTests
{
    private static TextCandidate Line(int x, int y, int width, int height = 20) =>
        new(new BoundingBox(x, y, width, height), .9);

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 137)]
    [InlineData(3, 701)]
    public void Group_UsesTypicalHeightAcrossScaleAndTranslation(int scale, int offset)
    {
        int[] heights = [26, 25, 25, 20, 20, 19, 22];
        var y = 10;
        var lines = heights.Select(height =>
        {
            var line = Line(offset + 10 * scale, offset + y * scale, 400 * scale, height * scale);
            y += height + 9;
            return line;
        }).ToArray();

        var result = Assert.Single(MenuDescriptionCandidateGrouping.Group(lines));
        Assert.Equal(lines.Select(line => line.Bounds), result.SourceCandidateBounds);
    }

    [Fact]
    public void Group_JoinsWrappedDescriptionAndPreservesSourceRectangles()
    {
        var lines = new[] { Line(10, 10, 400), Line(12, 40, 390), Line(10, 70, 80) };
        var result = Assert.Single(MenuDescriptionCandidateGrouping.Group(lines));
        Assert.Equal(new BoundingBox(10, 10, 400, 80), result.Bounds);
        Assert.Equal(lines.Select(c => c.Bounds), result.SourceCandidateBounds);
    }

    [Fact]
    public void Group_DoesNotJoinShortButtonsOrDifferentColumns()
    {
        var lines = new[] { Line(10, 10, 90), Line(10, 32, 90), Line(120, 10, 300), Line(450, 32, 300) };
        Assert.Equal(lines.Length, MenuDescriptionCandidateGrouping.Group(lines).Count);
    }

    [Fact]
    public void Group_StopsAtParagraphGapOrFontChange()
    {
        var lines = new[] { Line(10, 10, 400), Line(10, 40, 320), Line(10, 85, 400), Line(10, 110, 300, 35) };
        Assert.Equal(new[] { 2, 1, 1 }, MenuDescriptionCandidateGrouping.Group(lines).Select(c => c.SourceCandidateCount));
    }

    [Fact]
    public void Group_DoesNotEncloseIndependentValueOrIcon()
    {
        var lines = new[] { Line(10, 10, 400), Line(10, 40, 80), Line(200, 40, 30) };
        Assert.Equal(3, MenuDescriptionCandidateGrouping.Group(lines).Count);
    }

    [Fact]
    public void Group_ReunitesInlineGapOnlyWithWrappedContinuation()
    {
        var lines = new[] { Line(10, 10, 300), Line(335, 10, 100), Line(10, 40, 430), Line(10, 70, 70) };
        var result = Assert.Single(MenuDescriptionCandidateGrouping.Group(lines));
        Assert.Equal(4, result.SourceCandidateCount);
        Assert.Equal(2, MenuDescriptionCandidateGrouping.Group(lines.Take(2).ToArray()).Count);
    }
}
