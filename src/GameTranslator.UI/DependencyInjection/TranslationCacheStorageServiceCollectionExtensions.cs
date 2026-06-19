using System.IO;
using GameTranslator.Application.Cache;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.UI.DependencyInjection;

public static class TranslationCacheStorageServiceCollectionExtensions
{
    public static IServiceCollection AddDefaultTranslationCacheStorageOptions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var databaseFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameTranslator",
            "Cache",
            "translations.db");

        services.AddSingleton(new TranslationCacheStorageOptions(databaseFilePath));

        return services;
    }
}
