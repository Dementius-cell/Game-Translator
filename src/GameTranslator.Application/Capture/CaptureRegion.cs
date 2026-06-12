namespace GameTranslator.Application.Capture;

/// <summary>
/// Defines the screen-space rectangle requested from a capture source.
/// </summary>
public readonly record struct CaptureRegion
{
    public CaptureRegion(int x, int y, int width, int height)
    {
        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Capture region X must not be negative.");
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "Capture region Y must not be negative.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Capture region width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Capture region height must be positive.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }
}
