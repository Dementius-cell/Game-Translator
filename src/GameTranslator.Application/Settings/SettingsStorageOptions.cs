namespace GameTranslator.Application.Settings;

public sealed class SettingsStorageOptions
{
    public SettingsStorageOptions(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);

        SettingsFilePath = settingsFilePath;
    }

    public string SettingsFilePath { get; }
}
