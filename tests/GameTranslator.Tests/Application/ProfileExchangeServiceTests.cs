using System.Text.Json;
using GameTranslator.Application.Profiles;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class ProfileExchangeServiceTests
{
    private readonly StubProfileExchangeGateway gateway = new();
    private readonly ProfileExchangeService service;

    public ProfileExchangeServiceTests()
    {
        service = new ProfileExchangeService(gateway, new ProfileValidator());
    }

    [Fact]
    public async Task ImportAsync_WhenSchemaVersionIsUnsupported_ThrowsValidationException()
    {
        gateway.ImportedProfile = CreateProfile("Imported") with
        {
            SchemaVersion = "2.0",
        };

        var exception = await Assert.ThrowsAsync<ProfileValidationException>(
            () => service.ImportAsync("profile.json"));

        Assert.Contains(
            exception.Errors,
            error => error.Code == ProfileValidationErrorCodes.UnsupportedSchemaVersion);
    }

    [Fact]
    public async Task ImportAsync_WhenGatewayThrowsProfileImportException_RethrowsException()
    {
        gateway.ImportException = new ProfileImportException("Profile JSON is invalid or corrupted.", new JsonException());

        var exception = await Assert.ThrowsAsync<ProfileImportException>(
            () => service.ImportAsync("broken.json"));

        Assert.Equal("Profile JSON is invalid or corrupted.", exception.Message);
    }

    [Fact]
    public async Task ExportAsync_WhenProfileIsValid_DelegatesToGateway()
    {
        var profile = CreateProfile("Export me");

        await service.ExportAsync(profile, "profile.json");

        Assert.Same(profile, gateway.ExportedProfile);
        Assert.Equal("profile.json", gateway.ExportedPath);
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
                    Name = "zone",
                    AbsoluteBounds = new AbsoluteRectangle(0, 0, 100, 50),
                    RelativeBounds = new RelativeRectangle(0, 0, 0.5, 0.25),
                },
            },
        };
    }

    private sealed class StubProfileExchangeGateway : IProfileExchangeGateway
    {
        public GameProfile? ImportedProfile { get; set; }

        public Exception? ImportException { get; set; }

        public GameProfile? ExportedProfile { get; private set; }

        public string? ExportedPath { get; private set; }

        public Task ExportAsync(GameProfile profile, string filePath, CancellationToken cancellationToken = default)
        {
            ExportedProfile = profile;
            ExportedPath = filePath;
            return Task.CompletedTask;
        }

        public Task<GameProfile> ImportAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (ImportException is not null)
            {
                throw ImportException;
            }

            return Task.FromResult(
                ImportedProfile ?? throw new InvalidOperationException("ImportedProfile was not configured."));
        }
    }
}
