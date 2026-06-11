namespace GameTranslator.Domain.Profiles;

public sealed record ProfileValidationResult(IReadOnlyList<ProfileValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
