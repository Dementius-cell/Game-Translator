using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Translation;

namespace GameTranslator.Infrastructure.Translation;

/// <summary>
/// Direct Yandex Translate mode compatible with ScreTran's GTranslate 2.2.8 YandexTranslator.
/// </summary>
public sealed class YandexWebTranslatorProvider : ITranslatorProvider
{
    private const string YandexAndroidUserAgent = "ru.yandex.translate/3.20.2024";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly object ucidLock = new();
    private Guid ucid;
    private DateTimeOffset ucidExpiresAt;

    public YandexWebTranslatorProvider(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public string ProviderId => "YandexWeb";

    public async Task<TranslateResponse> TranslateAsync(
        TranslateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var translatedTexts = new List<string>(request.Texts.Count);
        foreach (var text in request.Texts)
        {
            translatedTexts.Add(await TranslateTextAsync(request, text, cancellationToken));
        }

        return new TranslateResponse(translatedTexts, DateTimeOffset.UtcNow, ProviderId);
    }

    private async Task<string> TranslateTextAsync(
        TranslateRequest request,
        string text,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            CreateTranslateUri(request.Credentials.Endpoint));
        httpRequest.Headers.UserAgent.ParseAdd(YandexAndroidUserAgent);
        httpRequest.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["text"] = text,
                // ScreTran calls GTranslate without a source language, so Yandex detects it.
                ["lang"] = YandexHotPatch(NormalizeLanguageTag(request.TargetLanguage)),
            });

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateProviderException(response.StatusCode, responseBody);
        }

        YandexWebResponse? payload;
        try
        {
            payload = JsonSerializer.Deserialize<YandexWebResponse>(responseBody, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new TranslatorProviderException(
                ProviderId,
                TranslatorProviderFailureKind.Parse,
                "YandexWeb translation response could not be parsed.",
                exception);
        }

        if (payload is null)
        {
            throw new TranslatorProviderException(
                ProviderId,
                TranslatorProviderFailureKind.Parse,
                "YandexWeb translation response was empty.");
        }

        if (payload.Code != 200)
        {
            throw CreateProviderException(payload.Code, payload.Message);
        }

        if (string.IsNullOrWhiteSpace(payload.Lang) || !payload.Lang.Contains('-', StringComparison.Ordinal))
        {
            throw new TranslatorProviderException(
                ProviderId,
                TranslatorProviderFailureKind.UnsupportedResponse,
                "YandexWeb translation response did not identify a source and target language.");
        }

        var translation = payload.Text?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(translation))
        {
            throw new TranslatorProviderException(
                ProviderId,
                TranslatorProviderFailureKind.EmptyResponse,
                "YandexWeb translation response did not contain translated text.");
        }

        return translation;
    }

    private Uri CreateTranslateUri(Uri endpoint)
    {
        var root = endpoint.ToString().TrimEnd('/');
        return new Uri(
            $"{root}/api/v1/tr.json/translate?ucid={GetOrCreateUcid():N}&srv=android&format=text");
    }

    private Guid GetOrCreateUcid()
    {
        lock (ucidLock)
        {
            if (ucid == Guid.Empty || ucidExpiresAt <= DateTimeOffset.UtcNow)
            {
                ucid = Guid.NewGuid();
                ucidExpiresAt = DateTimeOffset.UtcNow.AddSeconds(360);
            }

            return ucid;
        }
    }

    private TranslatorProviderException CreateProviderException(HttpStatusCode statusCode, string responseBody)
    {
        return new TranslatorProviderException(
            ProviderId,
            statusCode,
            $"YandexWeb translation request failed with HTTP {(int)statusCode}: {CreateSafeErrorMessage(responseBody)}");
    }

    private TranslatorProviderException CreateProviderException(int? providerCode, string? message)
    {
        var safeMessage = CreateSafeErrorMessage(message);
        if (providerCode is >= 100 and <= 599)
        {
            var statusCode = (HttpStatusCode)providerCode.Value;
            return new TranslatorProviderException(
                ProviderId,
                statusCode,
                $"YandexWeb translation request failed with provider code {providerCode}: {safeMessage}");
        }

        return new TranslatorProviderException(
            ProviderId,
            TranslatorProviderFailureKind.ProviderCode,
            $"YandexWeb translation request failed with provider code {providerCode?.ToString() ?? "(missing)"}: {safeMessage}");
    }

    private static string NormalizeLanguageTag(string languageTag)
    {
        return TesseractLanguageCatalog.TryMapTrainedDataCodeToPreferredLanguageTag(languageTag, out var preferredLanguageTag)
            ? preferredLanguageTag
            : languageTag.Trim();
    }

    private static string YandexHotPatch(string languageCode)
    {
        return languageCode switch
        {
            "pt-PT" => "pt",
            "pt" => "pt-BR",
            "zh-CN" => "zh",
            _ => languageCode,
        };
    }

    private static string CreateSafeErrorMessage(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "The provider returned an empty error response.";
        }

        return responseBody.Length > 300
            ? responseBody[..300]
            : responseBody;
    }

    private sealed class YandexWebResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("lang")]
        public string? Lang { get; init; }

        [JsonPropertyName("text")]
        public string[]? Text { get; init; }
    }
}
