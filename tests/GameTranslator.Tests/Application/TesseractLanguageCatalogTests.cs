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

    [Fact]
    public void TryMapLanguageTagToTrainedDataCode_MapsEveryGoogleWebLanguageWithAnAvailableModel()
    {
        var expectedModels = new Dictionary<string, string>
        {
            ["af"] = "afr", ["am"] = "amh", ["ar"] = "ara", ["az"] = "aze",
            ["be"] = "bel", ["bg"] = "bul", ["bn"] = "ben", ["bs"] = "bos",
            ["ca"] = "cat", ["ceb"] = "ceb", ["co"] = "cos", ["cs"] = "ces",
            ["cy"] = "cym", ["da"] = "dan", ["de"] = "deu", ["el"] = "ell",
            ["en"] = "eng", ["eo"] = "epo", ["es"] = "spa", ["et"] = "est",
            ["eu"] = "eus", ["fa"] = "fas", ["fi"] = "fin", ["fr"] = "fra",
            ["fy"] = "fry", ["ga"] = "gle", ["gd"] = "gla", ["gl"] = "glg",
            ["gu"] = "guj", ["he"] = "heb", ["hi"] = "hin", ["hr"] = "hrv",
            ["ht"] = "hat", ["hu"] = "hun", ["hy"] = "hye", ["id"] = "ind",
            ["is"] = "isl", ["it"] = "ita", ["ja"] = "jpn", ["jv"] = "jav",
            ["ka"] = "kat", ["kk"] = "kaz", ["km"] = "khm", ["kn"] = "kan",
            ["ko"] = "kor", ["ku"] = "kmr", ["ky"] = "kir", ["la"] = "lat",
            ["lb"] = "ltz", ["lo"] = "lao", ["lt"] = "lit", ["lv"] = "lav",
            ["mi"] = "mri", ["mk"] = "mkd", ["ml"] = "mal", ["mn"] = "mon",
            ["mr"] = "mar", ["ms"] = "msa", ["mt"] = "mlt", ["my"] = "mya",
            ["ne"] = "nep", ["nl"] = "nld", ["no"] = "nor", ["or"] = "ori",
            ["pa"] = "pan", ["pl"] = "pol", ["ps"] = "pus", ["pt"] = "por",
            ["ro"] = "ron", ["ru"] = "rus", ["sd"] = "snd", ["si"] = "sin",
            ["sk"] = "slk", ["sl"] = "slv", ["sq"] = "sqi", ["sr"] = "srp",
            ["su"] = "sun", ["sv"] = "swe", ["sw"] = "swa", ["ta"] = "tam",
            ["te"] = "tel", ["tg"] = "tgk", ["th"] = "tha", ["tr"] = "tur",
            ["uk"] = "ukr", ["ur"] = "urd", ["uz"] = "uzb", ["vi"] = "vie",
            ["yi"] = "yid", ["yo"] = "yor", ["zh-CN"] = "chi_sim", ["zh-TW"] = "chi_tra",
        };

        foreach (var (languageTag, expectedModel) in expectedModels)
        {
            var found = TesseractLanguageCatalog.TryMapLanguageTagToTrainedDataCode(languageTag, out var actualModel);

            Assert.True(found, languageTag);
            Assert.Equal(expectedModel, actualModel);
            Assert.True(
                TesseractLanguageCatalog.TryGetTrainedDataCode(actualModel, out _),
                $"{languageTag} mapped to a model outside the Tesseract catalog: {actualModel}");
        }
    }

    [Theory]
    [InlineData("ha")]
    [InlineData("haw")]
    [InlineData("hmn")]
    [InlineData("ig")]
    [InlineData("mg")]
    [InlineData("ny")]
    [InlineData("sm")]
    [InlineData("sn")]
    [InlineData("so")]
    [InlineData("st")]
    [InlineData("xh")]
    [InlineData("zu")]
    public void TryMapLanguageTagToTrainedDataCode_RejectsGoogleWebLanguagesWithoutCatalogModels(string languageTag)
    {
        var found = TesseractLanguageCatalog.TryMapLanguageTagToTrainedDataCode(languageTag, out _);

        Assert.False(found);
    }

    [Theory]
    [InlineData("eng", "en")]
    [InlineData("tha", "th")]
    [InlineData("jpn_vert", "ja")]
    [InlineData("chi_sim", "zh-CN")]
    [InlineData("chi_tra_vert", "zh-TW")]
    public void TryMapTrainedDataCodeToPreferredLanguageTag_MapsTesseractModelsToGoogleWebTags(
        string trainedDataCode,
        string expectedLanguageTag)
    {
        var found = TesseractLanguageCatalog.TryMapTrainedDataCodeToPreferredLanguageTag(trainedDataCode, out var actualLanguageTag);

        Assert.True(found);
        Assert.Equal(expectedLanguageTag, actualLanguageTag);
    }

    [Theory]
    [InlineData("ja", true)]
    [InlineData("jpn_vert", true)]
    [InlineData("zh-CN", true)]
    [InlineData("chi_tra_vert", true)]
    [InlineData("ko", false)]
    [InlineData("th", false)]
    [InlineData("en", false)]
    public void SupportsVerticalTextLayout_ReturnsOnlyBundledVerticalLanguageLayouts(
        string language,
        bool expected)
    {
        Assert.Equal(expected, TesseractLanguageCatalog.SupportsVerticalTextLayout(language));
    }
}
