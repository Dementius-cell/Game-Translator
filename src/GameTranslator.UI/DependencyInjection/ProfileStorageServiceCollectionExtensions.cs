using System.IO;
using GameTranslator.Application.Profiles;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.UI.DependencyInjection;

public static class ProfileStorageServiceCollectionExtensions
{
    public static IServiceCollection AddDefaultProfileStorageOptions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var profilesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameTranslator",
            "Profiles");

        services.AddSingleton(new ProfileStorageOptions(profilesDirectory));

        return services;
    }
}
