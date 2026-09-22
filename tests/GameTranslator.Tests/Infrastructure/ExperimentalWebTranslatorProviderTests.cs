using System.Net;
using System.Net.Http;
using System.Globalization;
using System.Text;
using GameTranslator.Application.Translation;
using GameTranslator.Infrastructure.Translation;

namespace GameTranslator.Tests.Infrastructure;

public sealed class ExperimentalWebTranslatorProviderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BingTransportTimeout_BootstrapRetriesOnceAndThenHonorsCooldown(bool failAgain)
    {
        var handler = new ScriptedHttpMessageHandler(
            (_, _) => throw new TaskCanceledException("Connect timeout", new TimeoutException()),
            (_, _) => failAgain
                ? throw new TaskCanceledException("Connect timeout", new TimeoutException())
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateHtmlContent(CreateBingSessionHtml("IG", CreateCurrentBingKey(), "TOKEN")) }),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent("""[{"translations":[{"text":"Hello","to":"ru"}]}]""") }));
        using var client = new HttpClient(handler);
        using var provider = new BingWebTranslatorProvider(client);
        var diagnostics = new TranslationProviderRequestDiagnostics(new[] { "Hello" }, DateTimeOffset.UtcNow);
        var request = CreateRequest("BingWeb", "https://example.test", diagnostics: diagnostics);
        if (failAgain)
        {
            var failure = await Assert.ThrowsAsync<TranslatorProviderException>(() => provider.TranslateAsync(request));
            Assert.Equal(TranslatorProviderFailureKind.Timeout, failure.FailureKind);
            Assert.NotNull(failure.RetryAfter);
            await Assert.ThrowsAsync<TranslatorProviderException>(() => provider.TranslateAsync(request));
            Assert.Equal(2, handler.RequestCount);
        }
        else
        {
            Assert.Equal("Hello", (await provider.TranslateAsync(request)).TranslatedTexts.Single());
            Assert.Equal(3, handler.RequestCount);
        }
        var attempts = diagnostics.CreateSnapshot().NetworkAttempts;
        Assert.Equal(TranslationProviderNetworkRequestOutcome.Timeout, attempts[0].Outcome);
        Assert.Equal(2, attempts.Count(a => a.Kind == TranslationProviderNetworkRequestKind.Credentials));
    }

    [Fact]
    public async Task BingTransportTimeout_TranslationPostIsNotReplayed()
    {
        var handler = new ScriptedHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateHtmlContent(CreateBingSessionHtml("IG", CreateCurrentBingKey(), "TOKEN")) }),
            (_, _) => throw new TaskCanceledException("Connect timeout", new TimeoutException()));
        using var client = new HttpClient(handler);
        using var provider = new BingWebTranslatorProvider(client);
        var failure = await Assert.ThrowsAsync<TranslatorProviderException>(() => provider.TranslateAsync(CreateRequest("BingWeb", "https://example.test")));
        Assert.Equal(TranslatorProviderFailureKind.Timeout, failure.FailureKind);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task BingTransportTimeout_RealHandlerConnectCancellationIsClassifiedAndBounded()
    {
        var connects = 0;
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromMilliseconds(50),
            ConnectCallback = async (_, token) =>
            {
                Interlocked.Increment(ref connects);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("Connection must time out without opening a socket.");
            },
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var provider = new BingWebTranslatorProvider(client);
        var failure = await Assert.ThrowsAsync<TranslatorProviderException>(() =>
            provider.TranslateAsync(CreateRequest("BingWeb", "http://example.test")).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(TranslatorProviderFailureKind.Timeout, failure.FailureKind);
        Assert.NotNull(failure.RetryAfter);
        Assert.Equal(2, connects);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BingSharedCredentials_CallerCancellationDoesNotAbortBootstrap(bool cancelAll)
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken networkToken = default;
        var handler = new ScriptedHttpMessageHandler(
            async (_, token) =>
            {
                networkToken = token;
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateHtmlContent(CreateBingSessionHtml("ABCDEF123456", "12345", "TOKENVALUE")) };
            },
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent("""[{"translations":[{"text":"Hello","to":"ru"}]}]""") }));
        using var provider = new BingWebTranslatorProvider(new HttpClient(handler));
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var firstDiagnostics = new TranslationProviderRequestDiagnostics(new[] { "Hello" }, DateTimeOffset.UtcNow);
        var secondDiagnostics = new TranslationProviderRequestDiagnostics(new[] { "Hello" }, DateTimeOffset.UtcNow);
        var first = provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com", diagnostics: firstDiagnostics), firstCancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com", diagnostics: secondDiagnostics), secondCancellation.Token);
        Assert.Equal(TranslationProviderWaitStage.Credentials, secondDiagnostics.CreateSnapshot().WaitStage);
        Assert.Empty(secondDiagnostics.CreateSnapshot().NetworkAttempts); // Waiting is not an extra HTTP request.
        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(networkToken.IsCancellationRequested);
        Assert.Equal(1, handler.RequestCount);
        if (cancelAll)
        {
            secondCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
            Assert.False(networkToken.IsCancellationRequested);
        }
        release.TrySetResult(true);
        if (cancelAll)
            second = provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com", diagnostics: secondDiagnostics));
        var response = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(response);
        Assert.Equal(2, handler.RequestCount); // one shared GET and one translation POST
        Assert.Empty(firstDiagnostics.CreateSnapshot().NetworkAttempts);
        Assert.Equal(2, secondDiagnostics.CreateSnapshot().NetworkAttempts.Count);
        Assert.All(secondDiagnostics.CreateSnapshot().NetworkAttempts,
            attempt => Assert.Equal(TranslationProviderNetworkRequestOutcome.Succeeded, attempt.Outcome));
    }

    [Fact]
    public async Task BingSharedCredentials_DisposeCancelsBootstrap()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ScriptedHttpMessageHandler(async (_, token) =>
        {
            entered.TrySetResult(true);
            return await WaitForCancellationAsync(token);
        });
        var provider = new BingWebTranslatorProvider(new HttpClient(handler));
        var pending = provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        provider.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        provider.Dispose();
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task BingSharedCredentials_FailedBootstrapCanBeReplaced()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ScriptedHttpMessageHandler(
            async (_, token) => { await release.Task.WaitAsync(token); return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = CreateHtmlContent("Unavailable") }; },
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateHtmlContent(CreateBingSessionHtml("ABCDEF123456", "12345", "TOKENVALUE")) }),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent("""[{"translations":[{"text":"Hello","to":"ru"}]}]""") }));
        using var provider = new BingWebTranslatorProvider(new HttpClient(handler));
        var first = provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com"));
        var second = provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com"));
        Assert.Equal(1, handler.RequestCount);
        release.TrySetResult(true);
        await Assert.ThrowsAsync<TranslatorProviderException>(() => first);
        await Assert.ThrowsAsync<TranslatorProviderException>(() => second);
        await provider.TranslateAsync(CreateRequest("BingWeb", "https://www.bing.com"));
        Assert.Equal(3, handler.RequestCount);
    }
    [Theory]
    [InlineData("timeout", TranslatorProviderFailureKind.Timeout)]
    [InlineData("429", TranslatorProviderFailureKind.Throttled)]
    [InlineData("captcha", TranslatorProviderFailureKind.UserVerificationRequired)]
    public async Task DiagnosticFaultWindow_SendsNothingUntilExpiryAndPreservesCancellation(string mode, TranslatorProviderFailureKind kind)
    {
        var clock = new DiagnosticClock();
        var inner = new DiagnosticInnerProvider();
        var provider = new DiagnosticWebTranslatorProvider(inner, mode, clock);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.TranslateAsync(CreateRequest("BingWeb", "https://example.test"), cancelled.Token));
        var failure = await Assert.ThrowsAsync<TranslatorProviderException>(() => provider.TranslateAsync(CreateRequest("BingWeb", "https://example.test")));
        Assert.Equal(kind, failure.FailureKind);
        Assert.Contains("[SIMULATED", failure.Message);
        Assert.Equal(0, inner.Calls);
        if (mode == "429")
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, failure.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(45), failure.RetryAfter);
            Assert.Equal(clock.GetUtcNow().AddSeconds(45), failure.NextRetryAt);
        }
        clock.Now = clock.Now.AddSeconds(44);
        await Assert.ThrowsAsync<TranslatorProviderException>(() => provider.TranslateAsync(CreateRequest("BingWeb", "https://example.test")));
        Assert.Equal(0, inner.Calls);
        clock.Now = clock.Now.AddSeconds(1);
        var result = await provider.TranslateAsync(CreateRequest("BingWeb", "https://example.test"));
        Assert.Same(inner.Response, result);
        Assert.Equal(1, inner.Calls);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(7200000)]
    public async Task BingPageSession_UsesIssuedLifetimeAndRefreshesIdentifiers(int lifetimeMs)
    {
        var clock = new DiagnosticClock();
        var key = clock.Now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var page = CreateBingSessionHtml("PAGE_IG", key, "TOKEN").Replace("3600000", lifetimeMs.ToString(CultureInfo.InvariantCulture));
        var nextPage = CreateBingSessionHtml("NEW_IG", (clock.Now.ToUnixTimeMilliseconds() + lifetimeMs).ToString(CultureInfo.InvariantCulture), "NEXT");
        var handler = new SequenceHttpMessageHandler(
            new(HttpStatusCode.OK) { Content = CreateHtmlContent(page) },
            new(HttpStatusCode.OK) { Content = CreateJsonContent("""[{"translations":[{"text":"ok"}]}]""") },
            new(HttpStatusCode.OK) { Content = CreateJsonContent("""[{"translations":[{"text":"ok"}]}]""") },
            new(HttpStatusCode.OK) { Content = CreateHtmlContent(nextPage) },
            new(HttpStatusCode.OK) { Content = CreateJsonContent("""[{"translations":[{"text":"ok"}]}]""") });
        using var client = new HttpClient(handler);
        using var provider = new BingWebTranslatorProvider(client, clock, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60));
        var request = CreateRequest("BingWeb", "https://example.test");
        await provider.TranslateAsync(request);
        clock.Now = clock.Now.AddMilliseconds(lifetimeMs / 2);
        await provider.TranslateAsync(request);
        Assert.Equal(3, handler.Requests.Count);
        clock.Now = clock.Now.AddMilliseconds(lifetimeMs / 2 + 1);
        await provider.TranslateAsync(request);
        Assert.Equal(5, handler.Requests.Count);
        Assert.Contains("IG=PAGE_IG&IID=translator.5028", handler.Requests[1].Uri!.Query);
        Assert.Contains("IG=NEW_IG&IID=translator.5028", handler.Requests[4].Uri!.Query);
    }

    [Theory]
    [InlineData("missing-ig")]
    [InlineData("missing-iid")]
    [InlineData("invalid-lifetime")]
    [InlineData("invalid-key")]
    public async Task BingPageSession_InvalidFieldsDoNotSendTranslationOrExposeToken(string scenario)
    {
        var page = CreateBingSessionHtml("PAGE_IG", CreateCurrentBingKey(), "PRIVATE_TOKEN");
        page = scenario switch
        {
            "missing-ig" => page.Replace("IG:", "Other:"),
            "missing-iid" => page.Replace("data-iid", "data-other"),
            "invalid-lifetime" => page.Replace("3600000", "-1"),
            _ => page,
        };
        if (scenario == "invalid-key")
            page = "<script>IG:\"PAGE_IG\";params_AbusePreventionHelper=[9999999999999999999999999999,\"PRIVATE_TOKEN\",1000]</script><div data-iid=\"translator.123\"></div>";
        var handler = new SequenceHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateHtmlContent(page) });
        using var client = new HttpClient(handler);
        using var provider = new BingWebTranslatorProvider(client);
        var failure = await Assert.ThrowsAsync<TranslatorProviderException>(() => provider.TranslateAsync(CreateRequest("BingWeb", "https://example.test")));
        Assert.Equal(TranslatorProviderFailureKind.UnsupportedResponse, failure.FailureKind);
        Assert.DoesNotContain("PRIVATE_TOKEN", failure.ToString());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task BingPageSession_RequestsHttp2WithFallbackAndSetsTranslationReferer()
    {
        var handler = new ScriptedHttpMessageHandler(
            (request, _) =>
            {
                Assert.Equal(HttpVersion.Version20, request.Version);
                Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, request.VersionPolicy);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateHtmlContent(CreateBingSessionHtml("PAGE", CreateCurrentBingKey(), "TOKEN")) });
            },
            (request, _) =>
            {
                Assert.Equal(new Uri("https://example.test/translator"), request.Headers.Referrer);
                Assert.Equal(HttpVersion.Version20, request.Version);
                Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, request.VersionPolicy);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent("""[{"translations":[{"text":"ok"}]}]""") });
            });
        using var client = new HttpClient(handler);
        using var provider = new BingWebTranslatorProvider(client);
        await provider.TranslateAsync(CreateRequest("BingWeb", "https://example.test"));
    }


    private sealed class DiagnosticClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class DiagnosticInnerProvider : ITranslatorProvider
    {
        public string ProviderId => "BingWeb";
        public int Calls { get; private set; }
        public TranslateResponse Response { get; } = new(new[] { "Real provider result" }, DateTimeOffset.UtcNow, "BingWeb");
        public Task<TranslateResponse> TranslateAsync(TranslateRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Response);
        }
    }
    [Theory]
    [InlineData("GoogleWeb", "<html><div class='smart-captcha'>secret</div></html>", TranslatorProviderFailureKind.UserVerificationRequired)]
    [InlineData("YandexWeb", "<html><div class='smart-captcha'>secret</div></html>", TranslatorProviderFailureKind.UserVerificationRequired)]
    [InlineData("GoogleWeb", "<html>secret maintenance</html>", TranslatorProviderFailureKind.UnsupportedResponse)]
    [InlineData("YandexWeb", "<html>secret maintenance</html>", TranslatorProviderFailureKind.UnsupportedResponse)]
    public async Task WebAvailability_Html_IsClassifiedWithoutLeakingBody(string id, string body, TranslatorProviderFailureKind expected)
    {
        var handler = new SequenceHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateHtmlContent(body) });
        ITranslatorProvider provider = id == "YandexWeb" ? new YandexWebTranslatorProvider(new HttpClient(handler))
            : new GoogleWebTranslatorProvider(new HttpClient(handler));
        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(() => provider.TranslateAsync(CreateRequest(id, "https://example.test")));
        Assert.Equal(expected, exception.FailureKind);
        Assert.DoesNotContain("secret", exception.ToString());
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, TranslatorProviderFailureKind.Dns)]
    [InlineData(HttpRequestError.ConnectionError, TranslatorProviderFailureKind.Connection)]
    [InlineData(HttpRequestError.SecureConnectionError, TranslatorProviderFailureKind.Tls)]
    [InlineData(HttpRequestError.Unknown, TranslatorProviderFailureKind.Network)]
    public void WebAvailability_NetworkFailures_AreSpecificAndRedacted(HttpRequestError error, TranslatorProviderFailureKind expected)
    {
        var exception = WebTranslatorAvailability.ClassifyNetworkFailure("YandexWeb", new HttpRequestException(error, "secret-url"));
        Assert.Equal(expected, exception.FailureKind);
        Assert.DoesNotContain("secret", exception.ToString());
    }

    [Theory]
    [InlineData("/showcaptcha?secret=1")]
    [InlineData("https://example.test/sorry/index?secret=1")]
    public void WebAvailability_ChallengeRedirect_IsRedacted(string location)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Found) { Content = CreateHtmlContent("") };
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        var failure = Assert.Throws<TranslatorProviderException>(() => WebTranslatorAvailability.ValidateResponse("GoogleWeb", response, ""));
        Assert.Equal(TranslatorProviderFailureKind.UserVerificationRequired, failure.FailureKind);
        Assert.DoesNotContain("secret", failure.ToString());
    }

    [Fact]
    public async Task WebAvailability_AfterChallenge_NextExplicitRequestCanRecover()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateHtmlContent("<html><div class='smart-captcha'></div></html>") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent("[[[\"Recovered\"]]]") });
        var provider = new GoogleWebTranslatorProvider(new HttpClient(handler));
        await Assert.ThrowsAsync<TranslatorProviderException>(() => provider.TranslateAsync(CreateRequest("GoogleWeb", "https://example.test")));
        Assert.Single(handler.Requests);
        var recovered = await provider.TranslateAsync(CreateRequest("GoogleWeb", "https://example.test"));
        Assert.Equal(new[] { "Recovered" }, recovered.TranslatedTexts);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void WebAvailability_JsonMentioningCaptcha_IsNotAChallenge()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = CreateJsonContent("{}") };
        WebTranslatorAvailability.ValidateResponse("YandexWeb", response, "{\"text\":[\"smart-captcha /showcaptcha\"]}");
    }

    [Fact]
    public async Task WebAvailability_TimeoutAndUserCancellation_AreDifferent()
    {
        var handler = new ScriptedHttpMessageHandler((_, _) => throw new TaskCanceledException("secret"));
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");
        var failure = await Assert.ThrowsAsync<TranslatorProviderException>(() => WebTranslatorAvailability.SendAsync(client, request, "GoogleWeb", CancellationToken.None));
        Assert.Equal(TranslatorProviderFailureKind.Timeout, failure.FailureKind);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var second = new HttpRequestMessage(HttpMethod.Get, "https://example.test");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WebTranslatorAvailability.SendAsync(client, second, "GoogleWeb", cancelled.Token));
    }

    [Fact]
    public void WebAvailability_Throttle_PreservesRetryAfter()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = CreateHtmlContent("secret") };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
        var failure = Assert.Throws<TranslatorProviderException>(() => WebTranslatorAvailability.ValidateResponse("YandexWeb", response, "secret"));
        Assert.Equal(TranslatorProviderFailureKind.Throttled, failure.FailureKind);
        Assert.Equal(TimeSpan.FromSeconds(90), failure.RetryAfter);
        Assert.DoesNotContain("secret", failure.ToString());
    }

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
        Assert.Contains("IID=translator.5028", handler.Requests[1].Uri?.Query, StringComparison.Ordinal);
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
