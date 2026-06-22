using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GameTranslator.Application.Translation;

namespace GameTranslator.Infrastructure.Translation;

public sealed class YandexWebTranslatorProvider : ITranslatorProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Uri TranslateApiUri = new("https://translate.yandex.net/api/v1/tr.json/translate");
    private static readonly Uri[] SessionPageUris =
    {
        new("https://translate.yandex.ru/"),
        new("https://translate.yandex.com/"),
    };

    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim sessionLock = new(1, 1);
    private YandexWebSession? session;

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
            translatedTexts.Add(await TranslateTextWithFreshSessionAsync(request, text, cancellationToken));
        }

        return new TranslateResponse(translatedTexts, DateTimeOffset.UtcNow);
    }

    private async Task<string> TranslateTextWithFreshSessionAsync(
        TranslateRequest request,
        string text,
        CancellationToken cancellationToken)
    {
        var sessionSnapshot = await GetSessionAsync(forceRefresh: true, cancellationToken);

        try
        {
            return await TranslateTextAsync(request, sessionSnapshot, text, cancellationToken);
        }
        catch (TranslatorProviderException) when (!cancellationToken.IsCancellationRequested)
        {
            InvalidateSession();
            sessionSnapshot = await GetSessionAsync(forceRefresh: true, cancellationToken);

            return await TranslateTextAsync(request, sessionSnapshot, text, cancellationToken);
        }
    }

    private async Task<string> TranslateTextAsync(
        TranslateRequest request,
        YandexWebSession sessionSnapshot,
        string text,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            CreateTranslateUri(sessionSnapshot));
        AddBrowserHeaders(httpRequest);
        httpRequest.Headers.Referrer = sessionSnapshot.Referrer;
        httpRequest.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["lang"] = CreateLanguagePair(request),
                ["reason"] = "auto",
                ["format"] = "text",
                ["text"] = text,
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
                "YandexWeb translation response could not be parsed.",
                exception);
        }

        if (payload?.Code is not null && payload.Code != 200)
        {
            throw new TranslatorProviderException(
                ProviderId,
                $"YandexWeb translation request failed with provider code {payload.Code}: {payload.Message ?? "The provider did not return a message."}");
        }

        var translation = payload?.Text?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(translation))
        {
            throw new TranslatorProviderException(
                ProviderId,
                "YandexWeb translation response did not contain translated text.");
        }

        return translation;
    }

    private async Task<YandexWebSession> GetSessionAsync(
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

            var failures = new List<string>();
            foreach (var pageUri in SessionPageUris)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var httpRequest = new HttpRequestMessage(HttpMethod.Get, pageUri);
                    AddBrowserHeaders(httpRequest);

                    using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
                    var html = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        failures.Add($"{pageUri.Host}: HTTP {(int)response.StatusCode}");
                        continue;
                    }

                    if (IsCaptchaPage(response.RequestMessage?.RequestUri, html))
                    {
                        failures.Add($"{pageUri.Host}: captcha page returned");
                        continue;
                    }

                    session = ParseSession(pageUri, html);
                    return session;
                }
                catch (TranslatorProviderException exception)
                {
                    failures.Add($"{pageUri.Host}: {exception.Message}");
                }
            }

            throw new TranslatorProviderException(
                ProviderId,
                $"YandexWeb session could not be created. {string.Join(" ", failures)}");
        }
        finally
        {
            sessionLock.Release();
        }
    }

    private static Uri CreateTranslateUri(YandexWebSession sessionSnapshot)
    {
        return new Uri(
            $"{TranslateApiUri}?id={Uri.EscapeDataString(sessionSnapshot.RequestId)}&srv=tr-text");
    }

    private static string CreateLanguagePair(TranslateRequest request)
    {
        return string.Equals(request.SourceLanguage, "auto", StringComparison.OrdinalIgnoreCase)
            ? request.TargetLanguage
            : $"{request.SourceLanguage}-{request.TargetLanguage}";
    }

    private static YandexWebSession ParseSession(Uri referrer, string html)
    {
        var sid = MatchFirstGroup(
            html,
            @"""SID"":\s*""(?<value>[^""]+)""",
            @"Ya\.i18n\.phrases\.SID\s*=\s*[""'](?<value>[^""']+)[""']");
        if (string.IsNullOrWhiteSpace(sid))
        {
            throw new TranslatorProviderException(
                "YandexWeb",
                "YandexWeb SID could not be found in the translator page.");
        }

        return new YandexWebSession(
            $"{sid}-0-0",
            referrer,
            DateTimeOffset.UtcNow.AddMinutes(5));
    }

    private static bool IsCaptchaPage(Uri? responseUri, string html)
    {
        return responseUri?.AbsoluteUri.Contains("showcaptcha", StringComparison.OrdinalIgnoreCase) == true
            || html.Contains("showcaptcha", StringComparison.OrdinalIgnoreCase)
            || html.Contains("SmartCaptcha", StringComparison.OrdinalIgnoreCase);
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

    private void InvalidateSession()
    {
        session = null;
    }

    private TranslatorProviderException CreateProviderException(HttpStatusCode statusCode, string responseBody)
    {
        return new TranslatorProviderException(
            ProviderId,
            statusCode,
            $"YandexWeb translation request failed with HTTP {(int)statusCode}: {CreateSafeErrorMessage(responseBody)}");
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
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        request.Headers.Accept.ParseAdd("application/json, text/plain, */*");
        request.Headers.AcceptLanguage.ParseAdd("ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
    }

    private sealed record YandexWebSession(
        string RequestId,
        Uri Referrer,
        DateTimeOffset ExpiresAt);

    private sealed class YandexWebResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("text")]
        public string[]? Text { get; init; }
    }
}
