using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Profiles;

public interface IProfileMigration
{
    string SourceSchemaVersion { get; }

    string TargetSchemaVersion { get; }

    GameProfile Migrate(GameProfile profile);
}
