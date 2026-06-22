using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GameTranslator.Application.Translation;

namespace GameTranslator.Infrastructure.Translation;

public sealed class BingWebTranslatorProvider : ITranslatorProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim sessionLock = new(1, 1);
    private BingWebSession? session;

    public BingWebTranslatorProvider(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public string ProviderId => "BingWeb";

    public async Task<TranslateResponse> TranslateAsync(
        TranslateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var translatedTexts = new List<string>(request.Texts.Count);
        foreach (var text in request.Texts)
        {
            translatedTexts.Add(await TranslateTextWithFreshSessionAsync(request, text, cancellationToken));
        }

        return new TranslateResponse(translatedTexts, DateTimeOffset.UtcNow);
    }

    private async Task<string> TranslateTextWithFreshSessionAsync(
        TranslateRequest request,
        string text,
        CancellationToken cancellationToken)
    {
        var sessionSnapshot = await GetSessionAsync(request.Credentials.Endpoint, forceRefresh: true, cancellationToken);

        try
        {
            return await TranslateTextAsync(request, sessionSnapshot, text, cancellationToken);
        }
        catch (TranslatorProviderException) when (!cancellationToken.IsCancellationRequested)
        {
            InvalidateSession();
            sessionSnapshot = await GetSessionAsync(request.Credentials.Endpoint, forceRefresh: true, cancellationToken);

            return await TranslateTextAsync(request, sessionSnapshot, text, cancellationToken);
        }
    }

    private async Task<string> TranslateTextAsync(
        TranslateRequest request,
        BingWebSession sessionSnapshot,
        string text,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            CreateTranslateUri(request.Credentials.Endpoint, sessionSnapshot));
        AddBrowserHeaders(httpRequest);
        httpRequest.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["fromLang"] = request.SourceLanguage,
                ["to"] = request.TargetLanguage,
                ["text"] = text,
                ["token"] = sessionSnapshot.Token,
                ["key"] = sessionSnapshot.Key,
            });

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateProviderException(response.StatusCode, responseBody);
        }

        BingTranslateResponseItem[]? payload;
        try
        {
            payload = JsonSerializer.Deserialize<BingTranslateResponseItem[]>(responseBody, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new TranslatorProviderException(
                ProviderId,
                "BingWeb translation response could not be parsed.",
                exception);
        }

        var translation = payload?.FirstOrDefault()?.Translations?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(translation))
        {
            throw new TranslatorProviderException(
                ProviderId,
                "BingWeb translation response did not contain translated text.");
        }

        return translation;
    }

    private async Task<BingWebSession> GetSessionAsync(
        Uri endpoint,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh && session is { ExpiresAt: var expiresAt } cached && expiresAt > DateTimeOffset.UtcNow)
        {
            return cached;
        }

        await sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh
                && session is { ExpiresAt: var expiresAtAfterLock } cachedAfterLock
                && expiresAtAfterLock > DateTimeOffset.UtcNow)
            {
                return cachedAfterLock;
            }

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri($"{endpoint.ToString().TrimEnd('/')}/translator"));
            AddBrowserHeaders(httpRequest);

            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateProviderException(response.StatusCode, html);
            }

            session = ParseSession(html);
            return session;
        }
        finally
        {
            sessionLock.Release();
        }
    }

    private void InvalidateSession()
    {
        session = null;
    }

    private static Uri CreateTranslateUri(Uri endpoint, BingWebSession sessionSnapshot)
    {
        var root = endpoint.ToString().TrimEnd('/');
        var ig = Uri.EscapeDataString(sessionSnapshot.Ig);
        var iid = Uri.EscapeDataString(sessionSnapshot.Iid);

        return new Uri($"{root}/ttranslatev3?isVertical=1&IG={ig}&IID={iid}");
    }

    private static BingWebSession ParseSession(string html)
    {
        var abuseMatch = Regex.Match(
            html,
            @"params_AbusePreventionHelper\s*=\s*\[\s*[""']?(?<key>[^,""'\]\s]+)[""']?\s*,\s*[""'](?<token>[^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (!abuseMatch.Success)
        {
            throw new TranslatorProviderException(
                "BingWeb",
                "BingWeb session token could not be found in the translator page.");
        }

        var ig = MatchFirstGroup(
            html,
            @"IG:""(?<value>[^""]+)""",
            @"""IG"":""(?<value>[^""]+)""");
        if (string.IsNullOrWhiteSpace(ig))
        {
            throw new TranslatorProviderException(
                "BingWeb",
                "BingWeb session IG value could not be found in the translator page.");
        }

        var iid = MatchFirstGroup(
            html,
            @"data-iid=""(?<value>translator\.[^""]+)""",
            @"""iid"":""(?<value>translator\.[^""]+)""");

        return new BingWebSession(
            abuseMatch.Groups["key"].Value,
            abuseMatch.Groups["token"].Value,
            ig,
            string.IsNullOrWhiteSpace(iid) ? "translator.5028" : iid,
            DateTimeOffset.UtcNow.AddMinutes(20));
    }

    private static string? MatchFirstGroup(string input, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups["value"].Value;
            }
        }

        return null;
    }

    private TranslatorProviderException CreateProviderException(HttpStatusCode statusCode, string responseBody)
    {
        return new TranslatorProviderException(
            ProviderId,
            statusCode,
            $"BingWeb translation request failed with HTTP {(int)statusCode}: {CreateSafeErrorMessage(responseBody)}");
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

    private sealed record BingWebSession(
        string Key,
        string Token,
        string Ig,
        string Iid,
        DateTimeOffset ExpiresAt);

    private sealed class BingTranslateResponseItem
    {
        [JsonPropertyName("translations")]
        public BingTranslation[]? Translations { get; init; }
    }

    private sealed class BingTranslation
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }
    }
}
