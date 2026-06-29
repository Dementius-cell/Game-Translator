using GameTranslator.Application.Translation;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Cache;

public sealed class TranslationCacheService
{
    private readonly ITranslationCacheRepository repository;
    private readonly TranslationCacheOptions options;
    private readonly Dictionary<TranslationCacheKey, TranslationCacheEntry> memoryEntries = new();

    public TranslationCacheService(
        ITranslationCacheRepository repository,
        TranslationCacheOptions options)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<TranslationCacheResult> GetOrAddAsync(
        TranslatorSettings settings,
        IReadOnlyList<string> sourceTexts,
        Func<IReadOnlyList<string>, Task<TranslateResponse>> translateMissingAsync,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sourceTexts);
        ArgumentNullException.ThrowIfNull(translateMissingAsync);
        cancellationToken.ThrowIfCancellationRequested();

        if (sourceTexts.Count == 0)
        {
            throw new ArgumentException("Translation cache requires at least one source text.", nameof(sourceTexts));
        }

        var translatedTexts = new string[sourceTexts.Count];
        var misses = new List<CacheMiss>();
        var memoryHits = 0;
        var persistentHits = 0;

        for (var index = 0; index < sourceTexts.Count; index++)
        {
            var key = CreateKey(settings, sourceTexts[index]);
            if (TryGetMemoryEntry(key, now, out var memoryEntry))
            {
                translatedTexts[index] = memoryEntry.TranslatedText;
                memoryHits++;
                continue;
            }

            var persistentEntry = await repository.GetAsync(key, now, cancellationToken);
            if (persistentEntry is not null)
            {
                memoryEntries[key] = persistentEntry;
                translatedTexts[index] = persistentEntry.TranslatedText;
                persistentHits++;
                continue;
            }

            misses.Add(new CacheMiss(index, key, key.SourceText));
        }

        var storedCount = 0;
        var translatedAt = now;
        var providerId = settings.Provider;
        var diagnosticMessage = string.Empty;
        if (misses.Count > 0)
        {
            var response = await translateMissingAsync(misses.Select(miss => miss.SourceText).ToArray());
            if (response.TranslatedTexts.Count != misses.Count)
            {
                throw new InvalidOperationException("Translated miss count must match cache miss count.");
            }

            translatedAt = response.TranslatedAt;
            providerId = string.IsNullOrWhiteSpace(response.ProviderId)
                ? settings.Provider
                : response.ProviderId;
            diagnosticMessage = response.DiagnosticMessage;
            for (var index = 0; index < misses.Count; index++)
            {
                var miss = misses[index];
                var translatedText = response.TranslatedTexts[index];
                translatedTexts[miss.Index] = translatedText;

                var entry = new TranslationCacheEntry(
                    miss.Key,
                    translatedText,
                    translatedAt,
                    translatedAt.Add(options.TimeToLive),
                    translatedAt,
                    hitCount: 0);
                await repository.SaveAsync(entry, cancellationToken);
                memoryEntries[miss.Key] = entry;
                storedCount++;
            }
        }

        return new TranslationCacheResult(
            translatedTexts,
            translatedAt,
            memoryHits,
            persistentHits,
            misses.Count,
            storedCount,
            providerId,
            diagnosticMessage);
    }

    public async Task<TranslationCacheCleanupResult> CleanupExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var expiredMemoryKeys = memoryEntries
            .Where(pair => pair.Value.IsExpired(now))
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in expiredMemoryKeys)
        {
            memoryEntries.Remove(key);
        }

        var persistentCount = await repository.DeleteExpiredAsync(now, cancellationToken);

        return new TranslationCacheCleanupResult(expiredMemoryKeys.Length, persistentCount);
    }

    private bool TryGetMemoryEntry(
        TranslationCacheKey key,
        DateTimeOffset now,
        out TranslationCacheEntry entry)
    {
        if (!memoryEntries.TryGetValue(key, out var candidate))
        {
            entry = null!;
            return false;
        }

        if (candidate.IsExpired(now))
        {
            memoryEntries.Remove(key);
            entry = null!;
            return false;
        }

        entry = candidate.MarkAccessed(now);
        memoryEntries[key] = entry;
        return true;
    }

    private static TranslationCacheKey CreateKey(TranslatorSettings settings, string sourceText)
    {
        return new TranslationCacheKey(
            settings.Provider,
            settings.SourceLanguage,
            settings.TargetLanguage,
            sourceText);
    }

    private sealed record CacheMiss(int Index, TranslationCacheKey Key, string SourceText);
}
