using GameTranslator.Application.Ocr;

namespace GameTranslator.Tests.Application;

public sealed class TesseractLanguageCatalogTests
{
    [Fact]
    public void Languages_ExposeBroadTesseractFastLanguageSet()
    {
        Assert.True(TesseractLanguageCatalog.Languages.Count >= 120);
        Assert.Contains(TesseractLanguageCatalog.Languages, language => language.Code == "aze_cyrl");
        Assert.Contains(TesseractLanguageCatalog.Languages, language => language.Code == "chi_sim_vert");
        Assert.Contains(TesseractLanguageCatalog.Languages, language => language.Code == "jpn_vert");
        Assert.Contains(TesseractLanguageCatalog.Languages, language => language.Code == "srp_latn");
    }

    [Theory]
    [InlineData("aze_cyrl", "aze_cyrl")]
    [InlineData("aze-cyrl", "aze_cyrl")]
    [InlineData("chi_sim_vert", "chi_sim_vert")]
    [InlineData("srp-latn", "srp_latn")]
    public void TryGetTrainedDataCode_NormalizesKnownTesseractCodes(string input, string expectedCode)
    {
        var found = TesseractLanguageCatalog.TryGetTrainedDataCode(input, out var actualCode);

        Assert.True(found);
        Assert.Equal(expectedCode, actualCode);
    }
}
