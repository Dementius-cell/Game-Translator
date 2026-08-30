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
    public async Task GetOrAddAsync_WhenYandexPersistentValueRepeatsNonRepeatingSource_CollapsesExactRepetition()
    {
        const string sourceText = "\u3053\u306e\u30b2\u30fc\u30e0\u304c\u5927\u597d\u304d";
        const string repeatedTranslation = "I love this game. I love this game. I love this game. I love this game. I love this game.";
        var repository = new InMemoryTranslationCacheRepository();
        var key = new TranslationCacheKey("YandexWeb", "ja", "ru", sourceText);
        repository.Entries[key] = new TranslationCacheEntry(
            key,
            repeatedTranslation,
            Now,
            Now.AddDays(30),
            Now,
            hitCount: 0);
        var service = CreateService(repository);

        var result = await service.GetOrAddAsync(
            CreateSettings("YandexWeb", "ja", "ru"),
            new[] { sourceText },
            _ => throw new InvalidOperationException("Translator should not be called."),
            Now.AddMinutes(1));

        Assert.Equal(new[] { "I love this game." }, result.TranslatedTexts);
        Assert.Equal(1, result.SanitizedTranslationCount);
        Assert.Equal(1, result.PersistentHitCount);
        Assert.Equal(0, result.MissCount);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenYandexMissReturnsExactRepetition_StoresCollapsedValue()
    {
        const string sourceText = "\u3053\u306e\u30b2\u30fc\u30e0\u304c\u5927\u597d\u304d";
        const string repeatedTranslation = "I love this game. I love this game. I love this game. I love this game. I love this game.";
        var repository = new InMemoryTranslationCacheRepository();
        var service = CreateService(repository);

        var result = await service.GetOrAddAsync(
            CreateSettings("YandexWeb", "ja", "ru"),
            new[] { sourceText },
            _ => Task.FromResult(new TranslateResponse(new[] { repeatedTranslation }, Now)),
            Now);

        Assert.Equal(new[] { "I love this game." }, result.TranslatedTexts);
        Assert.Equal(1, result.SanitizedTranslationCount);
        Assert.Equal("I love this game.", Assert.Single(repository.Entries).Value.TranslatedText);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenYandexMissReturnsDominantRepeatedWordRun_StoresCollapsedValue()
    {
        const string sourceText = "\u3053\u306e\u30b2\u30fc\u30e0\u304c\u5927\u597d\u304d";
        const string repeatedTranslation = "Prefix again again again again again again again again again.";
        var repository = new InMemoryTranslationCacheRepository();
        var service = CreateService(repository);

        var result = await service.GetOrAddAsync(
            CreateSettings("YandexWeb", "ja", "ru"),
            new[] { sourceText },
            _ => Task.FromResult(new TranslateResponse(new[] { repeatedTranslation }, Now)),
            Now);

        Assert.Equal(new[] { "Prefix again." }, result.TranslatedTexts);
        Assert.Equal(1, result.SanitizedTranslationCount);
        Assert.Equal("Prefix again.", Assert.Single(repository.Entries).Value.TranslatedText);
    }

    [Theory]
    [InlineData("Again again again again again", "Again")]
    [InlineData("Go now. Go now. Go now. Go now. Go now.", "Go now.")]
    public async Task GetOrAddAsync_WhenYandexRepeatsShortUnitFiveTimes_CollapsesExactRepetition(
        string repeatedTranslation,
        string expectedTranslation)
    {
        var repository = new InMemoryTranslationCacheRepository();
        var service = CreateService(repository);

        var result = await service.GetOrAddAsync(
            CreateSettings("YandexWeb", "ja", "ru"),
            new[] { "\u3053\u306e\u30b2\u30fc\u30e0\u304c\u5927\u597d\u304d" },
            _ => Task.FromResult(new TranslateResponse(new[] { repeatedTranslation }, Now)),
            Now);

        Assert.Equal(new[] { expectedTranslation }, result.TranslatedTexts);
        Assert.Equal(1, result.SanitizedTranslationCount);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenYandexRepeatsOnlyFourTimes_PreservesTranslation()
    {
        const string repeatedTranslation = "Again again again again";
        var repository = new InMemoryTranslationCacheRepository();
        var service = CreateService(repository);

        var result = await service.GetOrAddAsync(
            CreateSettings("YandexWeb", "ja", "ru"),
            new[] { "\u3053\u306e\u30b2\u30fc\u30e0\u304c\u5927\u597d\u304d" },
            _ => Task.FromResult(new TranslateResponse(new[] { repeatedTranslation }, Now)),
            Now);

        Assert.Equal(new[] { repeatedTranslation }, result.TranslatedTexts);
        Assert.Equal(0, result.SanitizedTranslationCount);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenYandexRepeatedWordRunHasTwoOtherWords_PreservesTranslation()
    {
        const string repeatedTranslation = "Prefix again again again again again suffix";
        var repository = new InMemoryTranslationCacheRepository();
        var service = CreateService(repository);

        var result = await service.GetOrAddAsync(
            CreateSettings("YandexWeb", "ja", "ru"),
            new[] { "\u3053\u306e\u30b2\u30fc\u30e0\u304c\u5927\u597d\u304d" },
            _ => Task.FromResult(new TranslateResponse(new[] { repeatedTranslation }, Now)),
            Now);

        Assert.Equal(new[] { repeatedTranslation }, result.TranslatedTexts);
        Assert.Equal(0, result.SanitizedTranslationCount);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenYandexSourceAndTranslationBothRepeat_PreservesTranslation()
    {
        const string repeatedSource = "\u597d\u304d\u597d\u304d\u597d\u304d\u597d\u304d\u597d\u304d";
        const string repeatedTranslation = "I like it. I like it. I like it. I like it. I like it.";
        var repository = new InMemoryTranslationCacheRepository();
        var service = CreateService(repository);

        var result = await service.GetOrAddAsync(
            CreateSettings("YandexWeb", "ja", "ru"),
            new[] { repeatedSource },
            _ => Task.FromResult(new TranslateResponse(new[] { repeatedTranslation }, Now)),
            Now);

        Assert.Equal(new[] { repeatedTranslation }, result.TranslatedTexts);
        Assert.Equal(0, result.SanitizedTranslationCount);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenNonYandexProviderReturnsExactRepetition_PreservesTranslation()
    {
        const string repeatedTranslation = "I love this game. I love this game. I love this game. I love this game. I love this game.";
        var repository = new InMemoryTranslationCacheRepository();
        var service = CreateService(repository);

        var result = await service.GetOrAddAsync(
            CreateSettings("Google", "ja", "ru"),
            new[] { "\u3053\u306e\u30b2\u30fc\u30e0\u304c\u5927\u597d\u304d" },
            _ => Task.FromResult(new TranslateResponse(new[] { repeatedTranslation }, Now)),
            Now);

        Assert.Equal(new[] { repeatedTranslation }, result.TranslatedTexts);
        Assert.Equal(0, result.SanitizedTranslationCount);
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

    [Fact]
    public async Task GetOrAddAsync_WhenIdenticalMissesOverlap_CoalescesTheProviderRequest()
    {
        var repository = new InMemoryTranslationCacheRepository();
        var service = CreateService(repository);
        var providerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerCallCount = 0;

        async Task<TranslateResponse> TranslateAsync(IReadOnlyList<string> texts)
        {
            Interlocked.Increment(ref providerCallCount);
            providerStarted.TrySetResult(true);
            await releaseProvider.Task;
            return new TranslateResponse(new[] { "Translated" }, Now.AddSeconds(1));
        }

        var first = service.GetOrAddAsync(CreateSettings(), new[] { "Same text" }, TranslateAsync, Now);
        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var second = service.GetOrAddAsync(CreateSettings(), new[] { "Same text" }, TranslateAsync, Now);

        Assert.Equal(1, Volatile.Read(ref providerCallCount));
        releaseProvider.TrySetResult(true);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, providerCallCount);
        Assert.Equal(1, results[0].MissCount);
        Assert.True(results[0].ProviderRequestIssued);
        Assert.Equal(1, results[1].MemoryHitCount);
        Assert.Equal(0, results[1].MissCount);
        Assert.False(results[1].ProviderRequestIssued);
    }

    private static TranslationCacheService CreateService(InMemoryTranslationCacheRepository repository)
    {
        return new TranslationCacheService(repository, new TranslationCacheOptions());
    }

    private static TranslatorSettings CreateSettings(
        string provider = "Google",
        string sourceLanguage = "en",
        string targetLanguage = "ru")
    {
        return new TranslatorSettings
        {
            Provider = provider,
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage,
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
