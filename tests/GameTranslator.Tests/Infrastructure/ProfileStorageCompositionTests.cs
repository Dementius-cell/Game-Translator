using System.IO;
using GameTranslator.Application.Abstractions;
using GameTranslator.Application.Profiles;
using GameTranslator.Application.Settings;
using GameTranslator.Application.Translation;
using GameTranslator.Domain.Profiles;
using GameTranslator.Infrastructure.Composition;
using GameTranslator.Infrastructure.Translation;
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
        services.AddSingleton(new SettingsStorageOptions(Path.Combine(profilesDirectory, "state", "settings.json")));

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

    [Fact]
    public void RegisterServices_RegistersProfileExchangeGateway()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ProfileStorageOptions(profilesDirectory));
        services.AddSingleton(new SettingsStorageOptions(Path.Combine(profilesDirectory, "state", "settings.json")));

        new InfrastructureServiceModule().RegisterServices(services);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IProfileExchangeGateway));
    }

    [Fact]
    public void RegisterServices_RegistersGoogleTranslatorProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ProfileStorageOptions(profilesDirectory));
        services.AddSingleton(new SettingsStorageOptions(Path.Combine(profilesDirectory, "state", "settings.json")));

        new InfrastructureServiceModule().RegisterServices(services);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ITranslatorProvider)
                && descriptor.ImplementationType == typeof(GoogleTranslatorProvider));
    }

    [Fact]
    public void RegisterServices_UsesConfiguredSettingsFile()
    {
        var settingsFilePath = Path.Combine(profilesDirectory, "state", "settings.json");
        var services = new ServiceCollection();
        services.AddSingleton(new ProfileStorageOptions(profilesDirectory));
        services.AddSingleton(new SettingsStorageOptions(settingsFilePath));

        new InfrastructureServiceModule().RegisterServices(services);

        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<ISettingsService>();

        settings.SetValue("profiles.selectedId", "active-profile");

        Assert.True(File.Exists(settingsFilePath));
        Assert.Equal("active-profile", settings.GetValue<string>("profiles.selectedId"));
    }

    public void Dispose()
    {
        if (Directory.Exists(profilesDirectory))
        {
            Directory.Delete(profilesDirectory, recursive: true);
        }
    }
}
