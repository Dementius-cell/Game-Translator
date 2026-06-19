namespace GameTranslator.Application.Cache;

public sealed class TranslationCacheCleanupResult
{
    public TranslationCacheCleanupResult(int memoryEntryCount, int persistentEntryCount)
    {
        if (memoryEntryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryEntryCount), "Memory cleanup count must not be negative.");
        }

        if (persistentEntryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(persistentEntryCount), "Persistent cleanup count must not be negative.");
        }

        MemoryEntryCount = memoryEntryCount;
        PersistentEntryCount = persistentEntryCount;
    }

    public int MemoryEntryCount { get; }

    public int PersistentEntryCount { get; }

    public int TotalEntryCount => checked(MemoryEntryCount + PersistentEntryCount);
}
