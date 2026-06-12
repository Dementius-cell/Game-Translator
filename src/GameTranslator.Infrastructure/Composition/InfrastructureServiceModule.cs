using GameTranslator.Application.Composition;
using GameTranslator.Application.Profiles;
using GameTranslator.Infrastructure.Profiles;
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
    }
}
