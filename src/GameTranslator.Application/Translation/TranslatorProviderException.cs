using System.Net;

namespace GameTranslator.Application.Translation;

public sealed class TranslatorProviderException : Exception
{
    public TranslatorProviderException(string providerId, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        ProviderId = providerId.Trim();
    }

    public TranslatorProviderException(
        string providerId,
        HttpStatusCode statusCode,
        string message,
        Exception? innerException = null)
        : this(providerId, message, innerException)
    {
        StatusCode = statusCode;
    }

    public string ProviderId { get; }

    public HttpStatusCode? StatusCode { get; }
}
