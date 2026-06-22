namespace GameTranslator.UI.Services;

public sealed record ScreenRegionSelectionResult(
    int X,
    int Y,
    int Width,
    int Height,
    int ReferenceWidth,
    int ReferenceHeight);
