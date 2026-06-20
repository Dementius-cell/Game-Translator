namespace GameTranslator.Domain.Profiles;

public static class ProfileValidationErrorCodes
{
    public const string MissingSchemaVersion = "profile.schemaVersion.missing";

    public const string UnsupportedSchemaVersion = "profile.schemaVersion.unsupported";

    public const string InvalidOcrZoneBounds = "profile.ocrZone.bounds.invalid";

    public const string OverlappingOcrZones = "profile.ocrZone.overlap";

    public const string InvalidOcrSettings = "profile.ocr.settings.invalid";

    public const string InvalidOcrPreprocessingSettings = "profile.ocr.preprocessing.invalid";
}