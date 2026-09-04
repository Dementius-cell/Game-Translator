using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Translation;

public sealed class TranslatorManager
{
    private readonly IReadOnlyDictionary<string, ITranslatorProvider> providers;

    public TranslatorManager(IEnumerable<ITranslatorProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        this.providers = providers.ToDictionary(
            provider => provider.ProviderId,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<TranslateResponse> TranslateAsync(
        TranslatorSettings settings,
        IEnumerable<string> texts,
        TranslatorCredentials credentials,
        CancellationToken cancellationToken = default,
        TranslationProviderRequestDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(credentials);

        var provider = GetProvider(settings.Provider);
        var request = new TranslateRequest(
            texts,
            settings.SourceLanguage,
            settings.TargetLanguage,
            credentials,
            diagnostics);

        try
        {
            return await provider.TranslateAsync(request, cancellationToken);
        }
        catch (TranslatorProviderException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TranslatorProviderException(
                provider.ProviderId,
                TranslatorProviderFailureKind.Unexpected,
                $"Translator provider '{provider.ProviderId}' failed: {RedactSecret(exception.Message, credentials.AccessToken)}");
        }
    }

    public ITranslatorProvider GetProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        if (providers.TryGetValue(providerId.Trim(), out var provider))
        {
            return provider;
        }

        throw new TranslatorProviderException(
            providerId,
            TranslatorProviderFailureKind.Configuration,
            $"Translator provider '{providerId.Trim()}' is not registered.");
    }

    private static string RedactSecret(string value, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return value;
        }

        var redacted = value.Replace(secret.Trim(), "<redacted>", StringComparison.Ordinal);
        var separatorIndex = secret.Trim().IndexOf(' ', StringComparison.Ordinal);
        if (separatorIndex > 0 && separatorIndex < secret.Trim().Length - 1)
        {
            redacted = redacted.Replace(
                secret.Trim()[(separatorIndex + 1)..].Trim(),
                "<redacted>",
                StringComparison.Ordinal);
        }

        return redacted;
    }
}
