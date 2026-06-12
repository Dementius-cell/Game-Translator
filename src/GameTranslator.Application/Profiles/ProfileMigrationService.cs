using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Profiles;

public sealed class ProfileMigrationService
{
    private readonly IReadOnlyDictionary<string, IProfileMigration> migrations;

    public ProfileMigrationService(IEnumerable<IProfileMigration>? migrations = null)
    {
        migrations ??= Array.Empty<IProfileMigration>();
        this.migrations = BuildMigrationMap(migrations);
    }

    public GameProfile MigrateToCurrent(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var current = profile;
        var visitedSchemaVersions = new HashSet<string>(StringComparer.Ordinal);

        while (!StringComparer.Ordinal.Equals(current.SchemaVersion, GameProfile.CurrentSchemaVersion)
               && migrations.TryGetValue(current.SchemaVersion, out var migration))
        {
            if (!visitedSchemaVersions.Add(current.SchemaVersion))
            {
                throw new InvalidOperationException(
                    $"Profile migration loop detected at schemaVersion '{current.SchemaVersion}'.");
            }

            current = migration.Migrate(current)
                ?? throw new InvalidOperationException(
                    $"Profile migration '{migration.SourceSchemaVersion}' -> '{migration.TargetSchemaVersion}' returned null.");

            if (!StringComparer.Ordinal.Equals(current.SchemaVersion, migration.TargetSchemaVersion))
            {
                throw new InvalidOperationException(
                    $"Profile migration '{migration.SourceSchemaVersion}' -> '{migration.TargetSchemaVersion}' produced schemaVersion '{current.SchemaVersion}'.");
            }
        }

        return current;
    }

    private static IReadOnlyDictionary<string, IProfileMigration> BuildMigrationMap(IEnumerable<IProfileMigration> migrations)
    {
        var migrationMap = new Dictionary<string, IProfileMigration>(StringComparer.Ordinal);

        foreach (var migration in migrations)
        {
            ArgumentNullException.ThrowIfNull(migration);
            ArgumentException.ThrowIfNullOrWhiteSpace(migration.SourceSchemaVersion);
            ArgumentException.ThrowIfNullOrWhiteSpace(migration.TargetSchemaVersion);

            if (migrationMap.ContainsKey(migration.SourceSchemaVersion))
            {
                throw new InvalidOperationException(
                    $"Multiple profile migrations start from schemaVersion '{migration.SourceSchemaVersion}'.");
            }

            migrationMap[migration.SourceSchemaVersion] = migration;
        }

        return migrationMap;
    }
}
