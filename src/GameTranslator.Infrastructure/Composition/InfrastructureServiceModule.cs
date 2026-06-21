using GameTranslator.Application.Abstractions;
using GameTranslator.Application.Cache;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Composition;
using GameTranslator.Application.Credentials;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Profiles;
using GameTranslator.Application.Settings;
using GameTranslator.Application.Translation;
using GameTranslator.Application.Updates;
using GameTranslator.Infrastructure.Capture;
using GameTranslator.Infrastructure.Cache;
using GameTranslator.Infrastructure.Credentials;
using GameTranslator.Infrastructure.Ocr;
using GameTranslator.Infrastructure.Profiles;
using GameTranslator.Infrastructure.Settings;
using GameTranslator.Infrastructure.Translation;
using GameTranslator.Infrastructure.Updates;
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
        services.AddSingleton<ITranslationCacheRepository, SqliteTranslationCacheRepository>();
        services.AddSingleton<ICaptureFrameSource, WindowsGraphicsCaptureFrameSource>();
        services.AddSingleton<IOcrEngine, WindowsOcrEngine>();
        services.AddSingleton<IOcrEngine, TesseractOcrEngine>();
        services.AddSingleton<HttpClient>();
        services.AddSingleton<ICredentialStorage, WindowsCredentialStorage>();
        services.AddSingleton<ITranslatorProvider, GoogleTranslatorProvider>();
        services.AddSingleton<ITranslatorProvider, AzureTranslatorProvider>();
        services.AddSingleton<ITranslatorProvider, YandexTranslatorProvider>();
        services.AddSingleton<IApplicationUpdateProvider, SquirrelApplicationUpdateProvider>();
        services.AddSingleton<ISettingsService>(provider =>
        {
            var options = provider.GetRequiredService<SettingsStorageOptions>();

            return new JsonSettingsService(options.SettingsFilePath);
        });
    }
}
