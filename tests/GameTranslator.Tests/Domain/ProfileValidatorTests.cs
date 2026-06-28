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

    [Fact]
    public void Validate_WhenOcrZoneTextStyleIsInvalid_ReturnsInvalidTextStyleError()
    {
        var profile = CreateValidProfile() with
        {
            OcrZones = new[]
            {
                CreateZone("dialog", new AbsoluteRectangle(10, 10, 120, 40)) with
                {
                    TextStyle = new OcrZoneTextStyle
                    {
                        FontFamily = string.Empty,
                        FontSize = 200,
                        LayoutMode = (OverlayTextLayoutMode)999,
                    },
                },
            },
        };

        var result = validator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == ProfileValidationErrorCodes.InvalidOcrZoneTextStyle);
    }

    [Fact]
    public void Validate_WhenTranslationGroupingModeIsInvalid_ReturnsInvalidGroupingModeError()
    {
        var profile = CreateValidProfile() with
        {
            OcrZones = new[]
            {
                CreateZone("dialog", new AbsoluteRectangle(10, 10, 120, 40)) with
                {
                    TranslationGroupingMode = (TranslationGroupingMode)999,
                },
            },
        };

        var result = validator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == ProfileValidationErrorCodes.InvalidOcrZoneTranslationGroupingMode);
    }

    [Fact]
    public void Validate_WhenTextGroupingGapIsOutOfRange_ReturnsInvalidTextGroupingError()
    {
        var profile = CreateValidProfile() with
        {
            OcrZones = new[]
            {
                CreateZone("dialog", new AbsoluteRectangle(10, 10, 120, 40)) with
                {
                    TextGrouping = new OcrZoneTextGroupingSettings
                    {
                        MergeDistancePercent = 25,
                    },
                },
            },
        };

        var result = validator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == ProfileValidationErrorCodes.InvalidOcrZoneTextGrouping);
    }

    [Fact]
    public void Validate_WhenOcrEngineIsUnsupported_ReturnsInvalidOcrSettingsError()
    {
        var profile = CreateValidProfile() with
        {
            OcrSettings = new OcrSettings
            {
                Engine = "UnsupportedEngine",
            },
        };

        var result = validator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == ProfileValidationErrorCodes.InvalidOcrSettings);
    }

    [Fact]
    public void Validate_WhenOcrOrientationModeIsUnsupported_ReturnsInvalidOcrSettingsError()
    {
        var profile = CreateValidProfile() with
        {
            OcrSettings = new OcrSettings
            {
                OrientationMode = (OcrOrientationMode)999,
            },
        };

        var result = validator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == ProfileValidationErrorCodes.InvalidOcrSettings);
    }

    [Fact]
    public void Validate_WhenOcrPreprocessingSettingsAreOutOfRange_ReturnsInvalidPreprocessingError()
    {
        var profile = CreateValidProfile() with
        {
            OcrPreprocessingSettings = new OcrPreprocessingSettings
            {
                Contrast = 4,
                Brightness = 101,
                Sharpness = 3,
                Scale = 4,
            },
        };

        var result = validator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == ProfileValidationErrorCodes.InvalidOcrPreprocessingSettings);
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
