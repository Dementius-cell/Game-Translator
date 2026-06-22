namespace GameTranslator.Domain.Profiles;

public sealed record OcrZoneTextStyle
{
    public const string DefaultFontFamily = "Segoe UI";
    public const double DefaultFontSize = 16;
    public const double MinimumFontSize = 8;
    public const double MaximumFontSize = 72;

    public static OcrZoneTextStyle Default { get; } = new();

    public string FontFamily { get; init; } = DefaultFontFamily;

    public double FontSize { get; init; } = DefaultFontSize;

    public bool IsBold { get; init; } = true;

    public bool IsItalic { get; init; }

    public OverlayTextLayoutMode LayoutMode { get; init; } = OverlayTextLayoutMode.FitToSourceBounds;
}
