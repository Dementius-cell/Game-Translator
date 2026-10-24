namespace GameTranslator.Application.Capture;

/// <summary>Obtains a frame with known source provenance within the requested age, or fails.</summary>
public interface IRecentCaptureFrameSource : ICaptureFrameSource
{
    Task<CapturedFrame> CaptureRecentAsync(
        CaptureRegion region,
        TimeSpan maximumSourceAge,
        CancellationToken cancellationToken = default);
}
