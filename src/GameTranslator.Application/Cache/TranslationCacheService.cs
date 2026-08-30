using System.Collections.Concurrent;
using GameTranslator.Application.Translation;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Cache;

public sealed class TranslationCacheService
{
    private readonly ITranslationCacheRepository repository;
    private readonly TranslationCacheOptions options;
    private readonly Dictionary<TranslationCacheKey, TranslationCacheEntry> memoryEntries = new();
    private readonly ConcurrentDictionary<TranslationCacheKey, SemaphoreSlim> keyLocks = new();
    private readonly object memoryEntriesLock = new();

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

        var orderedKeys = sourceTexts
            .Select(sourceText => CreateKey(settings, sourceText))
            .Distinct()
            .OrderBy(CreateLockOrderKey, StringComparer.Ordinal)
            .ToArray();
        var acquiredLocks = new List<SemaphoreSlim>(orderedKeys.Length);
        try
        {
            foreach (var key in orderedKeys)
            {
                var keyLock = keyLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
                await keyLock.WaitAsync(cancellationToken);
                acquiredLocks.Add(keyLock);
            }

            var translatedTexts = new string[sourceTexts.Count];
            var misses = new List<CacheMiss>();
            var memoryHits = 0;
            var persistentHits = 0;
            var sanitizedTranslationCount = 0;

            for (var index = 0; index < sourceTexts.Count; index++)
            {
                var key = CreateKey(settings, sourceTexts[index]);
                if (TryGetMemoryEntry(key, now, out var memoryEntry))
                {
                    var sanitation = TranslationOutputSanitizer.Sanitize(
                        settings.Provider,
                        key.SourceText,
                        memoryEntry.TranslatedText);
                    translatedTexts[index] = sanitation.Text;
                    if (sanitation.WasSanitized)
                    {
                        StoreMemoryEntry(key, ReplaceTranslatedText(memoryEntry, sanitation.Text));
                        sanitizedTranslationCount++;
                    }

                    memoryHits++;
                    continue;
                }

                var persistentEntry = await repository.GetAsync(key, now, cancellationToken);
                if (persistentEntry is not null)
                {
                    var sanitation = TranslationOutputSanitizer.Sanitize(
                        settings.Provider,
                        key.SourceText,
                        persistentEntry.TranslatedText);
                    var cachedMemoryEntry = sanitation.WasSanitized
                        ? ReplaceTranslatedText(persistentEntry, sanitation.Text)
                        : persistentEntry;
                    StoreMemoryEntry(key, cachedMemoryEntry);
                    translatedTexts[index] = sanitation.Text;
                    if (sanitation.WasSanitized)
                    {
                        sanitizedTranslationCount++;
                    }

                    persistentHits++;
                    continue;
                }

                misses.Add(new CacheMiss(index, key, key.SourceText));
            }

            var storedCount = 0;
            var translatedAt = now;
            var providerId = settings.Provider;
            var diagnosticMessage = string.Empty;
            DateTimeOffset? providerRequestStartedAt = null;
            DateTimeOffset? providerRequestCompletedAt = null;
            if (misses.Count > 0)
            {
                providerRequestStartedAt = DateTimeOffset.UtcNow;
                var response = await translateMissingAsync(misses.Select(miss => miss.SourceText).ToArray());
                providerRequestCompletedAt = DateTimeOffset.UtcNow;
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
                    var sanitation = TranslationOutputSanitizer.Sanitize(
                        settings.Provider,
                        miss.SourceText,
                        response.TranslatedTexts[index]);
                    var translatedText = sanitation.Text;
                    translatedTexts[miss.Index] = translatedText;
                    if (sanitation.WasSanitized)
                    {
                        sanitizedTranslationCount++;
                    }

                    var entry = new TranslationCacheEntry(
                        miss.Key,
                        translatedText,
                        translatedAt,
                        translatedAt.Add(options.TimeToLive),
                        translatedAt,
                        hitCount: 0);
                    // A provider may finish after its caller was invalidated. Persisting that completed,
                    // text-keyed response prevents the replacement revision from issuing the same request.
                    await repository.SaveAsync(entry, CancellationToken.None);
                    StoreMemoryEntry(miss.Key, entry);
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
                diagnosticMessage,
                providerRequestStartedAt,
                providerRequestCompletedAt,
                sanitizedTranslationCount);
        }
        finally
        {
            for (var index = acquiredLocks.Count - 1; index >= 0; index--)
            {
                acquiredLocks[index].Release();
            }
        }
    }

    public async Task<TranslationCacheCleanupResult> CleanupExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TranslationCacheKey[] expiredMemoryKeys;
        lock (memoryEntriesLock)
        {
            expiredMemoryKeys = memoryEntries
                .Where(pair => pair.Value.IsExpired(now))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var key in expiredMemoryKeys)
            {
                memoryEntries.Remove(key);
            }
        }

        var persistentCount = await repository.DeleteExpiredAsync(now, cancellationToken);

        return new TranslationCacheCleanupResult(expiredMemoryKeys.Length, persistentCount);
    }

    private bool TryGetMemoryEntry(
        TranslationCacheKey key,
        DateTimeOffset now,
        out TranslationCacheEntry entry)
    {
        lock (memoryEntriesLock)
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
    }

    private void StoreMemoryEntry(TranslationCacheKey key, TranslationCacheEntry entry)
    {
        lock (memoryEntriesLock)
        {
            memoryEntries[key] = entry;
        }
    }

    private static TranslationCacheKey CreateKey(TranslatorSettings settings, string sourceText)
    {
        return new TranslationCacheKey(
            settings.Provider,
            settings.SourceLanguage,
            settings.TargetLanguage,
            sourceText);
    }

    private static string CreateLockOrderKey(TranslationCacheKey key)
    {
        return string.Join(
            "\u001f",
            key.Provider.ToUpperInvariant(),
            key.SourceLanguage.ToUpperInvariant(),
            key.TargetLanguage.ToUpperInvariant(),
            key.SourceTextHash,
            key.SourceText);
    }

    private static TranslationCacheEntry ReplaceTranslatedText(
        TranslationCacheEntry entry,
        string translatedText)
    {
        return new TranslationCacheEntry(
            entry.Key,
            translatedText,
            entry.CreatedAt,
            entry.ExpiresAt,
            entry.LastAccessedAt,
            entry.HitCount);
    }

    private sealed record CacheMiss(int Index, TranslationCacheKey Key, string SourceText);
}
