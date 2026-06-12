using System.Text.Json;
using GameTranslator.Application.Abstractions;

namespace GameTranslator.Infrastructure.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly Lock syncRoot = new();
    private readonly string settingsFilePath;

    public JsonSettingsService(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);

        this.settingsFilePath = settingsFilePath;
    }

    public TValue? GetValue<TValue>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (syncRoot)
        {
            var values = ReadValues();
            if (!values.TryGetValue(key, out var value))
            {
                return default;
            }

            return value.Deserialize<TValue>(SerializerOptions);
        }
    }

    public void SetValue<TValue>(string key, TValue? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (syncRoot)
        {
            var values = ReadValues();

            if (value is null)
            {
                values.Remove(key);
            }
            else
            {
                values[key] = JsonSerializer.SerializeToElement(value, SerializerOptions);
            }

            WriteValues(values);
        }
    }

    private Dictionary<string, JsonElement> ReadValues()
    {
        if (!File.Exists(settingsFilePath))
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        try
        {
            using var stream = File.OpenRead(settingsFilePath);
            var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stream, SerializerOptions);

            return values is null
                ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                : new Dictionary<string, JsonElement>(values, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
    }

    private void WriteValues(Dictionary<string, JsonElement> values)
    {
        var directory = Path.GetDirectoryName(settingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{settingsFilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            var json = JsonSerializer.Serialize(values, SerializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, settingsFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
