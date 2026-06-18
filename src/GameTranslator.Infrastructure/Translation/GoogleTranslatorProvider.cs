using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameTranslator.Application.Translation;

namespace GameTranslator.Infrastructure.Translation;

public sealed class GoogleTranslatorProvider : ITranslatorProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;

    public GoogleTranslatorProvider(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public string ProviderId => "Google";

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

        GoogleTranslateTextResponse? payload;
        try
        {
            payload = JsonSerializer.Deserialize<GoogleTranslateTextResponse>(responseBody, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new TranslatorProviderException(
                ProviderId,
                "Google translation response could not be parsed.",
                exception);
        }

        var translations = payload?.Translations ?? Array.Empty<GoogleTranslation>();
        if (translations.Length != request.Texts.Count || translations.Any(translation => string.IsNullOrWhiteSpace(translation.TranslatedText)))
        {
            throw new TranslatorProviderException(
                ProviderId,
                "Google translation response did not contain the expected translated text items.");
        }

        return new TranslateResponse(
            translations.Select(translation => translation.TranslatedText!),
            DateTimeOffset.UtcNow);
    }

    private static HttpRequestMessage CreateHttpRequest(TranslateRequest request)
    {
        var credentials = request.Credentials;
        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            CreateTranslateUri(credentials));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        httpRequest.Headers.TryAddWithoutValidation("x-goog-user-project", credentials.ProjectId);
        httpRequest.Content = JsonContent.Create(
            new GoogleTranslateTextRequest(
                request.SourceLanguage,
                request.TargetLanguage,
                request.Texts),
            options: JsonOptions);

        return httpRequest;
    }

    private static Uri CreateTranslateUri(TranslatorCredentials credentials)
    {
        var endpoint = credentials.Endpoint.ToString().TrimEnd('/');
        var projectId = Uri.EscapeDataString(credentials.ProjectId);
        var location = Uri.EscapeDataString(credentials.Location);

        return new Uri($"{endpoint}/v3/projects/{projectId}/locations/{location}:translateText");
    }

    private TranslatorProviderException CreateProviderException(
        HttpStatusCode statusCode,
        string responseBody,
        string accessToken)
    {
        var errorMessage = ExtractGoogleErrorMessage(responseBody);
        var sanitizedMessage = SanitizeSecret(errorMessage, accessToken);

        return new TranslatorProviderException(
            ProviderId,
            statusCode,
            $"Google translation request failed with HTTP {(int)statusCode}: {sanitizedMessage}");
    }

    private static string ExtractGoogleErrorMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "The provider returned an empty error response.";
        }

        try
        {
            var error = JsonSerializer.Deserialize<GoogleErrorResponse>(responseBody, JsonOptions);

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

    private static string SanitizeSecret(string value, string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return value;
        }

        return value.Replace(secret, "<redacted>", StringComparison.Ordinal);
    }

    private sealed class GoogleTranslateTextRequest
    {
        public GoogleTranslateTextRequest(
            string sourceLanguageCode,
            string targetLanguageCode,
            IEnumerable<string> contents)
        {
            SourceLanguageCode = sourceLanguageCode;
            TargetLanguageCode = targetLanguageCode;
            Contents = contents.ToArray();
        }

        public string SourceLanguageCode { get; }

        public string TargetLanguageCode { get; }

        public IReadOnlyList<string> Contents { get; }
    }

    private sealed class GoogleTranslateTextResponse
    {
        [JsonPropertyName("translations")]
        public GoogleTranslation[]? Translations { get; init; }
    }

    private sealed class GoogleTranslation
    {
        [JsonPropertyName("translatedText")]
        public string? TranslatedText { get; init; }
    }

    private sealed class GoogleErrorResponse
    {
        [JsonPropertyName("error")]
        public GoogleError? Error { get; init; }
    }

    private sealed class GoogleError
    {
        [JsonPropertyName("message")]
        public string? Message { get; init; }
    }
}
