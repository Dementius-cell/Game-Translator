using System.Text.Json;
using System.Text.Json.Serialization;
using GameTranslator.Application.Profiles;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Infrastructure.Profiles;

public sealed class JsonProfileExchangeGateway : IProfileExchangeGateway
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public async Task ExportAsync(GameProfile profile, string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(profile, SerializerOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    public async Task<GameProfile> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var stream = File.OpenRead(filePath);
            var profile = await JsonSerializer.DeserializeAsync<GameProfile>(
                stream,
                SerializerOptions,
                cancellationToken);

            return profile ?? throw new ProfileImportException("Profile JSON did not contain a valid profile payload.");
        }
        catch (JsonException exception)
        {
            throw new ProfileImportException("Profile JSON is invalid or corrupted.", exception);
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
