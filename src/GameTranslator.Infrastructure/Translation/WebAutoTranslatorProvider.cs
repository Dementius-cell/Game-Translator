using GameTranslator.Application.Translation;

namespace GameTranslator.Infrastructure.Translation;

public sealed class WebAutoTranslatorProvider : ITranslatorProvider
{
    private readonly IReadOnlyList<ITranslatorProvider> providers;

    public WebAutoTranslatorProvider(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        providers = new ITranslatorProvider[]
        {
            new GoogleWebTranslatorProvider(httpClient),
            new BingWebTranslatorProvider(httpClient),
            new YandexWebTranslatorProvider(httpClient),
        };
    }

    public string ProviderId => "WebAuto";

    public async Task<TranslateResponse> TranslateAsync(
        TranslateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var failures = new List<string>();
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var response = await provider.TranslateAsync(
                    CreateProviderRequest(request, provider.ProviderId),
                    cancellationToken);

                return new TranslateResponse(
                    response.TranslatedTexts,
                    response.TranslatedAt,
                    provider.ProviderId,
                    CreateSuccessDiagnostic(provider.ProviderId, failures));
            }
            catch (TranslatorProviderException exception)
            {
                failures.Add($"{provider.ProviderId} [{exception.FailureKind}]: {exception.Message}");
            }
        }

        throw new TranslatorProviderException(
            ProviderId,
            TranslatorProviderFailureKind.AllProvidersFailed,
            $"All experimental web translators failed. {string.Join(" ", failures)}");
    }

    private static string CreateSuccessDiagnostic(string providerId, IReadOnlyList<string> previousFailures)
    {
        return previousFailures.Count == 0
            ? $"WebAuto used {providerId}."
            : $"WebAuto used {providerId} after {previousFailures.Count} provider fallback(s).";
    }

    private static TranslateRequest CreateProviderRequest(TranslateRequest request, string providerId)
    {
        return new TranslateRequest(
            request.Texts,
            request.SourceLanguage,
            request.TargetLanguage,
            new TranslatorCredentials(
                "experimental-web-provider",
                providerId,
                "global",
                new Uri(GetEndpoint(providerId))));
    }

    private static string GetEndpoint(string providerId)
    {
        return providerId switch
        {
            "BingWeb" => "https://www.bing.com",
            "YandexWeb" => "https://translate.yandex.net",
            _ => "https://translate.googleapis.com",
        };
    }
}
