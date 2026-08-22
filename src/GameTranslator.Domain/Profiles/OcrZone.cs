namespace GameTranslator.Domain.Profiles;

public sealed record OcrZone
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = string.Empty;

    public AbsoluteRectangle AbsoluteBounds { get; init; }

    public RelativeRectangle RelativeBounds { get; init; }

    public string OcrLanguage { get; init; } = string.Empty;

    public ContentLayoutMode ContentLayoutMode { get; init; } = ContentLayoutMode.DialogComic;

    public OcrZoneTextStyle TextStyle { get; init; } = OcrZoneTextStyle.Default;

    public TranslationGroupingMode TranslationGroupingMode { get; init; } = TranslationGroupingMode.BlockByBlock;

    public OcrZoneTextGroupingSettings TextGrouping { get; init; } = OcrZoneTextGroupingSettings.Default;

    public string ResolveOcrLanguage(string fallbackLanguage)
    {
        return string.IsNullOrWhiteSpace(OcrLanguage)
            ? fallbackLanguage?.Trim() ?? string.Empty
            : OcrLanguage.Trim();
    }
}
