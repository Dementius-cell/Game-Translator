namespace GameTranslator.Domain.Profiles;

public sealed class ProfileValidator
{
    public ProfileValidationResult Validate(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var errors = new List<ProfileValidationError>();

        ValidateSchemaVersion(profile, errors);
        ValidateOcrZones(profile, errors);
        ValidateOcrSettings(profile.OcrSettings, errors);
        ValidateOcrPreprocessing(profile.OcrPreprocessingSettings, errors);

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

    private static void ValidateOcrSettings(
        OcrSettings? settings,
        List<ProfileValidationError> errors)
    {
        var value = settings ?? OcrSettings.Default;

        if (!OcrSettings.IsSupportedEngine(value.Engine))
        {
            errors.Add(new ProfileValidationError(
                ProfileValidationErrorCodes.InvalidOcrSettings,
                $"OCR engine '{value.Engine}' is not supported."));
        }

        if (!OcrSettings.IsSupportedOrientationMode(value.OrientationMode))
        {
            errors.Add(new ProfileValidationError(
                ProfileValidationErrorCodes.InvalidOcrSettings,
                $"OCR orientation mode '{value.OrientationMode}' is not supported."));
        }
    }

    private static void ValidateOcrPreprocessing(
        OcrPreprocessingSettings? settings,
        List<ProfileValidationError> errors)
    {
        var value = settings ?? OcrPreprocessingSettings.Default;

        if (value.Contrast is < 0.5 or > 3)
        {
            errors.Add(new ProfileValidationError(
                ProfileValidationErrorCodes.InvalidOcrPreprocessingSettings,
                "OCR preprocessing contrast must be between 0.5 and 3."));
        }

        if (value.Brightness is < -100 or > 100)
        {
            errors.Add(new ProfileValidationError(
                ProfileValidationErrorCodes.InvalidOcrPreprocessingSettings,
                "OCR preprocessing brightness must be between -100 and 100."));
        }

        if (value.Sharpness is < 0 or > 2)
        {
            errors.Add(new ProfileValidationError(
                ProfileValidationErrorCodes.InvalidOcrPreprocessingSettings,
                "OCR preprocessing sharpness must be between 0 and 2."));
        }

        if (value.Scale is < 1 or > 3)
        {
            errors.Add(new ProfileValidationError(
                ProfileValidationErrorCodes.InvalidOcrPreprocessingSettings,
                "OCR preprocessing scale must be between 1 and 3."));
        }
    }
}