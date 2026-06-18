using System.Net;
using System.Net.Http;
using System.Text;
using GameTranslator.Application.Translation;
using GameTranslator.Infrastructure.Translation;

namespace GameTranslator.Tests.Infrastructure;

public sealed class GoogleTranslatorProviderTests
{
    [Fact]
    public async Task TranslateAsync_WhenGoogleReturnsTranslations_ReturnsTranslatedTexts()
    {
        var handler = new CapturingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent(
                    """
                    {
                      "translations": [
                        { "translatedText": "Привет" },
                        { "translatedText": "Мир" }
                      ]
                    }
                    """),
            });
        var provider = new GoogleTranslatorProvider(new HttpClient(handler));
        var request = CreateRequest("SECRET_ACCESS_TOKEN");

        var response = await provider.TranslateAsync(request);

        Assert.Equal(new[] { "Привет", "Мир" }, response.TranslatedTexts);
        Assert.Equal(HttpMethod.Post, handler.CapturedMethod);
        Assert.Equal(
            "https://translation.test/v3/projects/project-a/locations/global:translateText",
            handler.CapturedRequestUri?.ToString());
        Assert.Equal("Bearer", handler.CapturedAuthorizationScheme);
        Assert.Equal("SECRET_ACCESS_TOKEN", handler.CapturedAuthorizationParameter);
        Assert.Equal("project-a", handler.CapturedUserProject);
        Assert.Contains("\"sourceLanguageCode\":\"en\"", handler.CapturedContent, StringComparison.Ordinal);
        Assert.Contains("\"targetLanguageCode\":\"ru\"", handler.CapturedContent, StringComparison.Ordinal);
        Assert.Contains("\"contents\":[\"Hello\",\"World\"]", handler.CapturedContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_WhenGoogleReturnsError_ThrowsProviderExceptionWithoutSecret()
    {
        var handler = new CapturingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = CreateJsonContent(
                    """
                    {
                      "error": {
                        "message": "Token SECRET_ACCESS_TOKEN is not authorized"
                      }
                    }
                    """),
            });
        var provider = new GoogleTranslatorProvider(new HttpClient(handler));
        var request = CreateRequest("SECRET_ACCESS_TOKEN");

        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(request));

        Assert.Equal("Google", exception.ProviderId);
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("HTTP 403", exception.Message, StringComparison.Ordinal);
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
                        { "translatedText": "Привет" }
                      ]
                    }
                    """),
            });
        var provider = new GoogleTranslatorProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest()));

        Assert.Equal("Google", exception.ProviderId);
        Assert.Contains("expected translated text items", exception.Message, StringComparison.Ordinal);
    }

    private static TranslateRequest CreateRequest(string accessToken = "access-token")
    {
        return new TranslateRequest(
            new[] { "Hello", "World" },
            "en",
            "ru",
            new TranslatorCredentials(
                accessToken,
                "project-a",
                endpoint: new Uri("https://translation.test")));
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

        public string? CapturedUserProject { get; private set; }

        public string CapturedContent { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CapturedMethod = request.Method;
            CapturedRequestUri = request.RequestUri;
            CapturedAuthorizationScheme = request.Headers.Authorization?.Scheme;
            CapturedAuthorizationParameter = request.Headers.Authorization?.Parameter;
            CapturedUserProject = request.Headers.TryGetValues("x-goog-user-project", out var values)
                ? values.Single()
                : null;
            CapturedContent = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return response;
        }
    }
}
