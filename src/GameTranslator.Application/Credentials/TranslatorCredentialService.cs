using GameTranslator.Application.Translation;

namespace GameTranslator.Application.Credentials;

public sealed class TranslatorCredentialService
{
    private static readonly string[] CredentiallessProviders =
    {
        "GoogleWeb",
        "BingWeb",
        "YandexWeb",
    };

    private readonly ICredentialStorage storage;

    public TranslatorCredentialService(ICredentialStorage storage)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task SaveAsync(
        string provider,
        string accessToken,
        string projectId,
        string location,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        ThrowIfRemovedProvider(provider);
        var record = CreateRecord(provider, accessToken, projectId, location, endpoint);

        try
        {
            await storage.SaveAsync(record, cancellationToken);
        }
        catch (CredentialStorageException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CredentialStorageException(
                $"Translator credentials for provider '{record.Provider}' could not be saved: {Redact(exception.Message, record.AccessToken)}");
        }
    }

    public async Task<TranslatorCredentialRecord?> ReadAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        ThrowIfRemovedProvider(provider);
        var normalizedProvider = NormalizeProvider(provider);

        try
        {
            return await storage.ReadAsync(normalizedProvider, cancellationToken);
        }
        catch (CredentialStorageException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CredentialStorageException(
                $"Translator credentials for provider '{normalizedProvider}' could not be read.",
                exception);
        }
    }

    public async Task<bool> ValidateStoredAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        if (!RequiresStoredCredentials(provider))
        {
            return true;
        }

        var record = await ReadAsync(provider, cancellationToken);

        return record is not null
            && string.Equals(record.Provider, NormalizeProvider(provider), StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(record.AccessToken)
            && !string.IsNullOrWhiteSpace(record.ProjectId)
            && !string.IsNullOrWhiteSpace(record.Location)
            && record.Endpoint.IsAbsoluteUri;
    }

    public async Task<TranslatorCredentials> CreateCredentialsAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProvider(provider);

        if (!RequiresStoredCredentials(normalizedProvider))
        {
            return CreateCredentiallessCredentials(normalizedProvider);
        }

        var record = await ReadAsync(normalizedProvider, cancellationToken);

        if (record is null)
        {
            throw new CredentialStorageException(
                $"Translator credentials for provider '{normalizedProvider}' are not stored.");
        }

        return record.ToTranslatorCredentials();
    }

    public async Task DeleteAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProvider(provider);

        if (!RequiresStoredCredentials(normalizedProvider))
        {
            return;
        }

        try
        {
            await storage.DeleteAsync(normalizedProvider, cancellationToken);
        }
        catch (CredentialStorageException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CredentialStorageException(
                $"Translator credentials for provider '{normalizedProvider}' could not be deleted.",
                exception);
        }
    }

    public static string GetDefaultEndpoint(string provider)
    {
        ThrowIfRemovedProvider(provider);

        return NormalizeProvider(provider) switch
        {
            "Azure" => "https://api.cognitive.microsofttranslator.com",
            "Yandex" => "https://translate.api.cloud.yandex.net",
            "GoogleWeb" => "https://translate.googleapis.com",
            "BingWeb" => "https://www.bing.com",
            "YandexWeb" => "https://translate.yandex.net",
            _ => "https://translation.googleapis.com",
        };
    }

    public static bool RequiresStoredCredentials(string provider)
    {
        ThrowIfRemovedProvider(provider);
        return !CredentiallessProviders.Contains(NormalizeProvider(provider), StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsRemovedProvider(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        var normalized = provider.Trim().ToLowerInvariant();
        return normalized is "webauto" or "web-auto" or "web auto" or "glhf";
    }

    public static string NormalizeProvider(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        var normalized = provider.Trim();

        return normalized.ToLowerInvariant() switch
        {
            "google" => "Google",
            "azure" => "Azure",
            "yandex" => "Yandex",
            "googleweb" or "google-web" or "google web" => "GoogleWeb",
            "bingweb" or "bing-web" or "bing web" => "BingWeb",
            "yandexweb" or "yandex-web" or "yandex web" => "YandexWeb",
            _ => normalized,
        };
    }

    private static TranslatorCredentialRecord CreateRecord(
        string provider,
        string accessToken,
        string projectId,
        string location,
        string endpoint)
    {
        ThrowIfRemovedProvider(provider);
        var normalizedProvider = NormalizeProvider(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        var normalizedLocation = string.IsNullOrWhiteSpace(location)
            ? "global"
            : location.Trim();
        var normalizedEndpoint = string.IsNullOrWhiteSpace(endpoint)
            ? GetDefaultEndpoint(normalizedProvider)
            : endpoint.Trim();

        if (!Uri.TryCreate(normalizedEndpoint, UriKind.Absolute, out var endpointUri))
        {
            throw new ArgumentException("Translator credential endpoint must be an absolute URI.", nameof(endpoint));
        }

        return new TranslatorCredentialRecord(
            normalizedProvider,
            accessToken,
            projectId,
            normalizedLocation,
            endpointUri);
    }

    private static TranslatorCredentials CreateCredentiallessCredentials(string provider)
    {
        return new TranslatorCredentials(
            "experimental-web-provider",
            provider,
            "global",
            new Uri(GetDefaultEndpoint(provider)));
    }

    private static string Redact(string value, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return value;
        }

        return value.Replace(secret.Trim(), "<redacted>", StringComparison.Ordinal);
    }

    private static void ThrowIfRemovedProvider(string provider)
    {
        if (!IsRemovedProvider(provider))
        {
            return;
        }

        var removedProvider = string.Equals(provider.Trim(), "glhf", StringComparison.OrdinalIgnoreCase)
            ? "glhf"
            : "WebAuto";
        throw new NotSupportedException(
            $"Translator provider '{removedProvider}' is no longer supported. Choose another translator provider.");
    }
}
