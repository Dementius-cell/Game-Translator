using System.Net;
using System.Text;
using System.Text.Json;
using GameTranslator.Application.Translation;

namespace GameTranslator.Infrastructure.Translation;

public sealed class GoogleWebTranslatorProvider : ITranslatorProvider
{
    private readonly HttpClient httpClient;

    public GoogleWebTranslatorProvider(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public string ProviderId => "GoogleWeb";

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
            HttpMethod.Get,
            CreateTranslateUri(request, text));
        AddBrowserHeaders(httpRequest);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateProviderException(response.StatusCode, responseBody);
        }

        try
        {
            return ParseGoogleWebTranslation(responseBody);
        }
        catch (TranslatorProviderException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new TranslatorProviderException(
                ProviderId,
                TranslatorProviderFailureKind.Parse,
                "GoogleWeb translation response could not be parsed.",
                exception);
        }
    }

    private static Uri CreateTranslateUri(TranslateRequest request, string text)
    {
        var endpoint = request.Credentials.Endpoint.ToString().TrimEnd('/');
        var source = Uri.EscapeDataString(request.SourceLanguage);
        var target = Uri.EscapeDataString(request.TargetLanguage);
        var query = Uri.EscapeDataString(text);

        return new Uri($"{endpoint}/translate_a/single?client=gtx&sl={source}&tl={target}&dt=t&q={query}");
    }

    private static string ParseGoogleWebTranslation(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array
            || root.GetArrayLength() == 0
            || root[0].ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("GoogleWeb response root did not contain translation segments.");
        }

        var builder = new StringBuilder();
        foreach (var segment in root[0].EnumerateArray())
        {
            if (segment.ValueKind != JsonValueKind.Array
                || segment.GetArrayLength() == 0
                || segment[0].ValueKind != JsonValueKind.String)
            {
                continue;
            }

            builder.Append(segment[0].GetString());
        }

        var translation = builder.ToString();
        if (string.IsNullOrWhiteSpace(translation))
        {
            throw new TranslatorProviderException(
                "GoogleWeb",
                TranslatorProviderFailureKind.EmptyResponse,
                "GoogleWeb translation response did not contain translated text.");
        }

        return translation;
    }

    private TranslatorProviderException CreateProviderException(HttpStatusCode statusCode, string responseBody)
    {
        return new TranslatorProviderException(
            ProviderId,
            statusCode,
            $"GoogleWeb translation request failed with HTTP {(int)statusCode}: {CreateSafeErrorMessage(responseBody)}");
    }

    private static string CreateSafeErrorMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "The provider returned an empty error response.";
        }

        return responseBody.Length > 300
            ? responseBody[..300]
            : responseBody;
    }

    private static void AddBrowserHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 GameTranslator/1.0");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }
}
