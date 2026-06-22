using System.IO;
using GameTranslator.Domain.Profiles;
using GameTranslator.Infrastructure.Profiles;

namespace GameTranslator.Tests.Infrastructure;

public sealed class JsonProfileRepositoryTests : IDisposable
{
    private readonly string profilesDirectory = Path.Combine(
        Path.GetTempPath(),
        "GameTranslator.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAsync_WhenDirectoryDoesNotExist_CreatesDirectoryAndWritesProfileJson()
    {
        var repository = new JsonProfileRepository(profilesDirectory);
        var profile = CreateProfile("Disco Elysium");

        await repository.SaveAsync(profile);

        var profilePath = Path.Combine(profilesDirectory, $"{profile.Id}.json");
        var json = await File.ReadAllTextAsync(profilePath);

        Assert.True(Directory.Exists(profilesDirectory));
        Assert.Contains("\"schemaVersion\": \"1.0\"", json);
        Assert.False(json.Contains("apiKey", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("credential", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetByIdAsync_WhenProfileExists_ReturnsRoundTrippedProfile()
    {
        var repository = new JsonProfileRepository(profilesDirectory);
        var profile = CreateProfile("Persona 5");

        await repository.SaveAsync(profile);
        var loaded = await repository.GetByIdAsync(profile.Id);

        Assert.NotNull(loaded);
        Assert.Equal(profile.Id, loaded.Id);
        Assert.Equal(GameProfile.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(profile.OcrZones[0].AbsoluteBounds, loaded.OcrZones[0].AbsoluteBounds);
        Assert.Equal(profile.OcrZones[0].RelativeBounds, loaded.OcrZones[0].RelativeBounds);
        Assert.Equal(profile.OcrZones[0].TextStyle, loaded.OcrZones[0].TextStyle);
        Assert.Equal(profile.OcrSettings.Engine, loaded.OcrSettings.Engine);
        Assert.Equal(profile.OcrSettings.OrientationMode, loaded.OcrSettings.OrientationMode);
        Assert.Equal(profile.OcrPreprocessingSettings.Contrast, loaded.OcrPreprocessingSettings.Contrast);
        Assert.Equal(profile.OcrPreprocessingSettings.Brightness, loaded.OcrPreprocessingSettings.Brightness);
        Assert.Equal(profile.OcrPreprocessingSettings.ThresholdingEnabled, loaded.OcrPreprocessingSettings.ThresholdingEnabled);
        Assert.Equal(profile.OcrPreprocessingSettings.Scale, loaded.OcrPreprocessingSettings.Scale);
    }

    [Fact]
    public async Task ListAsync_ReturnsProfilesOrderedByName()
    {
        var repository = new JsonProfileRepository(profilesDirectory);
        await repository.SaveAsync(CreateProfile("Zeta"));
        await repository.SaveAsync(CreateProfile("Alpha"));

        var profiles = await repository.ListAsync();

        Assert.Equal(new[] { "Alpha", "Zeta" }, profiles.Select(profile => profile.Name));
    }

    [Fact]
    public async Task DeleteAsync_WhenProfileExists_RemovesProfileFile()
    {
        var repository = new JsonProfileRepository(profilesDirectory);
        var profile = CreateProfile("Delete me");
        await repository.SaveAsync(profile);

        await repository.DeleteAsync(profile.Id);

        Assert.Null(await repository.GetByIdAsync(profile.Id));
        Assert.False(File.Exists(Path.Combine(profilesDirectory, $"{profile.Id}.json")));
    }

    [Fact]
    public async Task GetByIdAsync_WhenProfileDoesNotExist_ReturnsNull()
    {
        var repository = new JsonProfileRepository(profilesDirectory);

        var profile = await repository.GetByIdAsync("missing");

        Assert.Null(profile);
    }

    public void Dispose()
    {
        if (Directory.Exists(profilesDirectory))
        {
            Directory.Delete(profilesDirectory, recursive: true);
        }
    }

    private static GameProfile CreateProfile(string name)
    {
        return new GameProfile
        {
            Name = name,
            Description = "Temp repository test profile.",
            OcrZones = new[]
            {
                new OcrZone
                {
                    Name = "subtitles",
                    AbsoluteBounds = new AbsoluteRectangle(10, 20, 300, 80),
                    RelativeBounds = new RelativeRectangle(0.1, 0.2, 0.4, 0.1),
                    TextStyle = new OcrZoneTextStyle
                    {
                        FontFamily = "Arial",
                        FontSize = 20,
                        IsBold = true,
                        IsItalic = true,
                        LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter,
                    },
                },
            },
            OcrSettings = new OcrSettings
            {
                Engine = OcrSettings.TesseractEngineId,
                OrientationMode = OcrOrientationMode.Vertical,
            },
            OcrPreprocessingSettings = new OcrPreprocessingSettings
            {
                IsEnabled = true,
                Contrast = 1.4,
                Brightness = 12,
                Sharpness = 0.5,
                ThresholdingEnabled = true,
                Threshold = 180,
                Scale = 2,
                NoiseReductionEnabled = true,
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
                SourceLanguage = "en",
                TargetLanguage = "ru",
            },
        };
    }
}
