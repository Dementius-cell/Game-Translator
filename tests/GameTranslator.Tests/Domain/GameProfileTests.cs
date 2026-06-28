using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Domain;

public sealed class GameProfileTests
{
    [Fact]
    public void NewProfile_UsesCurrentSchemaVersion()
    {
        var profile = new GameProfile
        {
            Name = "Test Game",
        };

        Assert.Equal(GameProfile.CurrentSchemaVersion, profile.SchemaVersion);
    }

    [Fact]
    public void OcrZone_StoresAbsoluteAndRelativeBounds()
    {
        var zone = new OcrZone
        {
            Name = "Subtitle area",
            AbsoluteBounds = new AbsoluteRectangle(X: 100, Y: 200, Width: 640, Height: 160),
            RelativeBounds = new RelativeRectangle(X: 0.1, Y: 0.2, Width: 0.5, Height: 0.15),
        };

        Assert.Equal(new AbsoluteRectangle(100, 200, 640, 160), zone.AbsoluteBounds);
        Assert.Equal(new RelativeRectangle(0.1, 0.2, 0.5, 0.15), zone.RelativeBounds);
        Assert.Equal(OcrZoneTextStyle.Default, zone.TextStyle);
        Assert.Equal(TranslationGroupingMode.BlockByBlock, zone.TranslationGroupingMode);
    }

    [Fact]
    public void TranslatorSettings_PublicContractDoesNotExposeSecretFields()
    {
        var forbiddenNameParts = new[] { "Key", "Secret", "Token", "Credential", "Password" };
        var propertyNames = typeof(TranslatorSettings)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(
            propertyNames,
            propertyName => forbiddenNameParts.Any(forbidden =>
                propertyName.Contains(forbidden, StringComparison.OrdinalIgnoreCase)));
    }
}
