using GameTranslator.Application.Updates;
using GameTranslator.Infrastructure.Composition;
using GameTranslator.Infrastructure.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.Tests.Infrastructure;

public sealed class SquirrelApplicationUpdateProviderTests
{
    [Fact]
    public void InfrastructureServiceModule_RegistersSquirrelApplicationUpdateProvider()
    {
        var services = new ServiceCollection();

        new InfrastructureServiceModule().RegisterServices(services);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IApplicationUpdateProvider)
                && descriptor.ImplementationType == typeof(SquirrelApplicationUpdateProvider));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenSquirrelInstallIsMissing_ReturnsNotInstalled()
    {
        var provider = new SquirrelApplicationUpdateProvider();

        var result = await provider.CheckForUpdatesAsync(
            new ApplicationUpdateOptions("https://updates.test"),
            ApplicationUpdateCheckMode.Manual);

        Assert.Equal(ApplicationUpdateStatus.NotInstalled, result.Status);
    }
}
