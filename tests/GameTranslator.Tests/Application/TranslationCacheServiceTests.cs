using GameTranslator.Application.Cache;
using GameTranslator.Application.Translation;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class TranslationCacheServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetOrAddAsync_WhenCacheMiss_TranslatesAndStoresEntry()
    {
        var repository = new InMemoryTranslationCacheRepository();
        var service = CreateService(repository);
        var factoryCallCount = 0;

        var result = await service.GetOrAddAsync(
            CreateSettings(),
            new[] { "Hello" },
            texts =>
            {
                factoryCallCount++;
                return Task.FromResult(new TranslateResponse(new[] { "Привет" }, Now.AddSeconds(1)));
            },
            Now);

        Assert.Equal(new[] { "Привет" }, result.TranslatedTexts);
        Assert.Equal(0, result.HitCount);
        Assert.Equal(1, result.MissCount);
        Assert.Equal(1, result.StoredCount);
        Assert.Equal(1, factoryCallCount);
        Assert.Single(repository.Entries);
        Assert.Equal(TimeSpan.FromDays(30), repository.Entries.Single().Value.ExpiresAt - repository.Entries.Single().Value.CreatedAt);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenMemoryCacheHasEntry_ServesHitWithoutRepositoryOrTranslator()
    {
        var repository = new InMemoryTranslationCacheRepository();
        var service = CreateService(repository);
        await service.GetOrAddAsync(
            CreateSettings(),
            new[] { "Hello" },
            _ => Task.FromResult(new TranslateResponse(new[] { "Привет" }, Now)),
            Now);
        repository.GetCallCount = 0;

        var result = await service.GetOrAddAsync(
            CreateSettings(),
            new[] { "Hello" },
            _ => throw new InvalidOperationException("Translator should not be called."),
            Now.AddMinutes(1));

        Assert.Equal(new[] { "Привет" }, result.TranslatedTexts);
        Assert.Equal(1, result.MemoryHitCount);
        Assert.Equal(0, result.PersistentHitCount);
        Assert.Equal(0, result.MissCount);
        Assert.Equal(0, repository.GetCallCount);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenOcrWhitespaceChanges_ServesHitWithoutTranslator()
    {
        var repository = new InMemoryTranslationCacheRepository();
        var service = CreateService(repository);
        var factoryCallCount = 0;
        IReadOnlyList<string>? translatedTexts = null;
        await service.GetOrAddAsync(
            CreateSettings(),
            new[] { "你 好" },
            texts =>
            {
                factoryCallCount++;
                translatedTexts = texts.ToArray();
                return Task.FromResult(new TranslateResponse(new[] { "Hello" }, Now));
            },
            Now);

        var result = await service.GetOrAddAsync(
            CreateSettings(),
            new[] { "你好" },
            _ => throw new InvalidOperationException("Translator should not be called."),
            Now.AddMilliseconds(250));

        Assert.Equal(new[] { "Hello" }, result.TranslatedTexts);
        Assert.Equal(1, result.MemoryHitCount);
        Assert.Equal(0, result.MissCount);
        Assert.Equal(1, factoryCallCount);
        Assert.Equal(new[] { "你好" }, translatedTexts);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenPersistentCacheHasEntry_ServesHitWithoutTranslator()
    {
        var repository = new InMemoryTranslationCacheRepository();
        var key = new TranslationCacheKey("Google", "en", "ru", "Hello");
        repository.Entries[key] = new TranslationCacheEntry(
            key,
            "Привет",
            Now,
            Now.AddDays(30),
            Now,
            hitCount: 0);
        var service = CreateService(repository);

        var result = await service.GetOrAddAsync(
            CreateSettings(),
            new[] { "Hello" },
            _ => throw new InvalidOperationException("Translator should not be called."),
            Now.AddMinutes(1));

        Assert.Equal(new[] { "Привет" }, result.TranslatedTexts);
        Assert.Equal(0, result.MemoryHitCount);
        Assert.Equal(1, result.PersistentHitCount);
        Assert.Equal(0, result.MissCount);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenEntryIsExpired_TranslatesAndReplacesEntry()
    {
        var repository = new InMemoryTranslationCacheRepository();
        var key = new TranslationCacheKey("Google", "en", "ru", "Hello");
        repository.Entries[key] = new TranslationCacheEntry(
            key,
            "Old",
            Now.AddDays(-31),
            Now.AddDays(-1),
            Now.AddDays(-2),
            hitCount: 0);
        var service = CreateService(repository);

        var result = await service.GetOrAddAsync(
            CreateSettings(),
            new[] { "Hello" },
            _ => Task.FromResult(new TranslateResponse(new[] { "Fresh" }, Now)),
            Now);

        Assert.Equal(new[] { "Fresh" }, result.TranslatedTexts);
        Assert.Equal(0, result.HitCount);
        Assert.Equal(1, result.MissCount);
        Assert.Equal("Fresh", repository.Entries[key].TranslatedText);
    }

    [Fact]
    public async Task CleanupExpiredAsync_RemovesExpiredMemoryAndPersistentEntries()
    {
        var repository = new InMemoryTranslationCacheRepository();
        var service = new TranslationCacheService(
            repository,
            new TranslationCacheOptions(TimeSpan.FromMinutes(5)));
        await service.GetOrAddAsync(
            CreateSettings(),
            new[] { "Hello" },
            _ => Task.FromResult(new TranslateResponse(new[] { "Привет" }, Now)),
            Now);

        var result = await service.CleanupExpiredAsync(Now.AddMinutes(10));

        Assert.Equal(1, result.MemoryEntryCount);
        Assert.Equal(1, result.PersistentEntryCount);
        Assert.Empty(repository.Entries);
    }

    private static TranslationCacheService CreateService(InMemoryTranslationCacheRepository repository)
    {
        return new TranslationCacheService(repository, new TranslationCacheOptions());
    }

    private static TranslatorSettings CreateSettings()
    {
        return new TranslatorSettings
        {
            Provider = "Google",
            SourceLanguage = "en",
            TargetLanguage = "ru",
        };
    }

    private sealed class InMemoryTranslationCacheRepository : ITranslationCacheRepository
    {
        public Dictionary<TranslationCacheKey, TranslationCacheEntry> Entries { get; } = new();

        public int GetCallCount { get; set; }

        public Task<TranslationCacheEntry?> GetAsync(
            TranslationCacheKey key,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            GetCallCount++;
            Entries.TryGetValue(key, out var entry);

            return Task.FromResult(entry?.IsExpired(now) == true ? null : entry?.MarkAccessed(now));
        }

        public Task SaveAsync(
            TranslationCacheEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries[entry.Key] = entry;
            return Task.CompletedTask;
        }

        public Task<int> DeleteExpiredAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var expiredKeys = Entries
                .Where(pair => pair.Value.IsExpired(now))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var key in expiredKeys)
            {
                Entries.Remove(key);
            }

            return Task.FromResult(expiredKeys.Length);
        }
    }
}
