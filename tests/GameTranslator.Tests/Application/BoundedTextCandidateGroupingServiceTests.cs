using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

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

    [Fact]
    public void Group_ComplexSouthEastAsian_MergesBoundedRaggedDialogLines()
    {
        var service = new BoundedTextCandidateGroupingService();

        var result = service.Group(
            new[]
            {
                Candidate(100, 100, 90, 20),
                Candidate(150, 127, 90, 20),
                Candidate(110, 220, 90, 20),
            },
            zoneHeight: 400,
            WritingSystemGroupingProfile.ComplexSouthEastAsian);

        Assert.Equal(
            new[]
            {
                new BoundingBox(100, 100, 140, 47),
                new BoundingBox(110, 220, 90, 20),
            },
            result.Select(candidate => candidate.Bounds));
    }

    [Fact]
    public void Group_SpacedProfile_DoesNotMergeTheSameRaggedDialogLines()
    {
        var service = new BoundedTextCandidateGroupingService();

        var result = service.Group(
            new[]
            {
                Candidate(100, 100, 90, 20),
                Candidate(150, 127, 90, 20),
            },
            zoneHeight: 400,
            WritingSystemGroupingProfile.SpacedLeftToRight);

        Assert.Equal(
            new[]
            {
                new BoundingBox(100, 100, 90, 20),
                new BoundingBox(150, 127, 90, 20),
            },
            result.Select(candidate => candidate.Bounds));
    }

    [Fact]
    public void Group_ComplexSouthEastAsian_AutoStrictContinuationKeepsNineAlignedDialogLinesTogether()
    {
        var service = new BoundedTextCandidateGroupingService();
        var candidates = Enumerable.Range(0, 9)
            .Select(index => Candidate(100 + (index % 2) * 4, 100 + index * 22, 50, 16));

        var result = service.Group(
            candidates,
            zoneHeight: 400,
            WritingSystemGroupingProfile.ComplexSouthEastAsian);

        Assert.Equal(
            new[] { new BoundingBox(100, 100, 54, 192) },
            result.Select(candidate => candidate.Bounds));
    }

    [Fact]
    public void Group_SpacedProfile_AutoStrictContinuationKeepsNineAlignedDialogLinesTogether()
    {
        var service = new BoundedTextCandidateGroupingService();
        var candidates = Enumerable.Range(0, 9)
            .Select(index => Candidate(100 + (index % 2) * 4, 100 + index * 22, 50, 16));

        var result = service.Group(
            candidates,
            zoneHeight: 400,
            WritingSystemGroupingProfile.SpacedLeftToRight);

        Assert.Equal(
            new[] { new BoundingBox(100, 100, 54, 192) },
            result.Select(candidate => candidate.Bounds));
    }

    [Fact]
    public void Group_SpacedProfile_CustomSixLineLimitRemainsAHardBoundary()
    {
        var service = new BoundedTextCandidateGroupingService();
        var candidates = Enumerable.Range(0, 9)
            .Select(index => Candidate(100 + (index % 2) * 4, 100 + index * 22, 50, 16));

        var result = service.Group(
            candidates,
            zoneHeight: 400,
            WritingSystemGroupingProfile.SpacedLeftToRight,
            new OcrCandidateGroupingSettings { MaximumHorizontalLines = 6 });

        Assert.Equal(
            new[]
            {
                new BoundingBox(100, 100, 54, 126),
                new BoundingBox(100, 232, 54, 60),
            },
            result.Select(candidate => candidate.Bounds));
    }

    [Fact]
    public void Group_SpacedProfile_AutoDoesNotContinuePastSixWhenAlignmentIsOnlyModerate()
    {
        var service = new BoundedTextCandidateGroupingService();
        var candidates = Enumerable.Range(0, 6)
            .Select(index => Candidate(100, 100 + index * 22, 54, 16))
            .Append(Candidate(125, 232, 50, 16));

        var result = service.Group(
            candidates,
            zoneHeight: 400,
            WritingSystemGroupingProfile.SpacedLeftToRight);

        Assert.Equal(
            new[]
            {
                new BoundingBox(100, 100, 54, 126),
                new BoundingBox(125, 232, 50, 16),
            },
            result.Select(candidate => candidate.Bounds));
    }

    [Fact]
    public void Group_VerticalProfile_UsesConfiguredMaximumColumnCount()
    {
        var service = new BoundedTextCandidateGroupingService();
        var candidates = Enumerable.Range(0, 4)
            .Select(index => Candidate(100 + index * 18, 100, 12, 80));

        var result = service.Group(
            candidates,
            zoneHeight: 400,
            WritingSystemGroupingProfile.CjkVertical,
            new OcrCandidateGroupingSettings { MaximumVerticalColumns = 2 });

        Assert.Equal(2, result.Count);
        Assert.All(result, candidate => Assert.Equal(2, candidate.SourceCandidateBounds.Count));
    }

    [Fact]
    public void Group_CjkVertical_AutoKeepsConfirmedFiveColumnLiveGeometryTogether()
    {
        var service = new BoundedTextCandidateGroupingService();
        var candidates = new[]
        {
            Candidate(170, 611, 36, 148),
            Candidate(207, 610, 33, 119),
            Candidate(241, 611, 36, 174),
            Candidate(279, 613, 31, 173),
            Candidate(312, 613, 33, 116),
        };

        var result = service.Group(
            candidates,
            zoneHeight: 1080,
            WritingSystemGroupingProfile.CjkVertical);

        var grouped = Assert.Single(result);
        Assert.Equal(new BoundingBox(170, 610, 175, 176), grouped.Bounds);
        Assert.Equal(5, grouped.SourceCandidateBounds.Count);
        Assert.Equal(
            new[] { 312, 279, 241, 207, 170 },
            grouped.SourceCandidateBounds.Select(bounds => bounds.X));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(12)]
    public void Group_CjkVertical_AutoDoesNotSplitCoherentBubbleByColumnCount(int columnCount)
    {
        var service = new BoundedTextCandidateGroupingService();
        var candidates = Enumerable.Range(0, columnCount)
            .Select(index => Candidate(100 + index * 34, 100, 30, 120))
            .Reverse();

        var result = service.Group(
            candidates,
            zoneHeight: 500,
            WritingSystemGroupingProfile.CjkVertical);

        var grouped = Assert.Single(result);
        Assert.Equal(columnCount, grouped.SourceCandidateBounds.Count);
        Assert.Equal(
            grouped.SourceCandidateBounds.OrderByDescending(bounds => bounds.X),
            grouped.SourceCandidateBounds);
    }

    [Fact]
    public void Group_CjkVertical_AutoSeparatesSameHeightBubblesAtPitchDiscontinuity()
    {
        var service = new BoundedTextCandidateGroupingService();

        var result = service.Group(
            new[]
            {
                Candidate(300, 100, 30, 120),
                Candidate(268, 100, 30, 120),
                Candidate(236, 100, 30, 120),
                Candidate(196, 100, 30, 120),
                Candidate(164, 100, 30, 120),
                Candidate(132, 100, 30, 120),
            },
            zoneHeight: 500,
            WritingSystemGroupingProfile.CjkVertical);

        Assert.Equal(
            new[]
            {
                new BoundingBox(132, 100, 94, 120),
                new BoundingBox(236, 100, 94, 120),
            },
            result.Select(candidate => candidate.Bounds));
        Assert.All(result, candidate => Assert.Equal(3, candidate.SourceCandidateBounds.Count));
    }

    [Fact]
    public void Group_CjkVertical_AutoKeepsRaggedCoherentColumnsTogether()
    {
        var service = new BoundedTextCandidateGroupingService();

        var result = service.Group(
            new[]
            {
                Candidate(300, 100, 30, 120),
                Candidate(268, 90, 30, 150),
                Candidate(236, 110, 30, 100),
                Candidate(204, 95, 30, 140),
            },
            zoneHeight: 500,
            WritingSystemGroupingProfile.CjkVertical);

        var grouped = Assert.Single(result);
        Assert.Equal(new BoundingBox(204, 90, 126, 150), grouped.Bounds);
        Assert.Equal(4, grouped.SourceCandidateBounds.Count);
    }

    [Fact]
    public void Group_CjkVertical_AutoKeepsFirstR36RaggedBottomBubbleTogether()
    {
        var service = new BoundedTextCandidateGroupingService();

        var result = service.Group(
            new[]
            {
                Candidate(1084, 57, 38, 92),
                Candidate(1053, 58, 31, 175),
                Candidate(1011, 55, 41, 98),
            },
            zoneHeight: 824,
            WritingSystemGroupingProfile.CjkVertical);

        var grouped = Assert.Single(result);
        Assert.Equal(new BoundingBox(1011, 55, 111, 178), grouped.Bounds);
        Assert.Equal(
            new[] { 1084, 1053, 1011 },
            grouped.SourceCandidateBounds.Select(bounds => bounds.X));
    }

    [Fact]
    public void Group_CjkVertical_AutoKeepsSecondR36RaggedBottomBubbleTogether()
    {
        var service = new BoundedTextCandidateGroupingService();

        var result = service.Group(
            new[]
            {
                Candidate(192, 5, 36, 120),
                Candidate(158, 5, 33, 230),
                Candidate(122, 5, 34, 91),
            },
            zoneHeight: 824,
            WritingSystemGroupingProfile.CjkVertical);

        var grouped = Assert.Single(result);
        Assert.Equal(new BoundingBox(122, 5, 106, 230), grouped.Bounds);
        Assert.Equal(
            new[] { 192, 158, 122 },
            grouped.SourceCandidateBounds.Select(bounds => bounds.X));
    }

    [Fact]
    public void Group_CjkVertical_AutoDoesNotExtendRaggedBottomBubbleAcrossWiderGap()
    {
        var service = new BoundedTextCandidateGroupingService();

        var result = service.Group(
            new[]
            {
                Candidate(1084, 57, 38, 92),
                Candidate(1053, 58, 31, 175),
                Candidate(1006, 55, 41, 98),
            },
            zoneHeight: 824,
            WritingSystemGroupingProfile.CjkVertical);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, candidate => candidate.SourceCandidateBounds.Count == 2);
        Assert.Contains(result, candidate => candidate.Bounds == new BoundingBox(1006, 55, 41, 98));
    }

    [Fact]
    public void Group_CjkVertical_AutoStopsStaggeredTransitiveCreep()
    {
        var service = new BoundedTextCandidateGroupingService();
        var candidates = Enumerable.Range(0, 6)
            .Select(index => Candidate(300 - index * 32, index * 20, 30, 100));

        var result = service.Group(
            candidates,
            zoneHeight: 500,
            WritingSystemGroupingProfile.CjkVertical);

        Assert.Equal(2, result.Count);
        Assert.All(result, candidate => Assert.Equal(3, candidate.SourceCandidateBounds.Count));
    }

    [Fact]
    public void Group_CjkVertical_AutoDoesNotAttachNarrowSingletonNoise()
    {
        var service = new BoundedTextCandidateGroupingService();

        var result = service.Group(
            new[]
            {
                Candidate(300, 100, 30, 120),
                Candidate(268, 100, 30, 120),
                Candidate(236, 100, 30, 120),
                Candidate(204, 100, 30, 120),
                Candidate(194, 140, 6, 20),
            },
            zoneHeight: 500,
            WritingSystemGroupingProfile.CjkVertical);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, candidate => candidate.SourceCandidateBounds.Count == 4);
        Assert.Contains(result, candidate => candidate.SourceCandidateBounds.Count == 1);
    }

    [Fact]
    public void Group_CjkVertical_AutoPreservesHorizontalCandidateBoundary()
    {
        var service = new BoundedTextCandidateGroupingService();

        var result = service.Group(
            Enumerable.Range(0, 5)
                .Select(index => Candidate(300 - index * 32, 100, 30, 120))
                .Append(Candidate(20, 350, 180, 24)),
            zoneHeight: 500,
            WritingSystemGroupingProfile.CjkVertical);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, candidate => candidate.SourceCandidateBounds.Count == 5);
        Assert.Contains(result, candidate => candidate.Bounds == new BoundingBox(20, 350, 180, 24));
    }

    [Theory]
    [InlineData(WritingSystemGroupingProfile.SpacedLeftToRight)]
    [InlineData(WritingSystemGroupingProfile.CjkHorizontalOrHybrid)]
    [InlineData(WritingSystemGroupingProfile.ComplexSouthEastAsian)]
    [InlineData(WritingSystemGroupingProfile.BrahmicIndic)]
    [InlineData(WritingSystemGroupingProfile.RightToLeftHebrew)]
    [InlineData(WritingSystemGroupingProfile.RightToLeftArabicDerived)]
    public void Group_NonCjkVerticalProfiles_RetainFourColumnVerticalCap(
        WritingSystemGroupingProfile groupingProfile)
    {
        var service = new BoundedTextCandidateGroupingService();
        var candidates = Enumerable.Range(0, 5)
            .Select(index => Candidate(index * 20, 10, 20, 100));

        var result = service.Group(candidates, zoneHeight: 400, groupingProfile);

        Assert.Equal(new[] { 4, 1 }, result.Select(candidate => candidate.SourceCandidateBounds.Count));
    }

    private static TextCandidate Candidate(int x, int y, int width, int height)
    {
        return new TextCandidate(new BoundingBox(x, y, width, height), 0.9d);
    }
}
