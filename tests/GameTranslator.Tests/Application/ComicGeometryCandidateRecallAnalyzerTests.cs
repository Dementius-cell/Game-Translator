using GameTranslator.Application.Ocr;

namespace GameTranslator.Tests.Application;

public sealed class ComicGeometryCandidateRecallAnalyzerTests
{
    private readonly ComicGeometryCandidateRecallAnalyzer analyzer = new();

    [Fact]
    public void Evaluate_WhenTwoReliableWordsSupportReference_ReportsCandidateRecallAndCoverage()
    {
        var expected = new[]
        {
            new BoundingBox(100, 100, 50, 100),
            new BoundingBox(300, 100, 50, 100),
        };
        var words = new[]
        {
            CreateWord("first", 105, 110, 10, 30),
            CreateWord("second", 120, 150, 10, 30),
            CreateWord("single", 305, 110, 10, 30),
        };

        var result = analyzer.Evaluate(words, expected);

        Assert.Equal(3, result.InputWordCount);
        Assert.Equal(3, result.ReliableWordCount);
        Assert.Equal(1, result.SupportedReferenceCount);
        Assert.Equal(1, result.UnsupportedReferenceCount);
        Assert.Equal(0, result.OutsideCandidateCount);
        Assert.Equal(0.5d, result.ReferenceRecall);
        Assert.Collection(
            result.References,
            first =>
            {
                Assert.True(first.IsSupported);
                Assert.Equal(2, first.SupportingWordCount);
                Assert.Equal(new[] { 0, 1 }, first.SupportingWordIndexes);
                Assert.Equal(0.12d, first.ExpectedCoverage, precision: 6);
            },
            second =>
            {
                Assert.False(second.IsSupported);
                Assert.Equal(1, second.SupportingWordCount);
                Assert.Equal(new[] { 2 }, second.SupportingWordIndexes);
            });
    }

    [Fact]
    public void Evaluate_WhenWordsAreLowConfidenceOrMostlyOutsideReference_ExcludesThemFromSupport()
    {
        var expected = new[] { new BoundingBox(100, 100, 50, 100) };
        var words = new[]
        {
            CreateWord("low", 105, 110, 10, 30, confidence: 49),
            CreateWord("partial", 145, 110, 20, 30),
            CreateWord("inside", 115, 150, 10, 30),
        };

        var result = analyzer.Evaluate(words, expected);

        var reference = Assert.Single(result.References);
        Assert.Equal(3, result.InputWordCount);
        Assert.Equal(2, result.ReliableWordCount);
        Assert.False(reference.IsSupported);
        Assert.Equal(1, reference.SupportingWordCount);
        Assert.Equal(new[] { 2 }, reference.SupportingWordIndexes);
        Assert.Equal(1, result.OutsideCandidateCount);
    }

    [Fact]
    public void Evaluate_WhenSupportingWordsOverlap_UsesUnionCoverageInsteadOfDoubleCounting()
    {
        var expected = new[] { new BoundingBox(100, 100, 100, 100) };
        var words = new[]
        {
            CreateWord("first", 100, 100, 50, 100),
            CreateWord("second", 125, 100, 50, 100),
        };

        var result = analyzer.Evaluate(words, expected);

        var reference = Assert.Single(result.References);
        Assert.True(reference.IsSupported);
        Assert.Equal(0.75d, reference.ExpectedCoverage, precision: 6);
    }

    private static OcrWord CreateWord(
        string text,
        int x,
        int y,
        int width,
        int height,
        double confidence = 90)
    {
        return new OcrWord(
            text,
            new BoundingBox(x, y, width, height),
            confidence,
            "test:candidates");
    }
}
