namespace GameTranslator.Application.Credentials;

public interface ICredentialStorage
{
    Task SaveAsync(TranslatorCredentialRecord credential, CancellationToken cancellationToken = default);

    Task<TranslatorCredentialRecord?> ReadAsync(string provider, CancellationToken cancellationToken = default);

    Task DeleteAsync(string provider, CancellationToken cancellationToken = default);
}
