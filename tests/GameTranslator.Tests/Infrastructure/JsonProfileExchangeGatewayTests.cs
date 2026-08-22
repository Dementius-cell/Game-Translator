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

    [Fact]
    public async Task ExportAndImportAsync_RoundTripsProfile()
    {
        var gateway = new JsonProfileExchangeGateway();
        var filePath = Path.Combine(workingDirectory, "profile.json");
        var profile = CreateProfile("Chrono Trigger");

        await gateway.ExportAsync(profile, filePath);
        var imported = await gateway.ImportAsync(filePath);

        Assert.Equal(profile.Name, imported.Name);
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
