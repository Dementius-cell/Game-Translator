namespace GameTranslator.Domain.Profiles;

public sealed record OcrZone
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = string.Empty;

    public AbsoluteRectangle AbsoluteBounds { get; init; }

    public RelativeRectangle RelativeBounds { get; init; }

    public OcrZoneTextStyle TextStyle { get; init; } = OcrZoneTextStyle.Default;

    public TranslationGroupingMode TranslationGroupingMode { get; init; } = TranslationGroupingMode.BlockByBlock;
}
