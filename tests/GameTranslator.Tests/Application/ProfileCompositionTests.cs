using GameTranslator.Application.DependencyInjection;
using GameTranslator.Application.Profiles;
using GameTranslator.Domain.Profiles;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.Tests.Application;

public sealed class ProfileCompositionTests
{
    [Fact]
    public void AddApplicationServices_RegistersProfileServiceAndValidator()
    {
        var services = new ServiceCollection();

        var result = services.AddApplicationServices();

        Assert.Same(services, result);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ProfileService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ProfileValidator));
    }

    [Fact]
    public void ProfileStorageOptions_WhenDirectoryIsEmpty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ProfileStorageOptions(string.Empty));
    }
}
