using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameTranslator.Application.Translation;

namespace GameTranslator.Infrastructure.Translation;

public sealed class AzureTranslatorProvider : ITranslatorProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;

    public AzureTranslatorProvider(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public string ProviderId => "Azure";

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

        AzureTranslateResponseItem[]? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AzureTranslateResponseItem[]>(responseBody, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new TranslatorProviderException(
                ProviderId,
                "Azure translation response could not be parsed.",
                exception);
        }

        var translations = payload ?? Array.Empty<AzureTranslateResponseItem>();
        if (translations.Length != request.Texts.Count
            || translations.Any(item => string.IsNullOrWhiteSpace(item.Translations?.FirstOrDefault()?.Text)))
        {
            throw new TranslatorProviderException(
                ProviderId,
                "Azure translation response did not contain the expected translated text items.");
        }

        return new TranslateResponse(
            translations.Select(item => item.Translations!.First().Text!),
            DateTimeOffset.UtcNow);
    }

    private static HttpRequestMessage CreateHttpRequest(TranslateRequest request)
    {
        var credentials = request.Credentials;
        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            CreateTranslateUri(credentials, request.SourceLanguage, request.TargetLanguage));
        httpRequest.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", credentials.AccessToken);
        if (!string.Equals(credentials.Location, "global", StringComparison.OrdinalIgnoreCase))
        {
            httpRequest.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Region", credentials.Location);
        }

        httpRequest.Content = JsonContent.Create(
            request.Texts.Select(text => new AzureTranslateRequestItem(text)).ToArray(),
            options: JsonOptions);

        return httpRequest;
    }

    private static Uri CreateTranslateUri(
        TranslatorCredentials credentials,
        string sourceLanguage,
        string targetLanguage)
    {
        var endpoint = credentials.Endpoint.ToString().TrimEnd('/');
        var source = Uri.EscapeDataString(sourceLanguage);
        var target = Uri.EscapeDataString(targetLanguage);

        return new Uri($"{endpoint}/translate?api-version=3.0&from={source}&to={target}");
    }

    private TranslatorProviderException CreateProviderException(
        HttpStatusCode statusCode,
        string responseBody,
        string accessToken)
    {
        var errorMessage = ExtractAzureErrorMessage(responseBody);
        var sanitizedMessage = TranslatorSecretRedactor.Redact(errorMessage, accessToken);

        return new TranslatorProviderException(
            ProviderId,
            statusCode,
            $"Azure translation request failed with HTTP {(int)statusCode}: {sanitizedMessage}");
    }

    private static string ExtractAzureErrorMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "The provider returned an empty error response.";
        }

        try
        {
            var error = JsonSerializer.Deserialize<AzureErrorResponse>(responseBody, JsonOptions);

            if (!string.IsNullOrWhiteSpace(error?.Error?.Message))
            {
                return error.Error.Message;
            }
        }
        catch (JsonException)
        {
            return responseBody;
        }

        return responseBody;
    }

    private sealed class AzureTranslateRequestItem
    {
        public AzureTranslateRequestItem(string text)
        {
            Text = text;
        }

        public string Text { get; }
    }

    private sealed class AzureTranslateResponseItem
    {
        [JsonPropertyName("translations")]
        public AzureTranslation[]? Translations { get; init; }
    }

    private sealed class AzureTranslation
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }
    }

    private sealed class AzureErrorResponse
    {
        [JsonPropertyName("error")]
        public AzureError? Error { get; init; }
    }

    private sealed class AzureError
    {
        [JsonPropertyName("message")]
        public string? Message { get; init; }
    }
}
