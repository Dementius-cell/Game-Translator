namespace GameTranslator.Domain.Profiles;

public static class ProfileValidationErrorCodes
{
    public const string MissingSchemaVersion = "profile.schemaVersion.missing";

    public const string UnsupportedSchemaVersion = "profile.schemaVersion.unsupported";

    public const string InvalidOcrZoneBounds = "profile.ocrZone.bounds.invalid";

    public const string InvalidOcrZoneContentLayoutMode = "profile.ocrZone.contentLayoutMode.invalid";

    public const string InvalidOcrZoneDetectorPreset = "profile.ocrZone.detectorPreset.invalid";

    public const string InvalidOcrZoneCandidateGrouping = "profile.ocrZone.candidateGrouping.invalid";

    public const string InvalidOcrZoneTextStyle = "profile.ocrZone.textStyle.invalid";

    public const string InvalidOcrZoneTranslationGroupingMode = "profile.ocrZone.translationGroupingMode.invalid";

    public const string InvalidOcrZoneTextGrouping = "profile.ocrZone.textGrouping.invalid";

    public const string OverlappingOcrZones = "profile.ocrZone.overlap";

    public const string InvalidOcrSettings = "profile.ocr.settings.invalid";

    public const string InvalidOcrPreprocessingSettings = "profile.ocr.preprocessing.invalid";
}
