namespace GameTranslator.Application.Overlay;

public sealed class OverlayDebugItem
{
    public OverlayDebugItem(
        string sourceText,
        string translatedText,
        int x,
        int y,
        int width,
        int height)
    {
        SourceText = sourceText?.Trim() ?? string.Empty;
        TranslatedText = translatedText?.Trim() ?? string.Empty;

        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Debug overlay X must not be negative.");
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "Debug overlay Y must not be negative.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Debug overlay width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Debug overlay height must be positive.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public string SourceText { get; }

    public string TranslatedText { get; }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }
}
