using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class ComicGeometryQualityGateTests
{
    private static readonly DateTimeOffset FrameTime = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> OwnerReferenceCases()
    {
        yield return new object[] { "S9", CreateS9JapaneseOwnerBounds() };
        yield return new object[] { "S10", CreateS10ChineseOwnerBounds() };
    }

    [Theory]
    [MemberData(nameof(OwnerReferenceCases))]
    public void Evaluate_WhenDetectedGeometryMatchesOwnerReference_Passes(
        string scenario,
        IReadOnlyList<BoundingBox> ownerBounds)
    {
        var gate = new ComicGeometryQualityGate();
        var result = CreateComicResult(scenario, ownerBounds);

        var evaluation = gate.Evaluate(result, ownerBounds);

        Assert.True(evaluation.Passed);
        Assert.Equal(ownerBounds.Count, evaluation.ExpectedCount);
        Assert.Equal(ownerBounds.Count, evaluation.DetectedCount);
        Assert.Equal(ownerBounds.Count, evaluation.MatchedCount);
        Assert.Equal(0, evaluation.MissedCount);
        Assert.Equal(0, evaluation.ExtraDetectionCount);
        Assert.Equal(0, evaluation.ReadingOrderViolationCount);
        Assert.All(evaluation.Matches, match =>
        {
            Assert.True(match.IsMatched);
            Assert.Equal(1, match.IntersectionOverUnion);
            Assert.Equal(1, match.ExpectedCoverage);
            Assert.Equal(1, match.DetectedCoverage);
        });
    }

    [Fact]
    public void Evaluate_WhenFullPageComicGeometryIsWrongButNonEmpty_FailsWithMissesAndExtras()
    {
        var gate = new ComicGeometryQualityGate();
        var ownerBounds = CreateS10ChineseOwnerBounds();
        var broadNoisyDetections = new[]
        {
            new BoundingBox(900, 100, 260, 350),
            new BoundingBox(650, 900, 300, 230),
            new BoundingBox(250, 950, 240, 330),
        };
        var result = CreateComicResult("S10", broadNoisyDetections);

        var evaluation = gate.Evaluate(result, ownerBounds);

        Assert.False(evaluation.Passed);
        Assert.Equal(ownerBounds.Count, evaluation.ExpectedCount);
        Assert.Equal(3, evaluation.DetectedCount);
        Assert.Equal(0, evaluation.MatchedCount);
        Assert.Equal(ownerBounds.Count, evaluation.MissedCount);
        Assert.Equal(3, evaluation.ExtraDetectionCount);
        Assert.All(evaluation.Matches, match => Assert.False(match.IsMatched));
    }

    [Fact]
    public void Evaluate_WhenFullPageComicGeometryOrderIsInterleaved_FailsWithReadingOrderViolation()
    {
        var gate = new ComicGeometryQualityGate();
        var ownerBounds = CreateS9JapaneseOwnerBounds().Take(3).ToArray();
        var result = CreateComicResult("S9", new[]
        {
            ownerBounds[1],
            ownerBounds[0],
            ownerBounds[2],
        });

        var evaluation = gate.Evaluate(result, ownerBounds);

        Assert.False(evaluation.Passed);
        Assert.Equal(3, evaluation.MatchedCount);
        Assert.Equal(0, evaluation.MissedCount);
        Assert.Equal(0, evaluation.ExtraDetectionCount);
        Assert.Equal(1, evaluation.ReadingOrderViolationCount);
    }

    [Fact]
    public void Evaluate_WhenFullPageComicGeometryHasExtraNoise_FailsWithExtraDetection()
    {
        var gate = new ComicGeometryQualityGate();
        var ownerBounds = CreateS9JapaneseOwnerBounds().Take(1).ToArray();
        var result = CreateComicResult("S9", new[]
        {
            ownerBounds[0],
            new BoundingBox(20, 20, 160, 80),
        });

        var evaluation = gate.Evaluate(result, ownerBounds);

        Assert.False(evaluation.Passed);
        Assert.Equal(1, evaluation.MatchedCount);
        Assert.Equal(1, evaluation.ExtraDetectionCount);
        Assert.Equal(new BoundingBox(20, 20, 160, 80), Assert.Single(evaluation.ExtraDetections).Bounds);
    }

    [Fact]
    public void Evaluate_WhenCandidateTightlyCoversOwnerReference_AcceptsFractionalOverlap()
    {
        var gate = new ComicGeometryQualityGate();
        var ownerBounds = new[] { new BoundingBox(606, 65, 85, 143) };
        var result = CreateComicResult("S9", new[] { new BoundingBox(602, 63, 90, 144) });

        var evaluation = gate.Evaluate(result, ownerBounds);

        var match = Assert.Single(evaluation.Matches);
        Assert.True(evaluation.Passed);
        Assert.True(match.IsMatched);
        Assert.InRange(match.IntersectionOverUnion, 0.9d, 1d);
        Assert.InRange(match.ExpectedCoverage, 0.95d, 1d);
        Assert.InRange(match.DetectedCoverage, 0.9d, 1d);
    }

    private static OcrResult CreateComicResult(string scenario, IReadOnlyList<BoundingBox> semanticBounds)
    {
        var frame = CreateFrame();
        var blocks = semanticBounds
            .Select((bounds, index) => new OcrTextBlock($"Text {index + 1}", bounds))
            .ToArray();
        var sources = semanticBounds
            .Select(bounds => new OcrTextBlockSource(bounds, new[] { bounds }, OcrOrientationMode.Vertical))
            .ToArray();

        return new OcrResult(
            new OcrRequest(
                frame,
                "ja+zh-CN",
                scenario,
                orientationMode: OcrOrientationMode.Vertical,
                layoutMode: OcrLayoutMode.Comic),
            blocks,
            FrameTime,
            sources);
    }

    private static CapturedFrame CreateFrame()
    {
        const int width = 1200;
        const int height = 1500;
        const int stride = width;

        return new CapturedFrame(
            new CaptureRegion(0, 0, width, height),
            width,
            height,
            stride,
            "Gray8",
            new byte[checked(stride * height)],
            FrameTime);
    }

    private static IReadOnlyList<BoundingBox> CreateS9JapaneseOwnerBounds()
    {
        return new[]
        {
            new BoundingBox(606, 65, 85, 143),
            new BoundingBox(373, 82, 34, 118),
            new BoundingBox(188, 85, 61, 142),
            new BoundingBox(555, 430, 100, 102),
            new BoundingBox(452, 503, 28, 101),
            new BoundingBox(117, 537, 28, 48),
            new BoundingBox(588, 668, 63, 156),
            new BoundingBox(286, 793, 54, 124),
            new BoundingBox(102, 803, 34, 71),
            new BoundingBox(421, 937, 60, 73),
        };
    }

    private static IReadOnlyList<BoundingBox> CreateS10ChineseOwnerBounds()
    {
        return new[]
        {
            new BoundingBox(999, 143, 87, 246),
            new BoundingBox(412, 545, 80, 97),
            new BoundingBox(250, 717, 52, 177),
            new BoundingBox(726, 939, 107, 154),
            new BoundingBox(336, 979, 61, 257),
            new BoundingBox(1011, 993, 66, 204),
        };
    }
}
