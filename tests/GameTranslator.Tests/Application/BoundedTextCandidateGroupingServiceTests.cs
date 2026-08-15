using GameTranslator.Application.Ocr;

namespace GameTranslator.Tests.Application;

public sealed class BoundedTextCandidateGroupingServiceTests
{
    [Fact]
    public void Group_FormsBoundedSharedCoreGroupWithoutConnectedComponentUnion()
    {
        var service = new BoundedTextCandidateGroupingService();

        var result = service.Group(
            new[]
            {
                Candidate(556, 430, 24, 82),
                Candidate(581, 431, 23, 99),
                Candidate(605, 430, 23, 101),
                Candidate(629, 429, 23, 101),
                Candidate(2, 1041, 185, 35),
            },
            zoneHeight: 1080);

        Assert.Equal(
            new[]
            {
                new BoundingBox(556, 429, 96, 102),
                new BoundingBox(2, 1041, 185, 35),
            },
            result.Select(candidate => candidate.Bounds));
    }

    [Fact]
    public void Group_WhenMoreThanFourNeighboringCandidatesExist_DoesNotCreateAnUnboundedChain()
    {
        var service = new BoundedTextCandidateGroupingService();

        var result = service.Group(
            Enumerable.Range(0, 5)
                .Select(index => Candidate(index * 20, 10, 20, 100)),
            zoneHeight: 400);

        Assert.Equal(
            new[]
            {
                new BoundingBox(0, 10, 80, 100),
                new BoundingBox(80, 10, 20, 100),
            },
            result.Select(candidate => candidate.Bounds));
    }

    [Fact]
    public void Group_FormsBoundedHorizontalTextStackForOneCandidateRegion()
    {
        var service = new BoundedTextCandidateGroupingService();

        var result = service.Group(
            new[]
            {
                Candidate(612, 670, 87, 16),
                Candidate(616, 691, 83, 16),
                Candidate(630, 710, 56, 19),
                Candidate(612, 731, 90, 20),
                Candidate(625, 754, 65, 16),
                Candidate(117, 680, 68, 20),
            },
            zoneHeight: 900);

        Assert.Equal(
            new[]
            {
                new BoundingBox(612, 670, 90, 100),
                new BoundingBox(117, 680, 68, 20),
            },
            result.Select(candidate => candidate.Bounds));
    }

    [Fact]
    public void Group_DoesNotJoinHorizontalCandidatesWithoutASharedColumnOrBoundedGap()
    {
        var service = new BoundedTextCandidateGroupingService();

        var result = service.Group(
            new[]
            {
                Candidate(100, 100, 100, 20),
                Candidate(260, 104, 100, 20),
                Candidate(105, 170, 95, 20),
            },
            zoneHeight: 300);

        Assert.Equal(
            new[]
            {
                new BoundingBox(100, 100, 100, 20),
                new BoundingBox(260, 104, 100, 20),
                new BoundingBox(105, 170, 95, 20),
            },
            result.Select(candidate => candidate.Bounds));
    }

    [Fact]
    public void Group_WhenDenseTopAlignedLayoutIsDetected_PreservesRawCandidates()
    {
        var service = new BoundedTextCandidateGroupingService();
        var input = Enumerable.Range(0, 20)
            .Select(index => Candidate(index * 10, index % 3, 8, 40))
            .ToArray();

        var result = service.Group(input, zoneHeight: 1000);

        Assert.Equal(
            input.Select(candidate => candidate.Bounds).OrderBy(bounds => bounds.Y).ThenBy(bounds => bounds.X),
            result.Select(candidate => candidate.Bounds));
    }

    private static TextCandidate Candidate(int x, int y, int width, int height)
    {
        return new TextCandidate(new BoundingBox(x, y, width, height), 0.9d);
    }
}
