using System.IO;
using GameTranslator.Application.Profiles;
using GameTranslator.Domain.Profiles;
using GameTranslator.Infrastructure.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.Tests.Infrastructure;

public sealed class ProfileStorageCompositionTests : IDisposable
{
    private readonly string profilesDirectory = Path.Combine(
        Path.GetTempPath(),
        "GameTranslator.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RegisterServices_UsesConfiguredProfileStorageDirectory()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ProfileStorageOptions(profilesDirectory));

        new InfrastructureServiceModule().RegisterServices(services);

        using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<IProfileRepository>();
        var profile = new GameProfile
        {
            Name = "Composition test profile",
        };

        await repository.SaveAsync(profile);

        Assert.True(File.Exists(Path.Combine(profilesDirectory, $"{profile.Id}.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(profilesDirectory))
        {
            Directory.Delete(profilesDirectory, recursive: true);
        }
    }
}
