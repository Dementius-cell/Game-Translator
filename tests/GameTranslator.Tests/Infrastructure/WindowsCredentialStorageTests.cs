using System.IO;
using GameTranslator.Application.Credentials;
using GameTranslator.Infrastructure.Composition;
using GameTranslator.Infrastructure.Credentials;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.Tests.Infrastructure;

public sealed class WindowsCredentialStorageTests
{
    [Fact]
    public void InfrastructureServiceModule_RegistersWindowsCredentialStorage()
    {
        var services = new ServiceCollection();

        new InfrastructureServiceModule().RegisterServices(services);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ICredentialStorage)
                && descriptor.ImplementationType == typeof(WindowsCredentialStorage));
    }

    [Fact]
    public void WindowsCredentialStorage_UsesWindowsCredentialManagerApis()
    {
        var source = File.ReadAllText(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.Infrastructure",
                "Credentials",
                "WindowsCredentialStorage.cs"));

        Assert.Contains("CredWriteW", source, StringComparison.Ordinal);
        Assert.Contains("CredReadW", source, StringComparison.Ordinal);
        Assert.Contains("CredDeleteW", source, StringComparison.Ordinal);
        Assert.Contains("CredFree", source, StringComparison.Ordinal);
        Assert.Contains("CredentialTypeGeneric", source, StringComparison.Ordinal);
        Assert.Contains("CredentialPersistLocalMachine", source, StringComparison.Ordinal);
        Assert.Contains("GameTranslator/Translator", source, StringComparison.Ordinal);

        var forbiddenPersistenceApis = new[]
        {
            "File.WriteAllText",
            "File.AppendAllText",
            "JsonProfileRepository",
            "JsonSettingsService",
        };

        foreach (var forbiddenPersistenceApi in forbiddenPersistenceApis)
        {
            Assert.DoesNotContain(forbiddenPersistenceApi, source, StringComparison.Ordinal);
        }
    }
}
