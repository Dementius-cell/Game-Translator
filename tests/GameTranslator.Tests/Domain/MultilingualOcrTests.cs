using System.Text.Json;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Domain;

public sealed class MultilingualOcrTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TranslationAuto_RoundTripsWithoutReplacingManualLanguage(bool auto)
    {
        var settings = new TranslatorSettings { SourceLanguage = "ja", TargetLanguage = "ru", AutoDetectSourceLanguage = auto };
        var restored = JsonSerializer.Deserialize<TranslatorSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(settings, restored);
        Assert.Equal(auto ? "auto" : "ja", restored.ResolveSourceLanguage());
        Assert.Null(JsonSerializer.Deserialize<TranslatorSettings>("{}")!.AutoDetectSourceLanguage);
    }

    [Fact]
    public void OldProfiles_DefaultToSingleLanguage_AndExperimentSurvivesRoundTrip()
    {
        Assert.False(JsonSerializer.Deserialize<OcrSettings>("{}")!.ExperimentalMultilingual);
        Assert.Equal(OcrSettings.ExperimentalMultilingualLanguages, JsonSerializer.Deserialize<OcrSettings>("{}")!.MultilingualLanguages);
        var selected = new OcrSettings { ExperimentalMultilingual = true, MultilingualLanguages = "chi_tra+eng" };
        Assert.Equal(selected, JsonSerializer.Deserialize<OcrSettings>(JsonSerializer.Serialize(selected)));
        var settings = new OcrSettings { ExperimentalMultilingual = true, Engine = "Tesseract", OrientationMode = OcrOrientationMode.Horizontal };
        Assert.Equal(settings, JsonSerializer.Deserialize<OcrSettings>(JsonSerializer.Serialize(settings)));
    }

    [Theory]
    [InlineData("Tesseract", OcrOrientationMode.Horizontal, "GoogleWeb", true)]
    [InlineData("Tesseract", OcrOrientationMode.Horizontal, "BingWeb", true)]
    [InlineData("Tesseract", OcrOrientationMode.Horizontal, "YandexWeb", true)]
    [InlineData("Windows", OcrOrientationMode.Horizontal, "GoogleWeb", false)]
    [InlineData("Tesseract", OcrOrientationMode.Vertical, "GoogleWeb", true)]
    [InlineData("Tesseract", OcrOrientationMode.Auto, "GoogleWeb", true)]
    [InlineData("Tesseract", OcrOrientationMode.Horizontal, "Google", true)]
    public void Experiment_RejectsUnsupportedConfiguration(string engine, OcrOrientationMode orientation, string provider, bool valid)
    {
        Assert.Equal(valid, OcrSettings.GetMultilingualConfigurationError(true, engine, orientation, provider) is null);
        Assert.Null(OcrSettings.GetMultilingualConfigurationError(false, engine, orientation, provider));
    }
}
