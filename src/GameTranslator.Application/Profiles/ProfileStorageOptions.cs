namespace GameTranslator.Application.Profiles;

public sealed class ProfileStorageOptions
{
    public ProfileStorageOptions(string profilesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesDirectory);

        ProfilesDirectory = profilesDirectory;
    }

    public string ProfilesDirectory { get; }
}
