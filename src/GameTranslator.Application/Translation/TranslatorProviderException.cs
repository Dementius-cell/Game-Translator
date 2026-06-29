using System.Net;

namespace GameTranslator.Application.Translation;

public sealed class TranslatorProviderException : Exception
{
    public TranslatorProviderException(string providerId, string message, Exception? innerException = null)
        : this(providerId, TranslatorProviderFailureKind.Unknown, message, innerException)
    {
    }

    public TranslatorProviderException(
        string providerId,
        TranslatorProviderFailureKind failureKind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        ProviderId = providerId.Trim();
        FailureKind = Enum.IsDefined(failureKind)
            ? failureKind
            : TranslatorProviderFailureKind.Unknown;
    }

    public TranslatorProviderException(
        string providerId,
        HttpStatusCode statusCode,
        string message,
        Exception? innerException = null)
        : this(providerId, statusCode, ClassifyStatusCode(statusCode), message, innerException)
    {
    }

    public TranslatorProviderException(
        string providerId,
        HttpStatusCode statusCode,
        TranslatorProviderFailureKind failureKind,
        string message,
        Exception? innerException = null)
        : this(providerId, failureKind, message, innerException)
    {
        StatusCode = statusCode;
    }

    public string ProviderId { get; }

    public TranslatorProviderFailureKind FailureKind { get; }

    public HttpStatusCode? StatusCode { get; }

    private static TranslatorProviderFailureKind ClassifyStatusCode(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.TooManyRequests => TranslatorProviderFailureKind.Throttled,
            _ => TranslatorProviderFailureKind.Http,
        };
    }
}
