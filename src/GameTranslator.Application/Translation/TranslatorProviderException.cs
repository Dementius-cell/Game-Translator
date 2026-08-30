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
        Exception? innerException = null,
        TimeSpan? retryAfter = null,
        int consecutiveFailureCount = 0)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (retryAfter.HasValue && retryAfter.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryAfter));
        }

        if (consecutiveFailureCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(consecutiveFailureCount));
        }

        ProviderId = providerId.Trim();
        FailureKind = Enum.IsDefined(failureKind)
            ? failureKind
            : TranslatorProviderFailureKind.Unknown;
        RetryAfter = retryAfter;
        ConsecutiveFailureCount = consecutiveFailureCount;
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
        Exception? innerException = null,
        TimeSpan? retryAfter = null,
        int consecutiveFailureCount = 0)
        : this(
            providerId,
            failureKind,
            message,
            innerException,
            retryAfter,
            consecutiveFailureCount)
    {
        StatusCode = statusCode;
    }

    public string ProviderId { get; }

    public TranslatorProviderFailureKind FailureKind { get; }

    public HttpStatusCode? StatusCode { get; }

    public TimeSpan? RetryAfter { get; }

    public int ConsecutiveFailureCount { get; }

    private static TranslatorProviderFailureKind ClassifyStatusCode(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.TooManyRequests => TranslatorProviderFailureKind.Throttled,
            _ => TranslatorProviderFailureKind.Http,
        };
    }
}
