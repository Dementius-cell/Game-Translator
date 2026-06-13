namespace GameTranslator.Application.Overlay;

public sealed class OverlayTextItem
{
    public OverlayTextItem(string text, int x, int y, int width, int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Overlay text X must not be negative.");
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "Overlay text Y must not be negative.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Overlay text width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Overlay text height must be positive.");
        }

        Text = text.Trim();
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public string Text { get; }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }
}
