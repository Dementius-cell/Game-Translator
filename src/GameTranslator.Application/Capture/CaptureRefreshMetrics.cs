namespace GameTranslator.Application.Capture;

/// <summary>
/// Reports measured refresh performance for a capture session.
/// </summary>
public sealed class CaptureRefreshMetrics
{
    public CaptureRefreshMetrics(int capturedFrameCount, TimeSpan elapsed, int targetFramesPerSecond)
    {
        if (capturedFrameCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capturedFrameCount));
        }

        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        if (targetFramesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetFramesPerSecond));
        }

        CapturedFrameCount = capturedFrameCount;
        Elapsed = elapsed;
        TargetFramesPerSecond = targetFramesPerSecond;
    }

    public int CapturedFrameCount { get; }

    public TimeSpan Elapsed { get; }

    public int TargetFramesPerSecond { get; }

    public double FramesPerSecond => Elapsed.TotalSeconds <= 0
        ? 0
        : CapturedFrameCount / Elapsed.TotalSeconds;

    public bool MeetsTarget => FramesPerSecond >= TargetFramesPerSecond;
}
