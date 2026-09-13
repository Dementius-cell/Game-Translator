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
        services.AddSingleton(new PaddleOcrTextCandidateDetectorOptions());
        services.AddSingleton<ITextCandidateDetector, PaddleOcrTextCandidateDetector>();
        services.AddSingleton<IOcrLanguagePackService, OcrLanguagePackService>();
        services.AddSingleton<HttpClient>();
        services.AddKeyedSingleton<HttpClient>("BingWeb", (_, _) => new HttpClient(
            new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5) })
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        });
        services.AddSingleton<ICredentialStorage, WindowsCredentialStorage>();
        services.AddSingleton<ITranslatorProvider, GoogleTranslatorProvider>();
        services.AddSingleton<ITranslatorProvider, AzureTranslatorProvider>();
        services.AddSingleton<ITranslatorProvider, YandexTranslatorProvider>();
        if (Environment.GetEnvironmentVariable("GAME_TRANSLATOR_TEST_FAULT") is "timeout" or "429" or "captcha")
        {
        services.AddSingleton<ITranslatorProvider>(provider => DiagnosticWebTranslatorProvider.Wrap(ActivatorUtilities.CreateInstance<GoogleWebTranslatorProvider>(provider)));
        services.AddSingleton<ITranslatorProvider>(provider => DiagnosticWebTranslatorProvider.Wrap(new BingWebTranslatorProvider(provider.GetRequiredKeyedService<HttpClient>("BingWeb"))));
        services.AddSingleton<ITranslatorProvider>(provider => DiagnosticWebTranslatorProvider.Wrap(ActivatorUtilities.CreateInstance<YandexWebTranslatorProvider>(provider)));
        }
        else
        {
            services.AddSingleton<ITranslatorProvider, GoogleWebTranslatorProvider>();
            services.AddSingleton<ITranslatorProvider>(provider => new BingWebTranslatorProvider(provider.GetRequiredKeyedService<HttpClient>("BingWeb")));
            services.AddSingleton<ITranslatorProvider, YandexWebTranslatorProvider>();
        }
        services.AddSingleton<IApplicationUpdateProvider, SquirrelApplicationUpdateProvider>();
        services.AddSingleton<ISettingsService>(provider =>
        {
            var options = provider.GetRequiredService<SettingsStorageOptions>();

            return new JsonSettingsService(options.SettingsFilePath);
        });
    }
}
