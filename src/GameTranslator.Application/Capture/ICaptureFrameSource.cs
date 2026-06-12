namespace GameTranslator.Application.Capture;

/// <summary>
/// Provides raw frames for selected screen regions without exposing platform-specific capture APIs.
/// </summary>
public interface ICaptureFrameSource
{
    Task<CapturedFrame> CaptureAsync(CaptureRegion region, CancellationToken cancellationToken = default);
}
