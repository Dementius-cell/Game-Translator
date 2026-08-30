using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class WritingSystemGroupingProfileSmokeTests
{
    public static IEnumerable<object[]> RepresentativeLanguageCases()
    {
        yield return new object[]
        {
            "LTR spaced", "en", OcrOrientationMode.Horizontal, WritingSystemGroupingProfile.SpacedLeftToRight,
        };
        yield return new object[]
        {
            "CJK horizontal or hybrid", "ja", OcrOrientationMode.Horizontal, WritingSystemGroupingProfile.CjkHorizontalOrHybrid,
        };
        yield return new object[]
        {
            "CJK vertical", "ja", OcrOrientationMode.Vertical, WritingSystemGroupingProfile.CjkVertical,
        };
        yield return new object[]
        {
            "Complex South-East Asian", "th", OcrOrientationMode.Horizontal, WritingSystemGroupingProfile.ComplexSouthEastAsian,
        };
        yield return new object[]
        {
            "Brahmic or Indic", "hi", OcrOrientationMode.Horizontal, WritingSystemGroupingProfile.BrahmicIndic,
        };
        yield return new object[]
        {
            "RTL Hebrew", "he", OcrOrientationMode.Horizontal, WritingSystemGroupingProfile.RightToLeftHebrew,
        };
        yield return new object[]
        {
            "RTL Arabic-derived", "ar", OcrOrientationMode.Horizontal, WritingSystemGroupingProfile.RightToLeftArabicDerived,
        };
    }

    [Theory]
    [MemberData(nameof(RepresentativeLanguageCases))]
    public void Group_SyntheticDialogGeometry_PreservesBoundedRegionsForEveryWritingSystemProfile(
        string scenario,
        string language,
        OcrOrientationMode orientationMode,
        WritingSystemGroupingProfile expectedProfile)
    {
        Assert.False(string.IsNullOrWhiteSpace(scenario));
        var groupingProfile = WritingSystemGroupingProfileResolver.Resolve(language, orientationMode);
        var service = new BoundedTextCandidateGroupingService();

        var result = service.Group(
            new[]
            {
                Candidate(100, 100, 90, 20),
                Candidate(105, 126, 85, 20),
                Candidate(110, 220, 80, 20),
            },
            zoneHeight: 400,
            groupingProfile);

        Assert.Equal(
            new[]
            {
                new BoundingBox(100, 100, 90, 46),
                new BoundingBox(110, 220, 80, 20),
            },
            result.Select(candidate => candidate.Bounds));
        Assert.Equal(expectedProfile, groupingProfile);
        Assert.Equal(2, result[0].SourceCandidateCount);
        Assert.Single(result[1].SourceCandidateBounds);
    }

    [Theory]
    [MemberData(nameof(RepresentativeLanguageCases))]
    public void Group_SyntheticRaggedAdjacentLines_UsesTheComplexSouthEastAsianToleranceOnly(
        string scenario,
        string language,
        OcrOrientationMode orientationMode,
        WritingSystemGroupingProfile expectedProfile)
    {
        Assert.False(string.IsNullOrWhiteSpace(scenario));
        var groupingProfile = WritingSystemGroupingProfileResolver.Resolve(language, orientationMode);
        var service = new BoundedTextCandidateGroupingService();

        var result = service.Group(
            new[]
            {
                Candidate(100, 100, 90, 20),
                Candidate(150, 127, 90, 20),
            },
            zoneHeight: 400,
            groupingProfile);

        Assert.Equal(expectedProfile, groupingProfile);
        if (expectedProfile is WritingSystemGroupingProfile.ComplexSouthEastAsian)
        {
            var grouped = Assert.Single(result);
            Assert.Equal(new BoundingBox(100, 100, 140, 47), grouped.Bounds);
            Assert.Equal(2, grouped.SourceCandidateCount);
            return;
        }

        Assert.Equal(
            new[]
            {
                new BoundingBox(100, 100, 90, 20),
                new BoundingBox(150, 127, 90, 20),
            },
            result.Select(candidate => candidate.Bounds));
        Assert.All(result, candidate => Assert.Single(candidate.SourceCandidateBounds));
    }

    [Theory]
    [MemberData(nameof(RepresentativeLanguageCases))]
    public void Group_SyntheticNineLineDialog_AutoStrictContinuationKeepsAlignedBubbleTogether(
        string scenario,
        string language,
        OcrOrientationMode orientationMode,
        WritingSystemGroupingProfile expectedProfile)
    {
        Assert.False(string.IsNullOrWhiteSpace(scenario));
        var groupingProfile = WritingSystemGroupingProfileResolver.Resolve(language, orientationMode);
        var service = new BoundedTextCandidateGroupingService();
        var candidates = Enumerable.Range(0, 9)
            .Select(index => Candidate(100 + (index % 2) * 4, 100 + index * 22, 50, 16));

        var result = service.Group(candidates, zoneHeight: 400, groupingProfile);

        Assert.Equal(expectedProfile, groupingProfile);
        var grouped = Assert.Single(result);
        Assert.Equal(9, grouped.SourceCandidateCount);
    }

    private static TextCandidate Candidate(int x, int y, int width, int height)
    {
        return new TextCandidate(new BoundingBox(x, y, width, height), 0.9d);
    }
}
