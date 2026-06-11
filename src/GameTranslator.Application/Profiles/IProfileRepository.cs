using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Profiles;

public interface IProfileRepository
{
    Task<IReadOnlyList<GameProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<GameProfile?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task SaveAsync(GameProfile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
