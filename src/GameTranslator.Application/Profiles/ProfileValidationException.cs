using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Profiles;

public sealed class ProfileValidationException : InvalidOperationException
{
    public ProfileValidationException(IReadOnlyList<ProfileValidationError> errors)
        : base("Profile validation failed.")
    {
        Errors = errors;
    }

    public IReadOnlyList<ProfileValidationError> Errors { get; }
}
