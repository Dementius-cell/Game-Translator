namespace GameTranslator.Domain.Profiles;

public sealed record OverlaySettings
{
    public static OverlaySettings Default { get; } = new();

    public OverlayMaskMode MaskMode { get; init; } = OverlayMaskMode.Solid;

    public string MaskColor { get; init; } = "#000000";

    public double Opacity { get; init; } = 1;

    public double Padding { get; init; }
}

public enum OverlayMaskMode
{
    Solid,
    Darken,
}
