using System.Globalization;
using GameTranslator.Application.Cache;
using Microsoft.Data.Sqlite;

namespace GameTranslator.Infrastructure.Cache;

public sealed class SqliteTranslationCacheRepository : ITranslationCacheRepository
{
    private readonly TranslationCacheStorageOptions options;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private bool isInitialized;

    public SqliteTranslationCacheRepository(TranslationCacheStorageOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<TranslationCacheEntry?> GetAsync(
        TranslationCacheKey key,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureDatabaseAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.translated_text,
                   t.created_at_utc,
                   t.expires_at_utc,
                   t.last_accessed_at_utc,
                   t.hit_count
            FROM translations t
            INNER JOIN language_pairs lp ON lp.id = t.language_pair_id
            WHERE lp.provider = $provider
              AND lp.source_language = $source_language
              AND lp.target_language = $target_language
              AND t.source_text_hash = $source_text_hash
              AND t.source_text = $source_text
              AND t.expires_at_utc > $now
            """;
        AddKeyParameters(command, key);
        command.Parameters.AddWithValue("$now", ToStorageValue(now));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var translatedText = reader.GetString(0);
        var createdAt = FromStorageValue(reader.GetString(1));
        var expiresAt = FromStorageValue(reader.GetString(2));
        var hitCount = reader.GetInt64(4) + 1;
        await reader.DisposeAsync();

        await UpdateHitAsync(connection, key, now, hitCount, cancellationToken);
        await IncrementStatisticAsync(connection, "persistent_hits", 1, cancellationToken);

        return new TranslationCacheEntry(
            key,
            translatedText,
            createdAt,
            expiresAt,
            now,
            hitCount);
    }

    public async Task SaveAsync(
        TranslationCacheEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureDatabaseAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var languagePairId = await GetOrCreateLanguagePairAsync(
            connection,
            transaction,
            entry.Key,
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO translations (
                language_pair_id,
                source_text_hash,
                source_text,
                translated_text,
                created_at_utc,
                expires_at_utc,
                last_accessed_at_utc,
                hit_count)
            VALUES (
                $language_pair_id,
                $source_text_hash,
                $source_text,
                $translated_text,
                $created_at_utc,
                $expires_at_utc,
                $last_accessed_at_utc,
                $hit_count)
            ON CONFLICT(language_pair_id, source_text_hash, source_text)
            DO UPDATE SET
                translated_text = excluded.translated_text,
                created_at_utc = excluded.created_at_utc,
                expires_at_utc = excluded.expires_at_utc,
                last_accessed_at_utc = excluded.last_accessed_at_utc,
                hit_count = excluded.hit_count
            """;
        command.Parameters.AddWithValue("$language_pair_id", languagePairId);
        command.Parameters.AddWithValue("$source_text_hash", entry.Key.SourceTextHash);
        command.Parameters.AddWithValue("$source_text", entry.Key.SourceText);
        command.Parameters.AddWithValue("$translated_text", entry.TranslatedText);
        command.Parameters.AddWithValue("$created_at_utc", ToStorageValue(entry.CreatedAt));
        command.Parameters.AddWithValue("$expires_at_utc", ToStorageValue(entry.ExpiresAt));
        command.Parameters.AddWithValue("$last_accessed_at_utc", ToStorageValue(entry.LastAccessedAt));
        command.Parameters.AddWithValue("$hit_count", entry.HitCount);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await IncrementStatisticAsync(connection, "stores", 1, cancellationToken);
        transaction.Commit();
    }

    public async Task<int> DeleteExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureDatabaseAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM translations
            WHERE expires_at_utc <= $now
            """;
        command.Parameters.AddWithValue("$now", ToStorageValue(now));

        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        if (deleted > 0)
        {
            await IncrementStatisticAsync(connection, "cleanup_deleted", deleted, cancellationToken);
        }

        return deleted;
    }

    private async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        if (isInitialized)
        {
            return;
        }

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (isInitialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(options.DatabaseFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS language_pairs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    provider TEXT NOT NULL,
                    source_language TEXT NOT NULL,
                    target_language TEXT NOT NULL,
                    UNIQUE(provider, source_language, target_language)
                );

                CREATE TABLE IF NOT EXISTS translations (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    language_pair_id INTEGER NOT NULL,
                    source_text_hash TEXT NOT NULL,
                    source_text TEXT NOT NULL,
                    translated_text TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    expires_at_utc TEXT NOT NULL,
                    last_accessed_at_utc TEXT NOT NULL,
                    hit_count INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY(language_pair_id) REFERENCES language_pairs(id) ON DELETE CASCADE,
                    UNIQUE(language_pair_id, source_text_hash, source_text)
                );

                CREATE TABLE IF NOT EXISTS cache_statistics (
                    name TEXT PRIMARY KEY,
                    value INTEGER NOT NULL DEFAULT 0
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            isInitialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private SqliteConnection CreateConnection()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        return new SqliteConnection(connectionString);
    }

    private static async Task<long> GetOrCreateLanguagePairAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TranslationCacheKey key,
        CancellationToken cancellationToken)
    {
        await using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT OR IGNORE INTO language_pairs (
                provider,
                source_language,
                target_language)
            VALUES (
                $provider,
                $source_language,
                $target_language)
            """;
        AddLanguagePairParameters(insertCommand, key);
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText = """
            SELECT id
            FROM language_pairs
            WHERE provider = $provider
              AND source_language = $source_language
              AND target_language = $target_language
            """;
        AddLanguagePairParameters(selectCommand, key);

        return (long)(await selectCommand.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Translation cache language pair was not created."));
    }

    private static async Task UpdateHitAsync(
        SqliteConnection connection,
        TranslationCacheKey key,
        DateTimeOffset now,
        long hitCount,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE translations
            SET last_accessed_at_utc = $last_accessed_at_utc,
                hit_count = $hit_count
            WHERE language_pair_id = (
                SELECT id
                FROM language_pairs
                WHERE provider = $provider
                  AND source_language = $source_language
                  AND target_language = $target_language)
              AND source_text_hash = $source_text_hash
              AND source_text = $source_text
            """;
        AddKeyParameters(command, key);
        command.Parameters.AddWithValue("$last_accessed_at_utc", ToStorageValue(now));
        command.Parameters.AddWithValue("$hit_count", hitCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task IncrementStatisticAsync(
        SqliteConnection connection,
        string name,
        long increment,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cache_statistics (name, value)
            VALUES ($name, $increment)
            ON CONFLICT(name)
            DO UPDATE SET value = value + $increment
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$increment", increment);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddKeyParameters(SqliteCommand command, TranslationCacheKey key)
    {
        AddLanguagePairParameters(command, key);
        command.Parameters.AddWithValue("$source_text_hash", key.SourceTextHash);
        command.Parameters.AddWithValue("$source_text", key.SourceText);
    }

    private static void AddLanguagePairParameters(SqliteCommand command, TranslationCacheKey key)
    {
        command.Parameters.AddWithValue("$provider", key.Provider);
        command.Parameters.AddWithValue("$source_language", key.SourceLanguage);
        command.Parameters.AddWithValue("$target_language", key.TargetLanguage);
    }

    private static string ToStorageValue(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O");
    }

    private static DateTimeOffset FromStorageValue(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
