using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Translation;

namespace GameTranslator.Infrastructure.Translation;

/// <summary>
/// Direct Bing Translate mode compatible with ScreTran's GTranslate 2.2.8 BingTranslator.
/// </summary>
public sealed class BingWebTranslatorProvider : ITranslatorProvider
{
    private const int TimeoutFailureThreshold = 2;
    private const string ScreTranBingIid = "translator.5024.1";
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan requestTimeout;
    private readonly TimeSpan defaultCooldown;
    private readonly SemaphoreSlim credentialsLock = new(1, 1);
    private readonly object providerHealthLock = new();
    private BingWebCredentials? credentials;
    private int consecutiveTimeoutCount;
    private ProviderCooldown? cooldown;

    public BingWebTranslatorProvider(HttpClient httpClient)
        : this(httpClient, TimeProvider.System, DefaultRequestTimeout, DefaultCooldown)
    {
    }

    internal BingWebTranslatorProvider(
        HttpClient httpClient,
        TimeProvider timeProvider,
        TimeSpan requestTimeout,
        TimeSpan defaultCooldown)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        if (defaultCooldown <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultCooldown));
        }

        this.requestTimeout = requestTimeout;
        this.defaultCooldown = defaultCooldown;
    }

    public string ProviderId => "BingWeb";

    public async Task<TranslateResponse> TranslateAsync(
        TranslateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfPaused();

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
        if (text.Length > 1000)
        {
            throw new TranslatorProviderException(
                ProviderId,
                TranslatorProviderFailureKind.Configuration,
                "BingWeb accepts at most 1000 characters per direct translation request.");
        }

        var credentialsSnapshot = await GetOrUpdateCredentialsAsync(request.Credentials.Endpoint, cancellationToken);
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            CreateTranslateUri(request.Credentials.Endpoint, credentialsSnapshot));
        AddBrowserHeaders(httpRequest);
        httpRequest.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                // ScreTran calls GTranslate without a source language, so Bing detects it.
                ["fromLang"] = "auto-detect",
                ["text"] = text,
                ["to"] = BingHotPatch(NormalizeLanguageTag(request.TargetLanguage)),
                ["token"] = credentialsSnapshot.Token,
                ["key"] = credentialsSnapshot.Key.ToString(CultureInfo.InvariantCulture),
            });

        using var response = await SendWithTimeoutAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateProviderException(response, responseBody);
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            ThrowIfProviderStatusCodeIsPresent(document);

            var payload = JsonSerializer.Deserialize<BingTranslateResponseItem[]>(responseBody, JsonOptions);
            var translation = payload?.FirstOrDefault()?.Translations?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(translation))
            {
                throw new TranslatorProviderException(
                    ProviderId,
                    TranslatorProviderFailureKind.EmptyResponse,
                    "BingWeb translation response did not contain translated text.");
            }

            RecordSuccess();
            return translation;
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
                "BingWeb translation response could not be parsed.",
                exception);
        }
    }

    private async Task<BingWebCredentials> GetOrUpdateCredentialsAsync(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        if (credentials is { ExpiresAt: var expiresAt } cached && expiresAt > timeProvider.GetUtcNow())
        {
            return cached;
        }

        await credentialsLock.WaitAsync(cancellationToken);
        try
        {
            if (credentials is { ExpiresAt: var expiresAtAfterLock } cachedAfterLock
                && expiresAtAfterLock > timeProvider.GetUtcNow())
            {
                return cachedAfterLock;
            }

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri($"{endpoint.ToString().TrimEnd('/')}/translator"));
            AddBrowserHeaders(httpRequest);

            using var response = await SendWithTimeoutAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateProviderException(response, responseBody);
            }

            credentials = ParseCredentials(responseBody);
            return credentials;
        }
        finally
        {
            credentialsLock.Release();
        }
    }

    private static Uri CreateTranslateUri(Uri endpoint, BingWebCredentials credentialsSnapshot)
    {
        var root = endpoint.ToString().TrimEnd('/');
        var impressionGuid = credentialsSnapshot.ImpressionGuid.ToString("N").ToUpperInvariant();
        return new Uri($"{root}/ttranslatev3?isVertical=1&IG={impressionGuid}&IID={ScreTranBingIid}");
    }

    private BingWebCredentials ParseCredentials(string html)
    {
        var match = Regex.Match(
            html,
            @"params_AbusePreventionHelper\s*=\s*\[\s*(?<key>[^,\]\s]+)\s*,\s*[""'](?<token>[^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            throw new TranslatorProviderException(
                ProviderId,
                TranslatorProviderFailureKind.UnsupportedResponse,
                "BingWeb credentials could not be found in the translator page.");
        }

        var key = long.TryParse(
            match.Groups["key"].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsedKey)
            ? parsedKey
            : timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(key).AddHours(1);
        return new BingWebCredentials(
            key,
            match.Groups["token"].Value,
            Guid.NewGuid(),
            expiresAt);
    }

    private void ThrowIfProviderStatusCodeIsPresent(JsonDocument document)
    {
        if (document.RootElement.ValueKind == JsonValueKind.Array
            || !document.RootElement.TryGetProperty("statusCode", out var statusCodeElement)
            || !statusCodeElement.TryGetInt32(out var statusCode))
        {
            return;
        }

        var message = document.RootElement.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : null;
        throw CreateProviderException(statusCode, message);
    }

    private async Task<HttpResponseMessage> SendWithTimeoutAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ThrowIfPaused();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(requestTimeout);
        try
        {
            return await httpClient.SendAsync(request, timeoutSource.Token);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            throw RecordTimeout(exception);
        }
    }

    private TranslatorProviderException CreateProviderException(
        HttpResponseMessage response,
        string responseBody)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return RecordThrottle(
                ResolveRetryAfter(response),
                response.StatusCode,
                "BingWeb returned HTTP 429 and has been paused.");
        }

        return new TranslatorProviderException(
            ProviderId,
            response.StatusCode,
            $"BingWeb translation request failed with HTTP {(int)response.StatusCode}: {CreateSafeErrorMessage(responseBody)}");
    }

    private TranslatorProviderException CreateProviderException(int providerStatusCode, string? message)
    {
        var safeMessage = CreateSafeErrorMessage(message);
        if (providerStatusCode is >= 100 and <= 599)
        {
            var statusCode = (HttpStatusCode)providerStatusCode;
            if (statusCode == HttpStatusCode.TooManyRequests)
            {
                return RecordThrottle(
                    defaultCooldown,
                    statusCode,
                    "BingWeb returned provider code 429 and has been paused.");
            }

            return new TranslatorProviderException(
                ProviderId,
                statusCode,
                $"BingWeb translation request failed with provider code {providerStatusCode}: {safeMessage}");
        }

        return new TranslatorProviderException(
            ProviderId,
            TranslatorProviderFailureKind.ProviderCode,
            $"BingWeb translation request failed with provider code {providerStatusCode}: {safeMessage}");
    }

    private TranslatorProviderException RecordTimeout(OperationCanceledException exception)
    {
        lock (providerHealthLock)
        {
            var now = timeProvider.GetUtcNow();
            if (cooldown is { Until: var cooldownUntil } activeCooldown && cooldownUntil > now)
            {
                return CreatePausedException(activeCooldown, cooldownUntil - now, exception);
            }

            consecutiveTimeoutCount = checked(consecutiveTimeoutCount + 1);
            if (consecutiveTimeoutCount < TimeoutFailureThreshold)
            {
                return new TranslatorProviderException(
                    ProviderId,
                    TranslatorProviderFailureKind.Timeout,
                    $"BingWeb did not respond within {FormatDuration(requestTimeout)}. No automatic retry was sent; one more consecutive timeout will pause the provider.",
                    exception,
                    consecutiveFailureCount: consecutiveTimeoutCount);
            }

            cooldown = new ProviderCooldown(
                TranslatorProviderFailureKind.Timeout,
                StatusCode: null,
                now + defaultCooldown,
                consecutiveTimeoutCount);
            return CreatePausedException(cooldown, defaultCooldown, exception);
        }
    }

    private TranslatorProviderException RecordThrottle(
        TimeSpan retryAfter,
        HttpStatusCode statusCode,
        string message)
    {
        lock (providerHealthLock)
        {
            var failureCount = Math.Max(1, consecutiveTimeoutCount);
            cooldown = new ProviderCooldown(
                TranslatorProviderFailureKind.Throttled,
                statusCode,
                timeProvider.GetUtcNow() + retryAfter,
                failureCount);
            return new TranslatorProviderException(
                ProviderId,
                statusCode,
                TranslatorProviderFailureKind.Throttled,
                $"{message} Retry after {FormatDuration(retryAfter)}.",
                retryAfter: retryAfter,
                consecutiveFailureCount: failureCount);
        }
    }

    private void RecordSuccess()
    {
        lock (providerHealthLock)
        {
            consecutiveTimeoutCount = 0;
            cooldown = null;
        }
    }

    private void ThrowIfPaused()
    {
        lock (providerHealthLock)
        {
            if (cooldown is null)
            {
                return;
            }

            var remaining = cooldown.Until - timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                cooldown = null;
                return;
            }

            throw CreatePausedException(cooldown, remaining);
        }
    }

    private TranslatorProviderException CreatePausedException(
        ProviderCooldown activeCooldown,
        TimeSpan remaining,
        Exception? innerException = null)
    {
        var message = activeCooldown.FailureKind == TranslatorProviderFailureKind.Throttled
            ? $"BingWeb is paused after HTTP 429. Retry after {FormatDuration(remaining)}."
            : $"BingWeb timed out {activeCooldown.ConsecutiveFailureCount} consecutive times and is paused. Retry after {FormatDuration(remaining)}.";
        return activeCooldown.StatusCode is { } statusCode
            ? new TranslatorProviderException(
                ProviderId,
                statusCode,
                activeCooldown.FailureKind,
                message,
                innerException,
                remaining,
                activeCooldown.ConsecutiveFailureCount)
            : new TranslatorProviderException(
                ProviderId,
                activeCooldown.FailureKind,
                message,
                innerException,
                remaining,
                activeCooldown.ConsecutiveFailureCount);
    }

    private TimeSpan ResolveRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var remaining = date - timeProvider.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                return remaining;
            }
        }

        return defaultCooldown;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalSeconds >= 1
            ? $"{Math.Ceiling(duration.TotalSeconds).ToString(CultureInfo.InvariantCulture)} seconds"
            : $"{Math.Ceiling(duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)} ms";
    }

    private static string NormalizeLanguageTag(string languageTag)
    {
        return TesseractLanguageCatalog.TryMapTrainedDataCodeToPreferredLanguageTag(languageTag, out var preferredLanguageTag)
            ? preferredLanguageTag
            : languageTag.Trim();
    }

    private static string BingHotPatch(string languageCode)
    {
        return languageCode switch
        {
            "lg" => "lug",
            "no" => "nb",
            "ny" => "nya",
            "rn" => "run",
            "sr" => "sr-Cyrl",
            "mn" => "mn-Cyrl",
            "tlh" => "tlh-Latn",
            "zh-CN" => "zh-Hans",
            "zh-TW" => "zh-Hant",
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

    private static void AddBrowserHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 GameTranslator/1.0");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    private sealed record BingWebCredentials(
        long Key,
        string Token,
        Guid ImpressionGuid,
        DateTimeOffset ExpiresAt);

    private sealed record ProviderCooldown(
        TranslatorProviderFailureKind FailureKind,
        HttpStatusCode? StatusCode,
        DateTimeOffset Until,
        int ConsecutiveFailureCount);

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
