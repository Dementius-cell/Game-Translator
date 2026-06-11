using System.Text.Json;
using System.Text.Json.Serialization;
using GameTranslator.Application.Profiles;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Infrastructure.Profiles;

public sealed class JsonProfileRepository : IProfileRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private readonly string profilesDirectory;

    public JsonProfileRepository(string profilesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesDirectory);

        this.profilesDirectory = profilesDirectory;
    }

    public async Task<IReadOnlyList<GameProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(profilesDirectory))
        {
            return Array.Empty<GameProfile>();
        }

        var profiles = new List<GameProfile>();
        foreach (var profilePath in Directory.EnumerateFiles(profilesDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var stream = File.OpenRead(profilePath);
            var profile = await JsonSerializer.DeserializeAsync<GameProfile>(
                stream,
                SerializerOptions,
                cancellationToken);

            if (profile is not null)
            {
                profiles.Add(profile);
            }
        }

        return profiles
            .OrderBy(profile => profile.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<GameProfile?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var profilePath = GetProfilePath(id);
        if (!File.Exists(profilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(profilePath);
        return await JsonSerializer.DeserializeAsync<GameProfile>(
            stream,
            SerializerOptions,
            cancellationToken);
    }

    public async Task SaveAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(profilesDirectory);

        var profilePath = GetProfilePath(profile.Id);
        var temporaryPath = $"{profilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            var json = JsonSerializer.Serialize(profile, SerializerOptions);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, profilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var profilePath = GetProfilePath(id);
        if (File.Exists(profilePath))
        {
            File.Delete(profilePath);
        }

        return Task.CompletedTask;
    }

    private string GetProfilePath(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Profile id contains invalid file name characters.", nameof(id));
        }

        return Path.Combine(profilesDirectory, $"{id}.json");
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
