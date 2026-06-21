using GameTranslator.Application.Cache;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Credentials;
using GameTranslator.Application.Debug;
using GameTranslator.Application.Hotkeys;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Pipeline;
using GameTranslator.Application.Profiles;
using GameTranslator.Application.Translation;
using GameTranslator.Application.Updates;
using GameTranslator.Domain.Profiles;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ProfileValidator>();
        services.AddSingleton(new TranslationCacheOptions());
        services.AddSingleton(new TranslationPipelineOptimizationOptions());
        services.AddSingleton(new ApplicationUpdateOptions());
        services.AddSingleton<IApplicationUpdateProvider, NoOpApplicationUpdateProvider>();
        services.AddSingleton<TranslationCacheService>();
        services.AddSingleton<ApplicationUpdateService>();
        services.AddSingleton<DebugMetricFormatter>();
        services.AddSingleton<ProfileMigrationService>();
        services.AddSingleton<ProfileService>();
        services.AddSingleton<ProfileExchangeService>();
        services.AddSingleton<CaptureService>();
        services.AddSingleton<OcrPreprocessor>();
        services.AddSingleton(provider => new OcrService(
            provider.GetServices<IOcrEngine>(),
            provider.GetRequiredService<OcrPreprocessor>()));
        services.AddSingleton<OverlayPositioningService>();
        services.AddSingleton<TranslationPipelineService>();
        services.AddSingleton<TranslatorManager>();
        services.AddSingleton<TranslatorCredentialService>();
        services.AddSingleton<GlobalHotkeyService>();

        return services;
    }
}
