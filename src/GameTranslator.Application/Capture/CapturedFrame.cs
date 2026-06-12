namespace GameTranslator.Application.Capture;

/// <summary>
/// Contains raw pixels captured for one region at a specific point in time.
/// </summary>
public sealed class CapturedFrame
{
    private readonly byte[] pixelData;

    public CapturedFrame(
        CaptureRegion region,
        int width,
        int height,
        int stride,
        string pixelFormat,
        ReadOnlyMemory<byte> pixelData,
        DateTimeOffset capturedAt)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Captured frame width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Captured frame height must be positive.");
        }

        if (stride < width)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "Captured frame stride must cover at least one row.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(pixelFormat);

        var requiredBytes = checked(stride * height);
        if (pixelData.Length < requiredBytes)
        {
            throw new ArgumentException(
                "Captured frame pixel data must contain at least stride times height bytes.",
                nameof(pixelData));
        }

        Region = region;
        Width = width;
        Height = height;
        Stride = stride;
        PixelFormat = pixelFormat;
        this.pixelData = pixelData.ToArray();
        CapturedAt = capturedAt;
    }

    public CaptureRegion Region { get; }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public string PixelFormat { get; }

    public ReadOnlyMemory<byte> PixelData => pixelData;

    public DateTimeOffset CapturedAt { get; }
}
