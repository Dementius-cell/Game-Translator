using GameTranslator.Application.Capture;
using GameTranslator.Application.Profiles;
using GameTranslator.Domain.Profiles;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ProfileValidator>();
        services.AddSingleton<ProfileMigrationService>();
        services.AddSingleton<ProfileService>();
        services.AddSingleton<ProfileExchangeService>();
        services.AddSingleton<CaptureService>();

        return services;
    }
}
