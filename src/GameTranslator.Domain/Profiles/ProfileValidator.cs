namespace GameTranslator.Domain.Profiles;

public sealed class ProfileValidator
{
    public ProfileValidationResult Validate(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var errors = new List<ProfileValidationError>();

        ValidateSchemaVersion(profile, errors);
        ValidateOcrZones(profile, errors);

        return new ProfileValidationResult(errors.AsReadOnly());
    }

    private static void ValidateSchemaVersion(GameProfile profile, List<ProfileValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(profile.SchemaVersion))
        {
            errors.Add(new ProfileValidationError(
                ProfileValidationErrorCodes.MissingSchemaVersion,
                "Profile schemaVersion is required."));
            return;
        }

        if (!StringComparer.Ordinal.Equals(profile.SchemaVersion, GameProfile.CurrentSchemaVersion))
        {
            errors.Add(new ProfileValidationError(
                ProfileValidationErrorCodes.UnsupportedSchemaVersion,
                $"Profile schemaVersion '{profile.SchemaVersion}' is not supported."));
        }
    }

    private static void ValidateOcrZones(GameProfile profile, List<ProfileValidationError> errors)
    {
        var zones = profile.OcrZones ?? Array.Empty<OcrZone>();

        for (var index = 0; index < zones.Count; index++)
        {
            if (!zones[index].AbsoluteBounds.HasPositiveSize)
            {
                errors.Add(new ProfileValidationError(
                    ProfileValidationErrorCodes.InvalidOcrZoneBounds,
                    $"OCR zone '{zones[index].Name}' must have positive absolute width and height."));
            }
        }

        for (var first = 0; first < zones.Count; first++)
        {
            for (var second = first + 1; second < zones.Count; second++)
            {
                if (zones[first].AbsoluteBounds.Intersects(zones[second].AbsoluteBounds))
                {
                    errors.Add(new ProfileValidationError(
                        ProfileValidationErrorCodes.OverlappingOcrZones,
                        $"OCR zones '{zones[first].Name}' and '{zones[second].Name}' overlap."));
                }
            }
        }
    }
}
