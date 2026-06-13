using GameTranslator.Application.Capture;
using GameTranslator.Application.DependencyInjection;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Profiles;
using GameTranslator.Domain.Profiles;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.Tests.Application;

public sealed class ProfileCompositionTests
{
    [Fact]
    public void AddApplicationServices_RegistersProfileServicesAndValidator()
    {
        var services = new ServiceCollection();

        var result = services.AddApplicationServices();

        Assert.Same(services, result);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ProfileService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ProfileExchangeService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ProfileMigrationService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ProfileValidator));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(CaptureService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(OcrService));
    }

    [Fact]
    public void ProfileStorageOptions_WhenDirectoryIsEmpty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ProfileStorageOptions(string.Empty));
    }
}
