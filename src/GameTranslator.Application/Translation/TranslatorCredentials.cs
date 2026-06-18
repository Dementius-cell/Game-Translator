namespace GameTranslator.Application.Translation;

public sealed class TranslatorCredentials
{
    public TranslatorCredentials(
        string accessToken,
        string projectId,
        string location = "global",
        Uri? endpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);

        AccessToken = accessToken.Trim();
        ProjectId = projectId.Trim();
        Location = location.Trim();
        Endpoint = endpoint ?? new Uri("https://translation.googleapis.com");
    }

    public string AccessToken { get; }

    public string ProjectId { get; }

    public string Location { get; }

    public Uri Endpoint { get; }

    public override string ToString()
    {
        return $"{nameof(TranslatorCredentials)} {{ AccessToken = <redacted>, ProjectId = {ProjectId}, Location = {Location}, Endpoint = {Endpoint} }}";
    }
}
