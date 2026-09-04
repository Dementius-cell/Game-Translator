using System.Net;

namespace GameTranslator.Application.Translation;

/// <summary>
/// Thread-safe, local-only diagnostics for one provider invocation. It records bounded request
/// input and actual HTTP attempts, but never response bodies, credentials, or access tokens.
/// </summary>
public sealed class TranslationProviderRequestDiagnostics
{
    private const int MaximumTextEntries = 16;
    private const int MaximumTextLength = 512;
    private readonly object syncRoot = new();
    private readonly List<MutableNetworkAttempt> networkAttempts = new();
    private DateTimeOffset? providerInvocationStartedAt;
    private DateTimeOffset? providerInvocationCompletedAt;
    private string? providerId;
    private TranslationProviderInvocationOutcome outcome = TranslationProviderInvocationOutcome.Pending;
    private TranslatorProviderFailureKind? failureKind;

    public TranslationProviderRequestDiagnostics(
        IEnumerable<string> inputTexts,
        DateTimeOffset queuedAt,
        string? requestId = null)
    {
        ArgumentNullException.ThrowIfNull(inputTexts);
        var materializedTexts = inputTexts.ToArray();
        if (materializedTexts.Length == 0 || materializedTexts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Provider request diagnostics require at least one non-empty input text.",
                nameof(inputTexts));
        }

        RequestId = string.IsNullOrWhiteSpace(requestId)
            ? Guid.NewGuid().ToString("N")
            : requestId.Trim();
        QueuedAt = queuedAt;
        InputTextCount = materializedTexts.Length;
        InputTexts = materializedTexts.Take(MaximumTextEntries).Select(BoundText).ToArray();
    }

    public string RequestId { get; }

    public DateTimeOffset QueuedAt { get; }

    public int InputTextCount { get; }

    public IReadOnlyList<string> InputTexts { get; }

    public void MarkProviderInvocationStarted(string providerId, DateTimeOffset startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        lock (syncRoot)
        {
            this.providerId ??= providerId.Trim();
            providerInvocationStartedAt ??= startedAt;
        }
    }

    public string MarkNetworkRequestStarted(
        TranslationProviderNetworkRequestKind kind,
        DateTimeOffset startedAt)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        lock (syncRoot)
        {
            var attemptId = $"{RequestId}:{networkAttempts.Count + 1}";
            networkAttempts.Add(new MutableNetworkAttempt(attemptId, kind, startedAt));
            return attemptId;
        }
    }

    public void MarkNetworkRequestCompleted(
        string attemptId,
        TranslationProviderNetworkRequestOutcome attemptOutcome,
        DateTimeOffset completedAt,
        HttpStatusCode? statusCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        if (!Enum.IsDefined(attemptOutcome) || attemptOutcome == TranslationProviderNetworkRequestOutcome.Pending)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptOutcome));
        }

        lock (syncRoot)
        {
            var attempt = networkAttempts.SingleOrDefault(candidate =>
                string.Equals(candidate.AttemptId, attemptId, StringComparison.Ordinal));
            if (attempt is null || attempt.Outcome != TranslationProviderNetworkRequestOutcome.Pending)
            {
                return;
            }

            attempt.CompletedAt = completedAt;
            attempt.Outcome = attemptOutcome;
            attempt.StatusCode = statusCode;
        }
    }

    public void MarkProviderInvocationCompleted(
        TranslationProviderInvocationOutcome invocationOutcome,
        DateTimeOffset completedAt,
        TranslatorProviderFailureKind? providerFailureKind = null)
    {
        if (!Enum.IsDefined(invocationOutcome) || invocationOutcome == TranslationProviderInvocationOutcome.Pending)
        {
            throw new ArgumentOutOfRangeException(nameof(invocationOutcome));
        }

        lock (syncRoot)
        {
            if (outcome != TranslationProviderInvocationOutcome.Pending)
            {
                return;
            }

            providerInvocationCompletedAt = completedAt;
            outcome = invocationOutcome;
            failureKind = providerFailureKind;
        }
    }

    public TranslationProviderRequestDiagnosticsSnapshot CreateSnapshot()
    {
        lock (syncRoot)
        {
            return new TranslationProviderRequestDiagnosticsSnapshot(
                RequestId,
                QueuedAt,
                providerInvocationStartedAt,
                providerInvocationCompletedAt,
                providerId,
                outcome,
                failureKind,
                InputTextCount,
                InputTexts.ToArray(),
                networkAttempts.Select(attempt => new TranslationProviderNetworkAttempt(
                    attempt.AttemptId,
                    attempt.Kind,
                    WasSent: true,
                    attempt.StartedAt,
                    attempt.CompletedAt,
                    attempt.Outcome,
                    attempt.StatusCode)).ToArray());
        }
    }

    private static string BoundText(string text)
    {
        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        if (normalized.Length <= MaximumTextLength)
        {
            return normalized;
        }

        var boundedLength = MaximumTextLength;
        if (char.IsHighSurrogate(normalized[boundedLength - 1]))
        {
            boundedLength--;
        }

        return normalized[..boundedLength];
    }

    private sealed class MutableNetworkAttempt(
        string attemptId,
        TranslationProviderNetworkRequestKind kind,
        DateTimeOffset startedAt)
    {
        public string AttemptId { get; } = attemptId;
        public TranslationProviderNetworkRequestKind Kind { get; } = kind;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public DateTimeOffset? CompletedAt { get; set; }
        public TranslationProviderNetworkRequestOutcome Outcome { get; set; } =
            TranslationProviderNetworkRequestOutcome.Pending;
        public HttpStatusCode? StatusCode { get; set; }
    }
}

public sealed record TranslationProviderRequestDiagnosticsSnapshot(
    string RequestId,
    DateTimeOffset QueuedAt,
    DateTimeOffset? ProviderInvocationStartedAt,
    DateTimeOffset? ProviderInvocationCompletedAt,
    string? ProviderId,
    TranslationProviderInvocationOutcome Outcome,
    TranslatorProviderFailureKind? FailureKind,
    int InputTextCount,
    IReadOnlyList<string> InputTexts,
    IReadOnlyList<TranslationProviderNetworkAttempt> NetworkAttempts)
{
    public bool WasNetworkRequestSent => NetworkAttempts.Any(attempt => attempt.WasSent);

    public TimeSpan? QueueDuration => ProviderInvocationStartedAt is { } startedAt
        ? startedAt >= QueuedAt ? startedAt - QueuedAt : TimeSpan.Zero
        : null;
}

public sealed record TranslationProviderNetworkAttempt(
    string AttemptId,
    TranslationProviderNetworkRequestKind Kind,
    bool WasSent,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    TranslationProviderNetworkRequestOutcome Outcome,
    HttpStatusCode? StatusCode);

public enum TranslationProviderInvocationOutcome
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3,
    RejectedBeforeSend = 4,
}

public enum TranslationProviderNetworkRequestKind
{
    Credentials = 0,
    Translation = 1,
}

public enum TranslationProviderNetworkRequestOutcome
{
    Pending = 0,
    Succeeded = 1,
    HttpError = 2,
    Timeout = 3,
    Cancelled = 4,
    Failed = 5,
}
