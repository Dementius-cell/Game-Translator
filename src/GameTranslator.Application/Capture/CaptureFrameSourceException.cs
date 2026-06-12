namespace GameTranslator.Application.Capture;

/// <summary>
/// Represents a capture-specific failure reported by an application capture source.
/// </summary>
public sealed class CaptureFrameSourceException : InvalidOperationException
{
    public CaptureFrameSourceException(string message)
        : base(message)
    {
    }

    public CaptureFrameSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
