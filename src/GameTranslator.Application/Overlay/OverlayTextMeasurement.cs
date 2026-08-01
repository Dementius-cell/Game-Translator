namespace GameTranslator.Application.Overlay;

public sealed class OverlayTextMeasurement
{
    public OverlayTextMeasurement(
        int width,
        int height,
        IEnumerable<OverlayTextLineMeasurement>? lines = null)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Overlay text measurement width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Overlay text measurement height must be positive.");
        }

        Width = width;
        Height = height;
        Lines = lines?.ToArray() ?? new[] { new OverlayTextLineMeasurement(width, height, 0, hasOverflowed: false) };
    }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<OverlayTextLineMeasurement> Lines { get; }

    public int LineCount => Lines.Count;

    public bool HasOverflowed => Lines.Any(line => line.HasOverflowed);
}
