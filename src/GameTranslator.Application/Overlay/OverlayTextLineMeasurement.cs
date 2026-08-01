namespace GameTranslator.Application.Overlay;

public sealed class OverlayTextLineMeasurement
{
    public OverlayTextLineMeasurement(
        int width,
        int height,
        int textLength,
        bool hasOverflowed)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Overlay text line width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Overlay text line height must be positive.");
        }

        if (textLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textLength), "Overlay text line length must not be negative.");
        }

        Width = width;
        Height = height;
        TextLength = textLength;
        HasOverflowed = hasOverflowed;
    }

    public int Width { get; }

    public int Height { get; }

    public int TextLength { get; }

    public bool HasOverflowed { get; }
}
