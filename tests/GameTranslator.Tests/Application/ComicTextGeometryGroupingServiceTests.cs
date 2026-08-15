using GameTranslator.Application.Ocr;

namespace GameTranslator.Tests.Application;

public sealed class ComicTextGeometryGroupingServiceTests
{
    private readonly ComicTextGeometryGroupingService service = new();

    [Fact]
    public void GroupVerticalWords_WhenAdjacentColumnsShareVerticalExtent_MergesAndUsesVerticalReadingOrder()
    {
        var groups = service.GroupVerticalWords(new[]
        {
            CreateWord("left-bottom", 75, 35),
            CreateWord("right-bottom", 100, 35),
            CreateWord("left-top", 75, 10),
            CreateWord("right-top", 100, 10),
        });

        var group = Assert.Single(groups);

        Assert.Equal("right-topright-bottomleft-topleft-bottom", group.Text);
        Assert.Equal(new BoundingBox(75, 10, 35, 45), group.SemanticBounds);
        Assert.Equal(new[]
        {
            new BoundingBox(100, 10, 10, 20),
            new BoundingBox(100, 35, 10, 20),
            new BoundingBox(75, 10, 10, 20),
            new BoundingBox(75, 35, 10, 20),
        }, group.MemberBounds);
    }

    [Fact]
    public void GroupVerticalWords_WhenFragmentsAreSeparatedByLargeVerticalGap_KeepsDistinctGroupsInPageOrder()
    {
        var groups = service.GroupVerticalWords(new[]
        {
            CreateWord("lower-top", 100, 140),
            CreateWord("upper-bottom", 100, 35),
            CreateWord("lower-bottom", 100, 165),
            CreateWord("upper-top", 100, 10),
        });

        Assert.Collection(
            groups,
            upper =>
            {
                Assert.Equal("upper-topupper-bottom", upper.Text);
                Assert.Equal(new BoundingBox(100, 10, 10, 45), upper.SemanticBounds);
            },
            lower =>
            {
                Assert.Equal("lower-toplower-bottom", lower.Text);
                Assert.Equal(new BoundingBox(100, 140, 10, 45), lower.SemanticBounds);
            });
    }

    [Fact]
    public void GroupVerticalWords_WhenLowConfidenceOrWideNoiseWouldBridgeColumns_ExcludesIt()
    {
        var groups = service.GroupVerticalWords(new[]
        {
            CreateWord("right-top", 150, 10),
            CreateWord("right-bottom", 150, 35),
            CreateWord("left-top", 50, 10),
            CreateWord("left-bottom", 50, 35),
            CreateWord("wide-noise", 70, 22, width: 100, height: 18),
            CreateWord("low-confidence", 100, 20, confidence: 49),
        });

        Assert.Collection(
            groups,
            right =>
            {
                Assert.Equal("right-topright-bottom", right.Text);
                Assert.Equal(new BoundingBox(150, 10, 10, 45), right.SemanticBounds);
            },
            left =>
            {
                Assert.Equal("left-topleft-bottom", left.Text);
                Assert.Equal(new BoundingBox(50, 10, 10, 45), left.SemanticBounds);
            });
    }

    [Fact]
    public void GroupVerticalWords_WhenOnlyOneReliableFragmentRemains_DoesNotCreateOverlayGeometry()
    {
        var groups = service.GroupVerticalWords(new[]
        {
            CreateWord("single", 100, 10),
            CreateWord("noise", 50, 20, confidence: 20),
        });

        Assert.Empty(groups);
    }

    private static OcrWord CreateWord(
        string text,
        int x,
        int y,
        double confidence = 90,
        int width = 10,
        int height = 20)
    {
        return new OcrWord(
            text,
            new BoundingBox(x, y, width, height),
            confidence,
            "test:vertical");
    }
}
