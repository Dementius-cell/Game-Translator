using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Profiles;

public sealed class ProfileExchangeService
{
    private readonly IProfileExchangeGateway gateway;
    private readonly ProfileValidator validator;

    public ProfileExchangeService(IProfileExchangeGateway gateway, ProfileValidator validator)
    {
        this.gateway = gateway;
        this.validator = validator;
    }

    public async Task ExportAsync(GameProfile profile, string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateProfile(profile);
        await gateway.ExportAsync(profile, filePath, cancellationToken);
    }

    public async Task<GameProfile> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        var profile = await gateway.ImportAsync(filePath, cancellationToken);
        ValidateProfile(profile);

        return profile;
    }

    private void ValidateProfile(GameProfile profile)
    {
        var validation = validator.Validate(profile);
        if (!validation.IsValid)
        {
            throw new ProfileValidationException(validation.Errors);
        }
    }
}
