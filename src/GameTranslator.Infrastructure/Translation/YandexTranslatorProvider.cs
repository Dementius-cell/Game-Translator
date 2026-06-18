using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameTranslator.Application.Translation;

namespace GameTranslator.Infrastructure.Translation;

public sealed class YandexTranslatorProvider : ITranslatorProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;

    public YandexTranslatorProvider(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public string ProviderId => "Yandex";

    public async Task<TranslateResponse> TranslateAsync(
        TranslateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var httpRequest = CreateHttpRequest(request);
        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateProviderException(response.StatusCode, responseBody, request.Credentials.AccessToken);
        }

        YandexTranslateResponse? payload;
        try
        {
            payload = JsonSerializer.Deserialize<YandexTranslateResponse>(responseBody, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new TranslatorProviderException(
                ProviderId,
                "Yandex translation response could not be parsed.",
                exception);
        }

        var translations = payload?.Translations ?? Array.Empty<YandexTranslation>();
        if (translations.Length != request.Texts.Count || translations.Any(translation => string.IsNullOrWhiteSpace(translation.Text)))
        {
            throw new TranslatorProviderException(
                ProviderId,
                "Yandex translation response did not contain the expected translated text items.");
        }

        return new TranslateResponse(
            translations.Select(translation => translation.Text!),
            DateTimeOffset.UtcNow);
    }

    private static HttpRequestMessage CreateHttpRequest(TranslateRequest request)
    {
        var credentials = request.Credentials;
        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            CreateTranslateUri(credentials));
        httpRequest.Headers.Authorization = CreateAuthorizationHeader(credentials.AccessToken);
        httpRequest.Content = JsonContent.Create(
            new YandexTranslateRequest(
                credentials.ProjectId,
                request.SourceLanguage,
                request.TargetLanguage,
                request.Texts),
            options: JsonOptions);

        return httpRequest;
    }

    private static Uri CreateTranslateUri(TranslatorCredentials credentials)
    {
        var endpoint = credentials.Endpoint.ToString().TrimEnd('/');

        return new Uri($"{endpoint}/translate/v2/translate");
    }

    private static AuthenticationHeaderValue CreateAuthorizationHeader(string accessToken)
    {
        var trimmed = accessToken.Trim();
        var separatorIndex = trimmed.IndexOf(' ', StringComparison.Ordinal);
        if (separatorIndex > 0 && separatorIndex < trimmed.Length - 1)
        {
            return new AuthenticationHeaderValue(
                trimmed[..separatorIndex],
                trimmed[(separatorIndex + 1)..].Trim());
        }

        return new AuthenticationHeaderValue("Bearer", trimmed);
    }

    private TranslatorProviderException CreateProviderException(
        HttpStatusCode statusCode,
        string responseBody,
        string accessToken)
    {
        var errorMessage = ExtractYandexErrorMessage(responseBody);
        var sanitizedMessage = TranslatorSecretRedactor.Redact(errorMessage, accessToken);

        return new TranslatorProviderException(
            ProviderId,
            statusCode,
            $"Yandex translation request failed with HTTP {(int)statusCode}: {sanitizedMessage}");
    }

    private static string ExtractYandexErrorMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "The provider returned an empty error response.";
        }

        try
        {
            var error = JsonSerializer.Deserialize<YandexErrorResponse>(responseBody, JsonOptions);

            if (!string.IsNullOrWhiteSpace(error?.Message))
            {
                return error.Message;
            }
        }
        catch (JsonException)
        {
            return responseBody;
        }

        return responseBody;
    }

    private sealed class YandexTranslateRequest
    {
        public YandexTranslateRequest(
            string folderId,
            string sourceLanguageCode,
            string targetLanguageCode,
            IEnumerable<string> texts)
        {
            FolderId = folderId;
            SourceLanguageCode = sourceLanguageCode;
            TargetLanguageCode = targetLanguageCode;
            Texts = texts.ToArray();
        }

        public string FolderId { get; }

        public string SourceLanguageCode { get; }

        public string TargetLanguageCode { get; }

        public string Format => "PLAIN_TEXT";

        public IReadOnlyList<string> Texts { get; }
    }

    private sealed class YandexTranslateResponse
    {
        [JsonPropertyName("translations")]
        public YandexTranslation[]? Translations { get; init; }
    }

    private sealed class YandexTranslation
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }
    }

    private sealed class YandexErrorResponse
    {
        [JsonPropertyName("message")]
        public string? Message { get; init; }
    }
}
