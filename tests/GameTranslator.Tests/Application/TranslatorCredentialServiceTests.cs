using GameTranslator.Application.Credentials;

namespace GameTranslator.Tests.Application;

public sealed class TranslatorCredentialServiceTests
{
    [Fact]
    public async Task SaveValidateCreateAndDeleteCredentials_UsesCredentialStorage()
    {
        var storage = new TestCredentialStorage();
        var service = new TranslatorCredentialService(storage);

        await service.SaveAsync(
            " google ",
            "SECRET_ACCESS_TOKEN",
            "project-a",
            "us-central1",
            "https://translation.test");

        var storedRecord = await storage.ReadAsync("Google");
        var credentials = await service.CreateCredentialsAsync("Google");
        var isValid = await service.ValidateStoredAsync("Google");

        Assert.NotNull(storedRecord);
        Assert.True(isValid);
        Assert.Equal("Google", storedRecord.Provider);
        Assert.Equal("SECRET_ACCESS_TOKEN", storedRecord.AccessToken);
        Assert.Equal("project-a", credentials.ProjectId);
        Assert.Equal("us-central1", credentials.Location);
        Assert.Equal(new Uri("https://translation.test"), credentials.Endpoint);

        await service.DeleteAsync("Google");

        Assert.False(await service.ValidateStoredAsync("Google"));
    }

    [Fact]
    public async Task SaveAsync_WhenSecretIsMissing_ThrowsArgumentException()
    {
        var service = new TranslatorCredentialService(new TestCredentialStorage());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SaveAsync("Google", string.Empty, "project-a", "global", "https://translation.test"));
    }

    [Fact]
    public async Task CreateCredentialsAsync_WhenCredentialsAreMissing_ThrowsCredentialStorageException()
    {
        var service = new TranslatorCredentialService(new TestCredentialStorage());

        var exception = await Assert.ThrowsAsync<CredentialStorageException>(
            () => service.CreateCredentialsAsync("Google"));

        Assert.DoesNotContain("SECRET", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GoogleWeb", "https://translate.googleapis.com/")]
    [InlineData("BingWeb", "https://www.bing.com/")]
    [InlineData("YandexWeb", "https://translate.yandex.net/")]
    public async Task CreateCredentialsAsync_ForExperimentalWebProvider_DoesNotReadStoredSecrets(
        string provider,
        string endpoint)
    {
        var storage = new TestCredentialStorage();
        var service = new TranslatorCredentialService(storage);

        var isValid = await service.ValidateStoredAsync(provider);
        var credentials = await service.CreateCredentialsAsync(provider);

        Assert.True(isValid);
        Assert.Equal("experimental-web-provider", credentials.AccessToken);
        Assert.Equal(provider, credentials.ProjectId);
        Assert.Equal("global", credentials.Location);
        Assert.Equal(new Uri(endpoint), credentials.Endpoint);
        Assert.Equal(0, storage.ReadCount);
    }

    [Theory]
    [InlineData("WebAuto")]
    [InlineData("web-auto")]
    [InlineData("glhf")]
    public async Task CreateCredentialsAsync_ForRemovedProvider_ThrowsWithoutReadingStoredSecrets(string provider)
    {
        var storage = new TestCredentialStorage();
        var service = new TranslatorCredentialService(storage);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => service.CreateCredentialsAsync(provider));

        Assert.Contains("no longer supported", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, storage.ReadCount);
    }

    [Fact]
    public async Task SaveAsync_WhenStorageFails_RedactsSecretFromExceptionMessage()
    {
        var service = new TranslatorCredentialService(
            new TestCredentialStorage
            {
                SaveException = new InvalidOperationException("Provider rejected SECRET_ACCESS_TOKEN."),
            });

        var exception = await Assert.ThrowsAsync<CredentialStorageException>(
            () => service.SaveAsync(
                "Google",
                "SECRET_ACCESS_TOKEN",
                "project-a",
                "global",
                "https://translation.test"));

        Assert.DoesNotContain("SECRET_ACCESS_TOKEN", exception.Message, StringComparison.Ordinal);
        Assert.Contains("<redacted>", exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void TranslatorCredentialRecord_ToString_RedactsSecret()
    {
        var record = new TranslatorCredentialRecord(
            "Google",
            "SECRET_ACCESS_TOKEN",
            "project-a",
            "global",
            new Uri("https://translation.test"));

        var text = record.ToString();

        Assert.DoesNotContain("SECRET_ACCESS_TOKEN", text, StringComparison.Ordinal);
        Assert.Contains("<redacted>", text, StringComparison.Ordinal);
        Assert.Contains("project-a", text, StringComparison.Ordinal);
    }

    private sealed class TestCredentialStorage : ICredentialStorage
    {
        private readonly Dictionary<string, TranslatorCredentialRecord> records = new(StringComparer.OrdinalIgnoreCase);

        public Exception? SaveException { get; init; }

        public int ReadCount { get; private set; }

        public Task SaveAsync(
            TranslatorCredentialRecord credential,
            CancellationToken cancellationToken = default)
        {
            if (SaveException is not null)
            {
                return Task.FromException(SaveException);
            }

            records[credential.Provider] = credential;

            return Task.CompletedTask;
        }

        public Task<TranslatorCredentialRecord?> ReadAsync(
            string provider,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            records.TryGetValue(provider, out var credential);

            return Task.FromResult(credential);
        }

        public Task DeleteAsync(string provider, CancellationToken cancellationToken = default)
        {
            records.Remove(provider);

            return Task.CompletedTask;
        }
    }
}
