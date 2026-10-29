using GameTranslator.Application.Ocr;
using GameTranslator.Application.Capture;

namespace GameTranslator.Tests.Application;

public sealed class MenuDescriptionCandidateGroupingTests
{
    private static TextCandidate Line(int x, int y, int width, int height = 20) =>
        new(new BoundingBox(x, y, width, height), .9);

    [Theory]
    [InlineData(1, 25)]
    [InlineData(2, 25)]
    [InlineData(1, 28)]
    public void Group_UsesInkHeightForTightFirstLineAndPaddedContinuation(int scale, int nextHeight)
    {
        var lines = new[] { Line(10 * scale, 10 * scale, 469 * scale, 17 * scale),
            Line(10 * scale, 38 * scale, 284 * scale, nextHeight * scale) };
        var frame = InkFrame(lines, scale, 22, 0);
        Assert.Equal(2, MenuDescriptionCandidateGrouping.Group(lines).Count);
        var group = Assert.Single(MenuDescriptionCandidateGrouping.Group(lines, frame));
        Assert.Equal(lines.Select(c => c.Bounds), group.SourceCandidateBounds);
    }

    [Theory]
    [InlineData(25, 0)] // Real font-size difference, not extra background padding.
    [InlineData(22, 100)] // Bright background is outside the supported pixel evidence.
    [InlineData(0, 0)] // No visible continuation.
    public void Group_RejectsUnsupportedInkEvidence(int nextInk, byte background)
    {
        var lines = new[] { Line(10, 10, 469, 17), Line(10, 38, 284, 28) };
        Assert.Equal(2, MenuDescriptionCandidateGrouping.Group(lines, InkFrame(lines, 1, nextInk, background)).Count);
    }

    [Fact]
    public void Group_PixelEvidenceDoesNotAbsorbNeighborOrBridgeParagraphGap()
    {
        var lines = new[] { Line(10, 10, 469, 17), Line(10, 38, 284, 25) };
        var frame = InkFrame(lines, 1, 22, 0);
        Assert.Equal(3, MenuDescriptionCandidateGrouping.Group([.. lines, Line(320, 38, 30, 15)], frame).Count);
        Assert.Equal(2, MenuDescriptionCandidateGrouping.Group([lines[0], Line(10, 75, 284, 25)], frame).Count);
    }

    private static CapturedFrame InkFrame(TextCandidate[] lines, int scale, int nextInk, byte background)
    {
        int width = 500 * scale, height = 100 * scale;
        var pixels = Enumerable.Repeat(background, width * height * 4).ToArray();
        for (var index = 0; index < lines.Length; index++)
        {
            var b = lines[index].Bounds;
            var inkHeight = index == 0 ? b.Height : nextInk * scale;
            for (var y = b.Bottom - inkHeight; y < b.Bottom; y++)
            for (var x = b.X; x < b.Right; x++)
            {
                if ((x / scale) % 5 != 0) continue;
                var offset = (y * width + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 200;
            }
        }
        return new CapturedFrame(new(0, 0, width, height), width, height, width * 4, "Bgra32", pixels, DateTimeOffset.UnixEpoch);
    }

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
