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
        string diagnosticMessage = "")
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

        TranslatedTexts = translatedTexts;
        TranslatedAt = translatedAt;
        MemoryHitCount = memoryHitCount;
        PersistentHitCount = persistentHitCount;
        MissCount = missCount;
        StoredCount = storedCount;
        ProviderId = providerId?.Trim() ?? string.Empty;
        DiagnosticMessage = diagnosticMessage?.Trim() ?? string.Empty;
    }

    public IReadOnlyList<string> TranslatedTexts { get; }

    public DateTimeOffset TranslatedAt { get; }

    public int MemoryHitCount { get; }

    public int PersistentHitCount { get; }

    public int HitCount => checked(MemoryHitCount + PersistentHitCount);

    public int MissCount { get; }

    public int StoredCount { get; }

    public string ProviderId { get; }

    public string DiagnosticMessage { get; }

    public TranslateResponse ToTranslateResponse()
    {
        return new TranslateResponse(TranslatedTexts, TranslatedAt, ProviderId, DiagnosticMessage);
    }
}
