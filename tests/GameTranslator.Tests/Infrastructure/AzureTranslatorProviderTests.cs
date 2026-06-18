using System.Net;
using System.Net.Http;
using System.Text;
using GameTranslator.Application.Translation;
using GameTranslator.Infrastructure.Translation;

namespace GameTranslator.Tests.Infrastructure;

public sealed class AzureTranslatorProviderTests
{
    [Fact]
    public async Task TranslateAsync_WhenAzureReturnsTranslations_ReturnsTranslatedTexts()
    {
        var handler = new CapturingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent(
                    """
                    [
                      { "translations": [ { "text": "Привет", "to": "ru" } ] },
                      { "translations": [ { "text": "Мир", "to": "ru" } ] }
                    ]
                    """),
            });
        var provider = new AzureTranslatorProvider(new HttpClient(handler));

        var response = await provider.TranslateAsync(CreateRequest("SECRET_ACCESS_TOKEN"));

        Assert.Equal(new[] { "Привет", "Мир" }, response.TranslatedTexts);
        Assert.Equal(HttpMethod.Post, handler.CapturedMethod);
        Assert.Equal(
            "https://azure.test/translate?api-version=3.0&from=en&to=ru",
            handler.CapturedRequestUri?.ToString());
        Assert.Equal("SECRET_ACCESS_TOKEN", handler.CapturedSubscriptionKey);
        Assert.Equal("westeurope", handler.CapturedRegion);
        Assert.Contains("\"text\":\"Hello\"", handler.CapturedContent, StringComparison.Ordinal);
        Assert.Contains("\"text\":\"World\"", handler.CapturedContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_WhenAzureReturnsError_ThrowsProviderExceptionWithoutSecret()
    {
        var handler = new CapturingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = CreateJsonContent(
                    """
                    {
                      "error": {
                        "message": "Key SECRET_ACCESS_TOKEN is invalid"
                      }
                    }
                    """),
            });
        var provider = new AzureTranslatorProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest("SECRET_ACCESS_TOKEN")));

        Assert.Equal("Azure", exception.ProviderId);
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
                    [
                      { "translations": [ { "text": "Привет", "to": "ru" } ] }
                    ]
                    """),
            });
        var provider = new AzureTranslatorProvider(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => provider.TranslateAsync(CreateRequest()));

        Assert.Equal("Azure", exception.ProviderId);
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
                "azure-resource",
                "westeurope",
                new Uri("https://azure.test")));
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

        public string? CapturedSubscriptionKey { get; private set; }

        public string? CapturedRegion { get; private set; }

        public string CapturedContent { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CapturedMethod = request.Method;
            CapturedRequestUri = request.RequestUri;
            CapturedSubscriptionKey = request.Headers.TryGetValues("Ocp-Apim-Subscription-Key", out var keyValues)
                ? keyValues.Single()
                : null;
            CapturedRegion = request.Headers.TryGetValues("Ocp-Apim-Subscription-Region", out var regionValues)
                ? regionValues.Single()
                : null;
            CapturedContent = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return response;
        }
    }
}
