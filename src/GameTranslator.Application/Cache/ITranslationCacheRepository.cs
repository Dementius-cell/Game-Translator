namespace GameTranslator.Application.Cache;

public interface ITranslationCacheRepository
{
    Task<int> DeleteAllAsync(CancellationToken cancellationToken = default);

    Task<TranslationCacheEntry?> GetAsync(
        TranslationCacheKey key,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        TranslationCacheEntry entry,
        CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
