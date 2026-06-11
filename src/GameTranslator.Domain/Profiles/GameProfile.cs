namespace GameTranslator.Domain.Profiles;

public sealed record GameProfile
{
    public const string CurrentSchemaVersion = "1.0";

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<OcrZone> OcrZones { get; init; } = Array.Empty<OcrZone>();

    public OverlaySettings OverlaySettings { get; init; } = OverlaySettings.Default;

    public TranslatorSettings TranslatorSettings { get; init; } = TranslatorSettings.Default;
}
