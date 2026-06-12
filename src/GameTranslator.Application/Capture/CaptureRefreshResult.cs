namespace GameTranslator.Application.Capture;

/// <summary>
/// Contains the latest captured frame and measured refresh metrics for a short capture probe.
/// </summary>
public sealed class CaptureRefreshResult
{
    public CaptureRefreshResult(CapturedFrame latestFrame, CaptureRefreshMetrics metrics)
    {
        LatestFrame = latestFrame ?? throw new ArgumentNullException(nameof(latestFrame));
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public CapturedFrame LatestFrame { get; }

    public CaptureRefreshMetrics Metrics { get; }
}
