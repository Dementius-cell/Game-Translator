using System.IO;
using GameTranslator.Application.Profiles;
using GameTranslator.Domain.Profiles;
using GameTranslator.Infrastructure.Profiles;

namespace GameTranslator.Tests.Infrastructure;

public sealed class JsonProfileExchangeGatewayTests : IDisposable
{
    private readonly string workingDirectory = Path.Combine(
        Path.GetTempPath(),
        "GameTranslator.Tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(WatermarkFilterMode.Off)]
    [InlineData(WatermarkFilterMode.Cautious)]
    [InlineData(WatermarkFilterMode.Strong)]
    public async Task ExportAndImportAsync_RoundTripsProfile(WatermarkFilterMode mode)
    {
        var gateway = new JsonProfileExchangeGateway();
        var filePath = Path.Combine(workingDirectory, "profile.json");
        var profile = CreateProfile("Chrono Trigger") with { OcrSettings = new() { WatermarkFilter = mode } };

        await gateway.ExportAsync(profile, filePath);
        var imported = await gateway.ImportAsync(filePath);

        Assert.Equal(profile.Name, imported.Name);
        Assert.Equal(mode, imported.OcrSettings.WatermarkFilter);
        Assert.Equal(profile.SchemaVersion, imported.SchemaVersion);
        Assert.Equal(profile.OverlaySettings.MaskColor, imported.OverlaySettings.MaskColor);
        Assert.Equal(profile.OcrZones[0].AbsoluteBounds, imported.OcrZones[0].AbsoluteBounds);
        Assert.Equal(profile.OcrZones[0].OcrLanguage, imported.OcrZones[0].OcrLanguage);
        Assert.Equal(profile.OcrZones[0].ContentLayoutMode, imported.OcrZones[0].ContentLayoutMode);
        Assert.Equal(profile.OcrZones[0].TextStyle, imported.OcrZones[0].TextStyle);
        Assert.Equal(profile.OcrZones[0].TranslationGroupingMode, imported.OcrZones[0].TranslationGroupingMode);
        Assert.Equal(profile.OcrZones[0].TextGrouping, imported.OcrZones[0].TextGrouping);
    }

    [Fact]
    public async Task ImportAsync_WhenJsonIsCorrupted_ThrowsProfileImportException()
    {
        var gateway = new JsonProfileExchangeGateway();
        var filePath = Path.Combine(workingDirectory, "broken.json");
        Directory.CreateDirectory(workingDirectory);
        await File.WriteAllTextAsync(filePath, "{ this is not valid json");

        var exception = await Assert.ThrowsAsync<ProfileImportException>(
            () => gateway.ImportAsync(filePath));

        Assert.Equal("Profile JSON is invalid or corrupted.", exception.Message);
    }

    [Fact]
    public async Task ImportAsync_OldProfileWithoutAutoSize_KeepsConfiguredFont()
    {
        var gateway = new JsonProfileExchangeGateway();
        var path = Path.Combine(workingDirectory, "old-profile.json");
        await gateway.ExportAsync(CreateProfile("Old"), path);
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        var style = json["ocrZones"]![0]!["textStyle"]!.AsObject();
        Assert.True(style.Remove("autoFontSize"));
        await File.WriteAllTextAsync(path, json.ToJsonString());
        var imported = await gateway.ImportAsync(path);
        Assert.False(imported.OcrZones[0].TextStyle.AutoFontSize);
        Assert.Equal(22, imported.OcrZones[0].TextStyle.FontSize);
    }

    [Fact]
    public async Task PreparationOptions_RoundTrip_AndMissingFieldsPreserveLegacyDefaults()
    {
        var gateway = new JsonProfileExchangeGateway();
        var path = Path.Combine(workingDirectory, "preparation.json");
        var profile = CreateProfile("Preparation") with { OcrPreprocessingSettings = new()
            { IsEnabled = true, Mode = OcrPreprocessingMode.Adaptive, Scale = .5, InvertColors = true, AdaptiveThresholding = true, AddWhiteBorder = true } };
        await gateway.ExportAsync(profile, path);
        Assert.Equal(profile.OcrPreprocessingSettings, (await gateway.ImportAsync(path)).OcrPreprocessingSettings);
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        var settings = json["ocrPreprocessingSettings"]!.AsObject();
        foreach (var name in new[] { "invertColors", "adaptiveThresholding", "addWhiteBorder", "mode" }) Assert.True(settings.Remove(name));
        settings["scale"] = 1.25;
        await File.WriteAllTextAsync(path, json.ToJsonString());
        var legacy = (await gateway.ImportAsync(path)).OcrPreprocessingSettings;
        Assert.Equal(OcrPreprocessingMode.Manual, legacy.ResolveMode());
        Assert.Equal(OcrPreprocessingMode.None, new OcrPreprocessingSettings().ResolveMode());
        Assert.False(legacy.InvertColors);
        Assert.False(legacy.AdaptiveThresholding);
        Assert.False(legacy.AddWhiteBorder);
        Assert.Equal(1.25, legacy.Scale);
    }

    public void Dispose()
    {
        if (Directory.Exists(workingDirectory))
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    private static GameProfile CreateProfile(string name)
    {
        return new GameProfile
        {
            Name = name,
            OcrZones = new[]
            {
                new OcrZone
                {
                    Name = "dialogue",
                    AbsoluteBounds = new AbsoluteRectangle(10, 20, 300, 80),
                    RelativeBounds = new RelativeRectangle(0.1, 0.2, 0.4, 0.1),
                    OcrLanguage = "chi_tra_vert",
                    ContentLayoutMode = ContentLayoutMode.DialogComic,
                    TextStyle = new OcrZoneTextStyle
                    {
                        FontFamily = "Segoe UI",
                        FontSize = 22,
                        AutoFontSize = true,
                        IsBold = false,
                        IsItalic = true,
                        LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter,
                    },
                    TranslationGroupingMode = TranslationGroupingMode.WholeZone,
                    TextGrouping = new OcrZoneTextGroupingSettings
                    {
                        MergeDistancePercent = 6.5,
                    },
                },
            },
            OverlaySettings = new OverlaySettings
            {
                MaskMode = OverlayMaskMode.Darken,
                MaskColor = "#101010",
                Opacity = 0.75,
                Padding = 6,
            },
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "ja",
                TargetLanguage = "en",
            },
        };
    }
}
