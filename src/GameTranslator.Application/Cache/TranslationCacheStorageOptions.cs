namespace GameTranslator.Application.Cache;

public sealed class TranslationCacheStorageOptions
{
    public TranslationCacheStorageOptions(string databaseFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFilePath);

        DatabaseFilePath = databaseFilePath;
    }

    public string DatabaseFilePath { get; }
}
