using System.IO;
using GameTranslator.Application.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.UI.DependencyInjection;

public static class SettingsStorageServiceCollectionExtensions
{
    public static IServiceCollection AddDefaultSettingsStorageOptions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var settingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameTranslator",
            "State",
            "settings.json");

        services.AddSingleton(new SettingsStorageOptions(settingsFilePath));

        return services;
    }
}
