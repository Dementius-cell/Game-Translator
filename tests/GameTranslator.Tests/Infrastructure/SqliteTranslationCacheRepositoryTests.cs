using System.IO;
using GameTranslator.Application.Cache;
using GameTranslator.Infrastructure.Cache;

namespace GameTranslator.Tests.Infrastructure;

public sealed class SqliteTranslationCacheRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);
    private readonly string workingDirectory = Path.Combine(
        Path.GetTempPath(),
        "GameTranslator.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndGetAsync_PersistsEntryAcrossRepositoryInstances()
    {
        var key = new TranslationCacheKey("Google", "en", "ru", "Hello");
        var firstRepository = CreateRepository();
        await firstRepository.SaveAsync(
            new TranslationCacheEntry(
                key,
                "Привет",
                Now,
                Now.AddDays(30),
                Now,
                hitCount: 0));
        var secondRepository = CreateRepository();

        var entry = await secondRepository.GetAsync(key, Now.AddMinutes(1));

        Assert.NotNull(entry);
        Assert.Equal("Привет", entry.TranslatedText);
        Assert.Equal("Google", entry.Key.Provider);
        Assert.Equal("en", entry.Key.SourceLanguage);
        Assert.Equal("ru", entry.Key.TargetLanguage);
        Assert.Equal(1, entry.HitCount);
    }

    [Fact]
    public async Task GetAsync_WhenEntryIsExpired_ReturnsNull()
    {
        var key = new TranslationCacheKey("Google", "en", "ru", "Hello");
        var repository = CreateRepository();
        await repository.SaveAsync(
            new TranslationCacheEntry(
                key,
                "Old",
                Now.AddDays(-31),
                Now.AddDays(-1),
                Now.AddDays(-2),
                hitCount: 0));

        var entry = await repository.GetAsync(key, Now);

        Assert.Null(entry);
    }

    [Fact]
    public async Task DeleteExpiredAsync_RemovesOnlyExpiredEntries()
    {
        var repository = CreateRepository();
        var expiredKey = new TranslationCacheKey("Google", "en", "ru", "Expired");
        var freshKey = new TranslationCacheKey("Google", "en", "ru", "Fresh");
        await repository.SaveAsync(
            new TranslationCacheEntry(
                expiredKey,
                "Old",
                Now.AddDays(-31),
                Now.AddDays(-1),
                Now.AddDays(-2),
                hitCount: 0));
        await repository.SaveAsync(
            new TranslationCacheEntry(
                freshKey,
                "Fresh",
                Now,
                Now.AddDays(30),
                Now,
                hitCount: 0));

        var deleted = await repository.DeleteExpiredAsync(Now);

        Assert.Equal(1, deleted);
        Assert.Null(await repository.GetAsync(expiredKey, Now));
        Assert.NotNull(await repository.GetAsync(freshKey, Now));
    }

    public void Dispose()
    {
        if (Directory.Exists(workingDirectory))
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    private SqliteTranslationCacheRepository CreateRepository()
    {
        return new SqliteTranslationCacheRepository(
            new TranslationCacheStorageOptions(Path.Combine(workingDirectory, "translations.db")));
    }
}
