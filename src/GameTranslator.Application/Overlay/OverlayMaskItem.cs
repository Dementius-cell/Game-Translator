using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Overlay;

public sealed class OverlayMaskItem
{
    public OverlayMaskItem(
        OverlayMaskMode mode,
        string color,
        double opacity,
        int x,
        int y,
        int width,
        int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(color);

        if (opacity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(opacity), "Overlay mask opacity must be between 0 and 1.");
        }

        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Overlay mask X must not be negative.");
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "Overlay mask Y must not be negative.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Overlay mask width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Overlay mask height must be positive.");
        }

        Mode = mode;
        Color = color.Trim();
        Opacity = opacity;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public OverlayMaskMode Mode { get; }

    public string Color { get; }

    public double Opacity { get; }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }
}
