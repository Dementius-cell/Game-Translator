using System.Net;
using System.Net.Http;
using System.Text;
using GameTranslator.Application.Translation;
using GameTranslator.Infrastructure.Translation;

namespace GameTranslator.Tests.Infrastructure;

public sealed class YandexTranslatorProviderTests
{
    [Fact]
    public async Task TranslateAsync_WhenYandexReturnsTranslations_ReturnsTranslatedTexts()
    {
        var handler = new CapturingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent(
                    """
                    {
                      "translations": [
                        { "text": "Привет" },
                        { "text": "Мир" }
                      ]
                    }
                    """),
            });
        var provider = new YandexTranslatorProvider(new HttpClient(handler));

        var response = await provider.TranslateAsync(CreateRequest("Api-Key SECRET_ACCESS_TOKEN"));

        Assert.Equal(new[] { "Привет", "Мир" }, response.TranslatedTexts);
        Assert.Equal(HttpMethod.Post, handler.CapturedMethod);
        Assert.Equal(
            "https://yandex.test/translate/v2/translate",
            handler.CapturedRequestUri?.ToString());
        Assert.Equal("Api-Key", handler.CapturedAuthorizationScheme);
        Assert.Equal("SECRET_ACCESS_TOKEN", handler.CapturedAuthorizationParameter);
        Assert.Contains("\"folderId\":\"folder-a\"", handler.CapturedContent, StringComparison.Ordinal);
        Assert.Contains("\"sourceLanguageCode\":\"en\"", handler.CapturedContent, StringComparison.Ordinal);
        Assert.Contains("\"targetLanguageCode\":\"ru\"", handler.CapturedContent, StringComparison.Ordinal);
        Assert.Contains("\"format\":\"PLAIN_TEXT\"", handler.CapturedContent, StringComparison.Ordinal);
        Assert.Contains("\"texts\":[\"Hello\",\"World\"]", handler.CapturedContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_WhenYandexReturnsError_ThrowsProviderExceptionWithoutSecret()
    {
        var handler = new CapturingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = CreateJsonContent(
                    """
                    {
                      "code": 16,
                      "message": "Token SECRET_ACCESS_TOKEN is invalid"
                    }
                    """),
            });
        var provider = new YandexTranslatorProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest("Api-Key SECRET_ACCESS_TOKEN")));

        Assert.Equal("Yandex", exception.ProviderId);
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Contains("HTTP 401", exception.Message, StringComparison.Ordinal);
        Assert.Contains("<redacted>", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_ACCESS_TOKEN", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_ACCESS_TOKEN", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_WhenResponseCountDoesNotMatchRequest_ThrowsProviderException()
    {
        var handler = new CapturingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent(
                    """
                    {
                      "translations": [
                        { "text": "Привет" }
                      ]
                    }
                    """),
            });
        var provider = new YandexTranslatorProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest()));

        Assert.Equal("Yandex", exception.ProviderId);
        Assert.Contains("expected translated text items", exception.Message, StringComparison.Ordinal);
    }

    private static TranslateRequest CreateRequest(string accessToken = "iam-token")
    {
        return new TranslateRequest(
            new[] { "Hello", "World" },
            "en",
            "ru",
            new TranslatorCredentials(
                accessToken,
                "folder-a",
                endpoint: new Uri("https://yandex.test")));
    }

    private static StringContent CreateJsonContent(string json)
    {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage response;

        public CapturingHttpMessageHandler(HttpResponseMessage response)
        {
            this.response = response;
        }

        public HttpMethod? CapturedMethod { get; private set; }

        public Uri? CapturedRequestUri { get; private set; }

        public string? CapturedAuthorizationScheme { get; private set; }

        public string? CapturedAuthorizationParameter { get; private set; }

        public string CapturedContent { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CapturedMethod = request.Method;
            CapturedRequestUri = request.RequestUri;
            CapturedAuthorizationScheme = request.Headers.Authorization?.Scheme;
            CapturedAuthorizationParameter = request.Headers.Authorization?.Parameter;
            CapturedContent = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return response;
        }
    }
}
