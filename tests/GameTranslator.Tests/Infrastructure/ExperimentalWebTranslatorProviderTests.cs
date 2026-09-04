using System.Net;
using System.Net.Http;
using System.Globalization;
using System.Text;
using GameTranslator.Application.Translation;
using GameTranslator.Infrastructure.Translation;

namespace GameTranslator.Tests.Infrastructure;

public sealed class ExperimentalWebTranslatorProviderTests
{
    [Fact]
    public async Task GoogleWebTranslateAsync_WhenResponseContainsSegments_ReturnsTranslatedText()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent("""[[["При","Hello",null,null,10],["вет","",null,null,10]],null,"en"]"""),
            });
        var provider = new GoogleWebTranslatorProvider(new HttpClient(handler));

        var response = await provider.TranslateAsync(CreateRequest("GoogleWeb", "https://translate.googleapis.com"));

        Assert.Equal(new[] { "Привет" }, response.TranslatedTexts);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("/translate_a/single", request.Uri?.ToString(), StringComparison.Ordinal);
        Assert.Contains("client=gtx", request.Uri?.Query, StringComparison.Ordinal);
        Assert.Contains("sl=en", request.Uri?.Query, StringComparison.Ordinal);
        Assert.Contains("tl=ru", request.Uri?.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GoogleWebTranslateAsync_WhenProviderThrottles_ReportsThrottledFailure()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = CreateJsonContent("""{ "error": "rate limited" }"""),
            });
        var provider = new GoogleWebTranslatorProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest("GoogleWeb", "https://translate.googleapis.com")));

        Assert.Equal("GoogleWeb", exception.ProviderId);
        Assert.Equal(TranslatorProviderFailureKind.Throttled, exception.FailureKind);
        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
    }

    [Fact]
    public async Task GoogleWebTranslateAsync_WhenTesseractThaiModelTagIsConfigured_UsesGoogleThaiTag()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent("""[[["РџСЂРёРІРµС‚","สวัสดี",null,null,10]],null,"th"]"""),
            });
        var provider = new GoogleWebTranslatorProvider(new HttpClient(handler));

        await provider.TranslateAsync(CreateRequest("GoogleWeb", "https://translate.googleapis.com", sourceLanguage: "tha"));

        var request = Assert.Single(handler.Requests);
        Assert.Contains("sl=th", request.Uri?.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("sl=tha", request.Uri?.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GoogleWebTranslateAsync_WhenResponseCannotBeParsed_ReportsParseFailure()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent("""{ "unexpected": true }"""),
            });
        var provider = new GoogleWebTranslatorProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest("GoogleWeb", "https://translate.googleapis.com")));

        Assert.Equal("GoogleWeb", exception.ProviderId);
        Assert.Equal(TranslatorProviderFailureKind.Parse, exception.FailureKind);
    }

    [Fact]
    public async Task GoogleWebTranslateAsync_WhenResponseHasNoTranslatedText_ReportsEmptyResponse()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent("""[[["","Hello",null,null,10]],null,"en"]"""),
            });
        var provider = new GoogleWebTranslatorProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest("GoogleWeb", "https://translate.googleapis.com")));

        Assert.Equal("GoogleWeb", exception.ProviderId);
        Assert.Equal(TranslatorProviderFailureKind.EmptyResponse, exception.FailureKind);
    }

    [Fact]
    public async Task BingWebTranslateAsync_WhenSessionAndTranslationSucceed_ReturnsTranslatedText()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateHtmlContent(CreateBingSessionHtml("ABCDEF123456", "12345", "TOKENVALUE")),
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent("""[{ "translations": [ { "text": "Привет", "to": "ru" } ] }]"""),
            });
        var provider = new BingWebTranslatorProvider(new HttpClient(handler));

        var response = await provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com"));

        Assert.Equal(new[] { "Привет" }, response.TranslatedTexts);
        Assert.Equal("BingWeb", response.ProviderId);
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith("/translator", handler.Requests[0].Uri?.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("/ttranslatev3", handler.Requests[1].Uri?.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("IID=translator.5024.1", handler.Requests[1].Uri?.Query, StringComparison.Ordinal);
        Assert.Contains("fromLang=auto-detect", handler.Requests[1].Content, StringComparison.Ordinal);
        Assert.Contains("to=ru", handler.Requests[1].Content, StringComparison.Ordinal);
        Assert.Contains("token=TOKENVALUE", handler.Requests[1].Content, StringComparison.Ordinal);
        Assert.Contains("key=12345", handler.Requests[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BingWebTranslateAsync_WhenDirectRequestFails_DoesNotRetry()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateHtmlContent(CreateBingSessionHtml("OLDIG", "11111", "OLDTOKEN")),
            },
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = CreateJsonContent("""{ "error": "stale token" }"""),
            });
        var provider = new BingWebTranslatorProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com")));

        Assert.Equal("BingWeb", exception.ProviderId);
        Assert.Equal(TranslatorProviderFailureKind.Http, exception.FailureKind);
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("token=OLDTOKEN", handler.Requests[1].Content, StringComparison.Ordinal);
        Assert.Contains("key=11111", handler.Requests[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BingWebTranslateAsync_RecordsEachActualNetworkAttemptSeparatelyFromQueueTime()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateHtmlContent(CreateBingSessionHtml("ABCDEF123456", "12345", "TOKENVALUE")),
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent("""[{ "translations": [ { "text": "Привет", "to": "ru" } ] }]"""),
            });
        var diagnostics = new TranslationProviderRequestDiagnostics(
            new[] { "Hello" },
            DateTimeOffset.UtcNow.AddSeconds(-1));
        var provider = new BingWebTranslatorProvider(new HttpClient(handler));

        await provider.TranslateAsync(CreateRequest(
            "BingWeb",
            "https://www.bing.com",
            diagnostics: diagnostics));

        var snapshot = diagnostics.CreateSnapshot();
        Assert.NotNull(snapshot.ProviderInvocationStartedAt);
        Assert.Equal(2, snapshot.NetworkAttempts.Count);
        Assert.Collection(
            snapshot.NetworkAttempts,
            credentialsAttempt =>
            {
                Assert.Equal(TranslationProviderNetworkRequestKind.Credentials, credentialsAttempt.Kind);
                Assert.True(credentialsAttempt.WasSent);
                Assert.Equal(TranslationProviderNetworkRequestOutcome.Succeeded, credentialsAttempt.Outcome);
                Assert.Equal(HttpStatusCode.OK, credentialsAttempt.StatusCode);
            },
            translationAttempt =>
            {
                Assert.Equal(TranslationProviderNetworkRequestKind.Translation, translationAttempt.Kind);
                Assert.True(translationAttempt.WasSent);
                Assert.Equal(TranslationProviderNetworkRequestOutcome.Succeeded, translationAttempt.Outcome);
                Assert.Equal(HttpStatusCode.OK, translationAttempt.StatusCode);
            });
    }

    [Fact]
    public async Task BingWebTranslateAsync_WhenTwoRequestsTimeout_OpensCooldownWithoutRetrying()
    {
        var handler = new ScriptedHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateHtmlContent(CreateBingSessionHtml("ABCDEF123456", CreateCurrentBingKey(), "TOKENVALUE")),
            }),
            (_, cancellationToken) => WaitForCancellationAsync(cancellationToken),
            (_, cancellationToken) => WaitForCancellationAsync(cancellationToken));
        var provider = new BingWebTranslatorProvider(
            new HttpClient(handler),
            TimeProvider.System,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromSeconds(60));

        var first = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com")));
        var second = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com")));
        var paused = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com")));

        Assert.Equal(TranslatorProviderFailureKind.Timeout, first.FailureKind);
        Assert.Equal(1, first.ConsecutiveFailureCount);
        Assert.Null(first.RetryAfter);
        Assert.Null(first.NextRetryAt);
        Assert.Equal(TranslatorProviderFailureKind.Timeout, second.FailureKind);
        Assert.Equal(2, second.ConsecutiveFailureCount);
        Assert.Equal(TimeSpan.FromSeconds(60), second.RetryAfter);
        Assert.NotNull(second.NextRetryAt);
        Assert.Equal(TranslatorProviderFailureKind.Timeout, paused.FailureKind);
        Assert.Equal(2, paused.ConsecutiveFailureCount);
        Assert.InRange(paused.RetryAfter!.Value, TimeSpan.FromSeconds(59), TimeSpan.FromSeconds(60));
        Assert.Equal(second.NextRetryAt, paused.NextRetryAt);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task BingWebTranslateAsync_WhenProviderReturns429_UsesRetryAfterAndPausesImmediately()
    {
        var throttledResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = CreateJsonContent("""{ "error": "rate limited" }"""),
        };
        throttledResponse.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.FromSeconds(90));
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateHtmlContent(CreateBingSessionHtml("ABCDEF123456", CreateCurrentBingKey(), "TOKENVALUE")),
            },
            throttledResponse);
        var provider = new BingWebTranslatorProvider(
            new HttpClient(handler),
            TimeProvider.System,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(60));

        var throttled = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com")));
        var pausedDiagnostics = new TranslationProviderRequestDiagnostics(
            new[] { "Hello" },
            DateTimeOffset.UtcNow);
        var paused = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest(
                "BingWeb",
                "https://www.bing.com",
                diagnostics: pausedDiagnostics)));

        Assert.Equal(TranslatorProviderFailureKind.Throttled, throttled.FailureKind);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(90), throttled.RetryAfter);
        Assert.NotNull(throttled.NextRetryAt);
        Assert.Equal(TranslatorProviderFailureKind.Throttled, paused.FailureKind);
        Assert.InRange(paused.RetryAfter!.Value, TimeSpan.FromSeconds(89), TimeSpan.FromSeconds(90));
        Assert.Equal(throttled.NextRetryAt, paused.NextRetryAt);
        Assert.Equal(2, handler.Requests.Count);
        var pausedSnapshot = pausedDiagnostics.CreateSnapshot();
        Assert.Equal(TranslationProviderInvocationOutcome.RejectedBeforeSend, pausedSnapshot.Outcome);
        Assert.False(pausedSnapshot.WasNetworkRequestSent);
        Assert.Empty(pausedSnapshot.NetworkAttempts);
    }

    [Fact]
    public async Task BingWebTranslateAsync_WhenSuccessFollowsTimeout_ResetsConsecutiveTimeoutCount()
    {
        var handler = new ScriptedHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateHtmlContent(CreateBingSessionHtml("ABCDEF123456", CreateCurrentBingKey(), "TOKENVALUE")),
            }),
            (_, cancellationToken) => WaitForCancellationAsync(cancellationToken),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent("""[{ "translations": [ { "text": "Привет", "to": "ru" } ] }]"""),
            }),
            (_, cancellationToken) => WaitForCancellationAsync(cancellationToken));
        var provider = new BingWebTranslatorProvider(
            new HttpClient(handler),
            TimeProvider.System,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromSeconds(60));

        var first = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com")));
        var success = await provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com"));
        var afterSuccess = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com")));

        Assert.Equal(1, first.ConsecutiveFailureCount);
        Assert.Equal(new[] { "Привет" }, success.TranslatedTexts);
        Assert.Equal(TranslatorProviderFailureKind.Timeout, afterSuccess.FailureKind);
        Assert.Equal(1, afterSuccess.ConsecutiveFailureCount);
        Assert.Null(afterSuccess.RetryAfter);
        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task YandexWebTranslateAsync_WhenResponseContainsText_ReturnsTranslatedText()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent("""{ "code": 200, "lang": "en-ru", "text": [ "Привет" ] }"""),
            });
        var provider = new YandexWebTranslatorProvider(new HttpClient(handler));

        var response = await provider.TranslateAsync(CreateRequest("YandexWeb", "https://translate.yandex.net"));

        Assert.Equal(new[] { "Привет" }, response.TranslatedTexts);
        var translateRequest = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, translateRequest.Method);
        Assert.Contains("/api/v1/tr.json/translate", translateRequest.Uri?.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("ucid=", translateRequest.Uri?.Query, StringComparison.Ordinal);
        Assert.Contains("srv=android", translateRequest.Uri?.Query, StringComparison.Ordinal);
        Assert.Contains("format=text", translateRequest.Uri?.Query, StringComparison.Ordinal);
        Assert.Contains("lang=ru", translateRequest.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task YandexWebTranslateAsync_WhenProviderReturnsRateLimit_ReportsThrottledFailure()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent("""{ "code": 429, "message": "rate limited" }"""),
            });
        var provider = new YandexWebTranslatorProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest("YandexWeb", "https://translate.yandex.net")));

        Assert.Equal("YandexWeb", exception.ProviderId);
        Assert.Equal(TranslatorProviderFailureKind.Throttled, exception.FailureKind);
        Assert.Contains("provider code 429", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static TranslateRequest CreateRequest(
        string provider,
        string endpoint,
        string sourceLanguage = "en",
        string targetLanguage = "ru",
        TranslationProviderRequestDiagnostics? diagnostics = null)
    {
        return new TranslateRequest(
            new[] { "Hello" },
            sourceLanguage,
            targetLanguage,
            new TranslatorCredentials(
                "experimental-web-provider",
                provider,
                "global",
                new Uri(endpoint)),
            diagnostics);
    }

    private static StringContent CreateJsonContent(string json)
    {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static StringContent CreateHtmlContent(string html)
    {
        return new StringContent(html, Encoding.UTF8, "text/html");
    }

    private static string CreateBingSessionHtml(string ig, string key, string token)
    {
        return $"""
                <html>
                <script>IG:"{ig}"</script>
                <script>var params_AbusePreventionHelper = [{key},"{token}",3600000];</script>
                <div data-iid="translator.5028"></div>
                </html>
                """;
    }

    private static string CreateCurrentBingKey()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
    }

    private static async Task<HttpResponseMessage> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The request should have been cancelled by the provider timeout.");
    }

    private sealed class SequenceHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses;

        public SequenceHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            this.responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken)));

            return responses.Dequeue();
        }
    }

    private sealed class ScriptedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> responses;

        public ScriptedHttpMessageHandler(
            params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] responses)
        {
            this.responses = new Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(responses);
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return responses.Dequeue()(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri? Uri,
        string Content);
}
