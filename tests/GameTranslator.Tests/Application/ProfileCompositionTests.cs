using GameTranslator.Application.Cache;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Credentials;
using GameTranslator.Application.DependencyInjection;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Profiles;
using GameTranslator.Application.Translation;
using GameTranslator.Application.Updates;
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
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TranslationCacheService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TranslationCacheOptions));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ProfileExchangeService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ProfileMigrationService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ProfileValidator));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(CaptureService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(OcrService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(OverlayPositioningService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TranslatorManager));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TranslatorCredentialService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ApplicationUpdateService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ApplicationUpdateOptions));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IApplicationUpdateProvider)
            && descriptor.ImplementationType == typeof(NoOpApplicationUpdateProvider));
    }

    [Fact]
    public void AddApplicationServices_WhenMultipleOcrEnginesAreRegistered_ResolvesOcrService()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();
        services.AddSingleton<IOcrEngine>(new TestOcrEngine("Windows"));
        services.AddSingleton<IOcrEngine>(new TestOcrEngine("Tesseract"));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<OcrService>());
    }

    [Fact]
    public void ProfileStorageOptions_WhenDirectoryIsEmpty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ProfileStorageOptions(string.Empty));
    }

    [Fact]
    public void TranslationCacheStorageOptions_WhenPathIsEmpty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new TranslationCacheStorageOptions(string.Empty));
    }

    [Fact]
    public void TranslationCacheOptions_DefaultToThirtyDayTtl()
    {
        var options = new TranslationCacheOptions();

        Assert.Equal(TimeSpan.FromDays(30), options.TimeToLive);
    }

    private sealed class TestOcrEngine : IOcrEngine
    {
        public TestOcrEngine(string engineId)
        {
            EngineId = engineId;
        }

        public string EngineId { get; }

        public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Composition test OCR engine does not recognize frames.");
        }
    }
}
