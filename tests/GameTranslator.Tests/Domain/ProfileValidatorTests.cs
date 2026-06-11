using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Domain;

public sealed class ProfileValidatorTests
{
    private readonly ProfileValidator validator = new();

    [Fact]
    public void Validate_WhenSchemaVersionIsCurrentAndZonesDoNotOverlap_ReturnsValid()
    {
        var profile = CreateValidProfile();

        var result = validator.Validate(profile);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_WhenSchemaVersionIsMissing_ReturnsMissingSchemaVersionError()
    {
        var profile = CreateValidProfile() with
        {
            SchemaVersion = string.Empty,
        };

        var result = validator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == ProfileValidationErrorCodes.MissingSchemaVersion);
    }

    [Fact]
    public void Validate_WhenSchemaVersionIsUnsupported_ReturnsUnsupportedSchemaVersionError()
    {
        var profile = CreateValidProfile() with
        {
            SchemaVersion = "0.9",
        };

        var result = validator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == ProfileValidationErrorCodes.UnsupportedSchemaVersion);
    }

    [Fact]
    public void Validate_WhenOcrZonesOverlap_ReturnsOverlappingOcrZonesError()
    {
        var profile = CreateValidProfile() with
        {
            OcrZones = new[]
            {
                CreateZone("dialog", new AbsoluteRectangle(10, 10, 120, 40)),
                CreateZone("subtitle", new AbsoluteRectangle(100, 20, 120, 40)),
            },
        };

        var result = validator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == ProfileValidationErrorCodes.OverlappingOcrZones);
    }

    [Fact]
    public void Validate_WhenOcrZonesOnlyTouchAtEdge_ReturnsValid()
    {
        var profile = CreateValidProfile() with
        {
            OcrZones = new[]
            {
                CreateZone("left", new AbsoluteRectangle(0, 0, 100, 100)),
                CreateZone("right", new AbsoluteRectangle(100, 0, 100, 100)),
            },
        };

        var result = validator.Validate(profile);

        Assert.True(result.IsValid);
    }

    private static GameProfile CreateValidProfile()
    {
        return new GameProfile
        {
            Name = "Test Game",
            Description = "Profile used by domain tests.",
            OcrZones = new[]
            {
                CreateZone("top", new AbsoluteRectangle(0, 0, 100, 100)),
                CreateZone("bottom", new AbsoluteRectangle(0, 120, 100, 100)),
            },
            OverlaySettings = new OverlaySettings
            {
                MaskMode = OverlayMaskMode.Solid,
                MaskColor = "#000000",
                Opacity = 0.85,
                Padding = 4,
            },
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "en",
                TargetLanguage = "ru",
            },
        };
    }

    private static OcrZone CreateZone(string name, AbsoluteRectangle absoluteBounds)
    {
        return new OcrZone
        {
            Name = name,
            AbsoluteBounds = absoluteBounds,
            RelativeBounds = new RelativeRectangle(0, 0, 0.5, 0.5),
        };
    }
}
