using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Profiles;

public interface IProfileExchangeGateway
{
    Task ExportAsync(GameProfile profile, string filePath, CancellationToken cancellationToken = default);

    Task<GameProfile> ImportAsync(string filePath, CancellationToken cancellationToken = default);
}
