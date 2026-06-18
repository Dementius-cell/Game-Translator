using GameTranslator.Application.Translation;

namespace GameTranslator.Application.Credentials;

public sealed class TranslatorCredentialRecord
{
    public TranslatorCredentialRecord(
        string provider,
        string accessToken,
        string projectId,
        string location,
        Uri endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Credential endpoint must be an absolute URI.", nameof(endpoint));
        }

        Provider = provider.Trim();
        AccessToken = accessToken.Trim();
        ProjectId = projectId.Trim();
        Location = location.Trim();
        Endpoint = endpoint;
    }

    public string Provider { get; }

    public string AccessToken { get; }

    public string ProjectId { get; }

    public string Location { get; }

    public Uri Endpoint { get; }

    public TranslatorCredentials ToTranslatorCredentials()
    {
        return new TranslatorCredentials(
            AccessToken,
            ProjectId,
            Location,
            Endpoint);
    }

    public override string ToString()
    {
        return $"{nameof(TranslatorCredentialRecord)} {{ Provider = {Provider}, AccessToken = <redacted>, ProjectId = {ProjectId}, Location = {Location}, Endpoint = {Endpoint} }}";
    }
}
