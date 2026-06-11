using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Profiles;

public sealed class ProfileService
{
    private readonly IProfileRepository repository;
    private readonly ProfileValidator validator;

    public ProfileService(IProfileRepository repository, ProfileValidator validator)
    {
        this.repository = repository;
        this.validator = validator;
    }

    public async Task<GameProfile> CreateAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var profileToSave = EnsureProfileId(profile);
        ValidateProfile(profileToSave);

        await repository.SaveAsync(profileToSave, cancellationToken);

        return profileToSave;
    }

    public Task<IReadOnlyList<GameProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return repository.ListAsync(cancellationToken);
    }

    public Task<GameProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();

        return repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<GameProfile> UpdateAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var profileToSave = EnsureProfileId(profile);
        var existing = await repository.GetByIdAsync(profileToSave.Id, cancellationToken);
        if (existing is null)
        {
            throw new ProfileNotFoundException(profileToSave.Id);
        }

        ValidateProfile(profileToSave);
        await repository.SaveAsync(profileToSave, cancellationToken);

        return profileToSave;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await repository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        await repository.DeleteAsync(id, cancellationToken);
        return true;
    }

    public async Task<GameProfile> CloneAsync(
        string sourceProfileId,
        string cloneName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cloneName);
        cancellationToken.ThrowIfCancellationRequested();

        var source = await repository.GetByIdAsync(sourceProfileId, cancellationToken);
        if (source is null)
        {
            throw new ProfileNotFoundException(sourceProfileId);
        }

        var clone = source with
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = cloneName,
            OcrZones = source.OcrZones
                .Select(zone => zone with { Id = Guid.NewGuid().ToString("N") })
                .ToArray(),
        };

        ValidateProfile(clone);
        await repository.SaveAsync(clone, cancellationToken);

        return clone;
    }

    private void ValidateProfile(GameProfile profile)
    {
        var validation = validator.Validate(profile);
        if (!validation.IsValid)
        {
            throw new ProfileValidationException(validation.Errors);
        }
    }

    private static GameProfile EnsureProfileId(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return string.IsNullOrWhiteSpace(profile.Id)
            ? profile with { Id = Guid.NewGuid().ToString("N") }
            : profile;
    }
}
