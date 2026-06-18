using GameTranslator.Application.Translation;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class TranslatorManagerTests
{
    [Fact]
    public async Task TranslateAsync_SelectsProviderConfiguredInTranslatorSettings()
    {
        var google = new TestTranslatorProvider("Google", "google-result");
        var azure = new TestTranslatorProvider("Azure", "azure-result");
        var manager = new TranslatorManager(new ITranslatorProvider[] { google, azure });
        var settings = new TranslatorSettings
        {
            Provider = "azure",
            SourceLanguage = "en",
            TargetLanguage = "ru",
        };

        var response = await manager.TranslateAsync(
            settings,
            new[] { "Hello" },
            CreateCredentials());

        Assert.Equal(new[] { "azure-result" }, response.TranslatedTexts);
        Assert.False(google.WasCalled);
        Assert.True(azure.WasCalled);
        Assert.Equal("en", azure.Request?.SourceLanguage);
        Assert.Equal("ru", azure.Request?.TargetLanguage);
    }

    [Fact]
    public async Task TranslateAsync_WhenProviderThrowsUnexpectedException_RedactsSecret()
    {
        var provider = new TestTranslatorProvider(
            "Yandex",
            failure: new InvalidOperationException("Token SECRET_ACCESS_TOKEN failed"));
        var manager = new TranslatorManager(new[] { provider });
        var settings = new TranslatorSettings
        {
            Provider = "Yandex",
            SourceLanguage = "en",
            TargetLanguage = "ru",
        };

        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => manager.TranslateAsync(
                settings,
                new[] { "Hello" },
                CreateCredentials("Api-Key SECRET_ACCESS_TOKEN")));

        Assert.Equal("Yandex", exception.ProviderId);
        Assert.Contains("<redacted>", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_ACCESS_TOKEN", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_ACCESS_TOKEN", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_WhenProviderIsNotRegistered_ThrowsProviderException()
    {
        var manager = new TranslatorManager(new ITranslatorProvider[] { new TestTranslatorProvider("Google", "ok") });
        var settings = new TranslatorSettings
        {
            Provider = "Azure",
            SourceLanguage = "en",
            TargetLanguage = "ru",
        };

        var exception = await Assert.ThrowsAsync<TranslatorProviderException>(
            () => manager.TranslateAsync(settings, new[] { "Hello" }, CreateCredentials()));

        Assert.Equal("Azure", exception.ProviderId);
        Assert.Contains("not registered", exception.Message, StringComparison.Ordinal);
    }

    private static TranslatorCredentials CreateCredentials(string accessToken = "access-token")
    {
        return new TranslatorCredentials(
            accessToken,
            "project-a",
            endpoint: new Uri("https://translation.test"));
    }

    private sealed class TestTranslatorProvider : ITranslatorProvider
    {
        private readonly string? translatedText;
        private readonly Exception? failure;

        public TestTranslatorProvider(string providerId, string? translatedText = null, Exception? failure = null)
        {
            ProviderId = providerId;
            this.translatedText = translatedText;
            this.failure = failure;
        }

        public string ProviderId { get; }

        public bool WasCalled { get; private set; }

        public TranslateRequest? Request { get; private set; }

        public Task<TranslateResponse> TranslateAsync(
            TranslateRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WasCalled = true;
            Request = request;

            if (failure is not null)
            {
                return Task.FromException<TranslateResponse>(failure);
            }

            return Task.FromResult(
                new TranslateResponse(
                    new[] { translatedText ?? request.Texts.Single() },
                    DateTimeOffset.UtcNow));
        }
    }
}
