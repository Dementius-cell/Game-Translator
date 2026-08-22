using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class WritingSystemGroupingProfileResolverTests
{
    [Theory]
    [InlineData("en", OcrOrientationMode.Horizontal, WritingSystemGroupingProfile.SpacedLeftToRight)]
    [InlineData("rus", OcrOrientationMode.Auto, WritingSystemGroupingProfile.SpacedLeftToRight)]
    [InlineData("ja", OcrOrientationMode.Horizontal, WritingSystemGroupingProfile.CjkHorizontalOrHybrid)]
    [InlineData("zh-CN", OcrOrientationMode.Horizontal, WritingSystemGroupingProfile.CjkHorizontalOrHybrid)]
    [InlineData("kor", OcrOrientationMode.Auto, WritingSystemGroupingProfile.CjkHorizontalOrHybrid)]
    [InlineData("jpn_vert", OcrOrientationMode.Auto, WritingSystemGroupingProfile.CjkVertical)]
    [InlineData("zh-TW", OcrOrientationMode.Vertical, WritingSystemGroupingProfile.CjkVertical)]
    [InlineData("th-TH", OcrOrientationMode.Horizontal, WritingSystemGroupingProfile.ComplexSouthEastAsian)]
    [InlineData("khm", OcrOrientationMode.Auto, WritingSystemGroupingProfile.ComplexSouthEastAsian)]
    [InlineData("hi", OcrOrientationMode.Horizontal, WritingSystemGroupingProfile.BrahmicIndic)]
    [InlineData("tam", OcrOrientationMode.Auto, WritingSystemGroupingProfile.BrahmicIndic)]
    [InlineData("he", OcrOrientationMode.Horizontal, WritingSystemGroupingProfile.RightToLeftHebrew)]
    [InlineData("yid", OcrOrientationMode.Auto, WritingSystemGroupingProfile.RightToLeftHebrew)]
    [InlineData("ar", OcrOrientationMode.Horizontal, WritingSystemGroupingProfile.RightToLeftArabicDerived)]
    [InlineData("urd", OcrOrientationMode.Auto, WritingSystemGroupingProfile.RightToLeftArabicDerived)]
    public void Resolve_MapsLanguageTagOrTrainedDataCodeToWritingSystemProfile(
        string language,
        OcrOrientationMode orientationMode,
        WritingSystemGroupingProfile expected)
    {
        var actual = WritingSystemGroupingProfileResolver.Resolve(language, orientationMode);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Resolve_UnknownLanguageUsesTheConservativeSpacedProfile()
    {
        var actual = WritingSystemGroupingProfileResolver.Resolve("x-test", OcrOrientationMode.Auto);

        Assert.Equal(WritingSystemGroupingProfile.SpacedLeftToRight, actual);
    }
}
