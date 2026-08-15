using System.Net;
using System.Net.Http;
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
        Assert.Contains("fromLang=en", handler.Requests[1].Content, StringComparison.Ordinal);
        Assert.Contains("to=ru", handler.Requests[1].Content, StringComparison.Ordinal);
        Assert.Contains("token=TOKENVALUE", handler.Requests[1].Content, StringComparison.Ordinal);
        Assert.Contains("key=12345", handler.Requests[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BingWebTranslateAsync_WhenSessionTokenFails_RetriesWithFreshSession()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateHtmlContent(CreateBingSessionHtml("OLDIG", "11111", "OLDTOKEN")),
            },
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = CreateJsonContent("""{ "error": "stale token" }"""),
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateHtmlContent(CreateBingSessionHtml("NEWIG", "22222", "NEWTOKEN")),
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent("""[{ "translations": [ { "text": "Привет", "to": "ru" } ] }]"""),
            });
        var provider = new BingWebTranslatorProvider(new HttpClient(handler));

        var response = await provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com"));

        Assert.Equal(new[] { "Привет" }, response.TranslatedTexts);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Contains("token=OLDTOKEN", handler.Requests[1].Content, StringComparison.Ordinal);
        Assert.Contains("key=11111", handler.Requests[1].Content, StringComparison.Ordinal);
        Assert.Contains("token=NEWTOKEN", handler.Requests[3].Content, StringComparison.Ordinal);
        Assert.Contains("key=22222", handler.Requests[3].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task YandexWebTranslateAsync_WhenResponseContainsText_ReturnsTranslatedText()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateHtmlContent(CreateYandexSessionHtml("de0be810.179383a6.9f2492fa.47875647d22747")),
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent("""{ "code": 200, "lang": "en-ru", "text": [ "Привет" ] }"""),
            });
        var provider = new YandexWebTranslatorProvider(new HttpClient(handler));

        var response = await provider.TranslateAsync(CreateRequest("YandexWeb", "https://translate.yandex.net"));

        Assert.Equal(new[] { "Привет" }, response.TranslatedTexts);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Contains("/api/v1/tr.json/translate", handler.Requests[1].Uri?.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("srv=tr-text", handler.Requests[1].Uri?.Query, StringComparison.Ordinal);
        Assert.Contains("id=de0be810.179383a6.9f2492fa.47875647d22747-0-0", handler.Requests[1].Uri?.Query, StringComparison.Ordinal);
        Assert.Contains("lang=en-ru", handler.Requests[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task YandexWebTranslateAsync_WhenSessionPageIsCaptcha_ThrowsProviderException()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://translate.yandex.ru/showcaptchafast"),
                Content = CreateHtmlContent("<html>SmartCaptcha</html>"),
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateHtmlContent("<html>SmartCaptcha</html>"),
            });
        var provider = new YandexWebTranslatorProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest("YandexWeb", "https://translate.yandex.net")));

        Assert.Equal("YandexWeb", exception.ProviderId);
        Assert.Equal(TranslatorProviderFailureKind.Throttled, exception.FailureKind);
        Assert.Contains("session could not be created", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("captcha", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WebAutoTranslateAsync_WhenGoogleWebFails_FallsBackToBingWeb()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = CreateJsonContent("""{ "error": "rate limited" }"""),
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateHtmlContent(CreateBingSessionHtml("ABCDEF123456", "12345", "TOKENVALUE")),
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent("""[{ "translations": [ { "text": "Привет", "to": "ru" } ] }]"""),
            });
        var provider = new WebAutoTranslatorProvider(new HttpClient(handler));

        var response = await provider.TranslateAsync(CreateRequest("WebAuto", "https://translate.googleapis.com"));

        Assert.Equal(new[] { "Привет" }, response.TranslatedTexts);
        Assert.Equal("BingWeb", response.ProviderId);
        Assert.Contains("WebAuto used BingWeb", response.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Contains("1 provider fallback", response.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("/translate_a/single", handler.Requests[0].Uri?.AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith("/translator", handler.Requests[1].Uri?.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("/ttranslatev3", handler.Requests[2].Uri?.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WebAutoTranslateAsync_WhenAllProvidersFail_ReportsFallbackFailureCategories()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = CreateJsonContent("""{ "error": "rate limited" }"""),
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateHtmlContent("<html>missing session</html>"),
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateHtmlContent("<html>SmartCaptcha</html>"),
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateHtmlContent("<html>SmartCaptcha</html>"),
            });
        var provider = new WebAutoTranslatorProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest("WebAuto", "https://translate.googleapis.com")));

        Assert.Equal("WebAuto", exception.ProviderId);
        Assert.Equal(TranslatorProviderFailureKind.AllProvidersFailed, exception.FailureKind);
        Assert.Contains("GoogleWeb [Throttled]", exception.Message, StringComparison.Ordinal);
        Assert.Contains("BingWeb [UnsupportedResponse]", exception.Message, StringComparison.Ordinal);
        Assert.Contains("YandexWeb [Throttled]", exception.Message, StringComparison.Ordinal);
    }

    private static TranslateRequest CreateRequest(string provider, string endpoint, string sourceLanguage = "en", string targetLanguage = "ru")
    {
        return new TranslateRequest(
            new[] { "Hello" },
            sourceLanguage,
            targetLanguage,
            new TranslatorCredentials(
                "experimental-web-provider",
                provider,
                "global",
                new Uri(endpoint)));
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

    private static string CreateYandexSessionHtml(string sid)
    {
        return $$"""
                 <html>
                 <script>
                 window.__INITIAL_STATE__ = {"SID":"{{sid}}","SRV":"tr-text"};
                 </script>
                 </html>
                 """;
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

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri? Uri,
        string Content);
}
