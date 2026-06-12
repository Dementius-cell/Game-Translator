namespace GameTranslator.Application.Profiles;

public sealed class ProfileImportException : InvalidOperationException
{
    public ProfileImportException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
