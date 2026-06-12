using GameTranslator.Application.Abstractions;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Composition;
using GameTranslator.Application.Profiles;
using GameTranslator.Application.Settings;
using GameTranslator.Infrastructure.Capture;
using GameTranslator.Infrastructure.Profiles;
using GameTranslator.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.Infrastructure.Composition;

public sealed class InfrastructureServiceModule : IApplicationServiceModule
{
    public void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IProfileRepository>(provider =>
        {
            var options = provider.GetRequiredService<ProfileStorageOptions>();

            return new JsonProfileRepository(options.ProfilesDirectory);
        });
        services.AddSingleton<IProfileExchangeGateway, JsonProfileExchangeGateway>();
        services.AddSingleton<ICaptureFrameSource, WindowsGraphicsCaptureFrameSource>();
        services.AddSingleton<ISettingsService>(provider =>
        {
            var options = provider.GetRequiredService<SettingsStorageOptions>();

            return new JsonSettingsService(options.SettingsFilePath);
        });
    }
}
