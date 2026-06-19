namespace GameTranslator.Application.Cache;

public sealed class TranslationCacheEntry
{
    public TranslationCacheEntry(
        TranslationCacheKey key,
        string translatedText,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset lastAccessedAt,
        long hitCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(translatedText);

        if (expiresAt <= createdAt)
        {
            throw new ArgumentException("Translation cache entry expiration must be after creation.", nameof(expiresAt));
        }

        if (hitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hitCount), "Translation cache hit count must not be negative.");
        }

        Key = key ?? throw new ArgumentNullException(nameof(key));
        TranslatedText = translatedText.Trim();
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        LastAccessedAt = lastAccessedAt;
        HitCount = hitCount;
    }

    public TranslationCacheKey Key { get; }

    public string TranslatedText { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset LastAccessedAt { get; }

    public long HitCount { get; }

    public bool IsExpired(DateTimeOffset now)
    {
        return ExpiresAt <= now;
    }

    public TranslationCacheEntry MarkAccessed(DateTimeOffset accessedAt)
    {
        return new TranslationCacheEntry(
            Key,
            TranslatedText,
            CreatedAt,
            ExpiresAt,
            accessedAt,
            checked(HitCount + 1));
    }
}
