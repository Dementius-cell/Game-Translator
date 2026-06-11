namespace GameTranslator.Application.Profiles;

public sealed class ProfileNotFoundException : InvalidOperationException
{
    public ProfileNotFoundException(string profileId)
        : base($"Profile '{profileId}' was not found.")
    {
        ProfileId = profileId;
    }

    public string ProfileId { get; }
}
