namespace GameTranslator.Application.Credentials;

public sealed class CredentialStorageException : Exception
{
    public CredentialStorageException(string message)
        : base(message)
    {
    }

    public CredentialStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
