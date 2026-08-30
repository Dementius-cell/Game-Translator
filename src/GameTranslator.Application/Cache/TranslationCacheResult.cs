using GameTranslator.Application.Translation;

namespace GameTranslator.Application.Cache;

public sealed class TranslationCacheResult
{
    public TranslationCacheResult(
        IReadOnlyList<string> translatedTexts,
        DateTimeOffset translatedAt,
        int memoryHitCount,
        int persistentHitCount,
        int missCount,
        int storedCount,
        string providerId = "",
        string diagnosticMessage = "",
        DateTimeOffset? providerRequestStartedAt = null,
        DateTimeOffset? providerRequestCompletedAt = null,
        int sanitizedTranslationCount = 0)
    {
        ArgumentNullException.ThrowIfNull(translatedTexts);

        if (translatedTexts.Count == 0)
        {
            throw new ArgumentException("Translation cache result must contain at least one translated text.", nameof(translatedTexts));
        }

        if (memoryHitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryHitCount), "Memory hit count must not be negative.");
        }

        if (persistentHitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(persistentHitCount), "Persistent hit count must not be negative.");
        }

        if (missCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(missCount), "Miss count must not be negative.");
        }

        if (storedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(storedCount), "Stored count must not be negative.");
        }

        if (sanitizedTranslationCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sanitizedTranslationCount),
                "Sanitized translation count must not be negative.");
        }

        if (providerRequestStartedAt is null != providerRequestCompletedAt is null)
        {
            throw new ArgumentException(
                "Provider request timestamps must either both be present or both be absent.");
        }

        if (providerRequestStartedAt is { } startedAt
            && providerRequestCompletedAt is { } completedAt
            && completedAt < startedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(providerRequestCompletedAt),
                "Provider request completion cannot precede its start.");
        }

        TranslatedTexts = translatedTexts;
        TranslatedAt = translatedAt;
        MemoryHitCount = memoryHitCount;
        PersistentHitCount = persistentHitCount;
        MissCount = missCount;
        StoredCount = storedCount;
        ProviderId = providerId?.Trim() ?? string.Empty;
        DiagnosticMessage = diagnosticMessage?.Trim() ?? string.Empty;
        ProviderRequestStartedAt = providerRequestStartedAt;
        ProviderRequestCompletedAt = providerRequestCompletedAt;
        SanitizedTranslationCount = sanitizedTranslationCount;
    }

    public IReadOnlyList<string> TranslatedTexts { get; }

    public DateTimeOffset TranslatedAt { get; }

    public int MemoryHitCount { get; }

    public int PersistentHitCount { get; }

    public int HitCount => checked(MemoryHitCount + PersistentHitCount);

    public int MissCount { get; }

    public int StoredCount { get; }

    public int SanitizedTranslationCount { get; }

    public string ProviderId { get; }

    public string DiagnosticMessage { get; }

    public DateTimeOffset? ProviderRequestStartedAt { get; }

    public DateTimeOffset? ProviderRequestCompletedAt { get; }

    public bool ProviderRequestIssued => ProviderRequestStartedAt is not null;

    public TranslateResponse ToTranslateResponse()
    {
        return new TranslateResponse(TranslatedTexts, TranslatedAt, ProviderId, DiagnosticMessage);
    }
}
