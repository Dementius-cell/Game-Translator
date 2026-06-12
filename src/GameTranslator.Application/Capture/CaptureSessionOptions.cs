namespace GameTranslator.Application.Capture;

/// <summary>
/// Defines refresh behavior for a capture session.
/// </summary>
public sealed class CaptureSessionOptions
{
    public const int MvpTargetFramesPerSecond = 30;

    public CaptureSessionOptions(int targetFramesPerSecond = MvpTargetFramesPerSecond)
    {
        if (targetFramesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetFramesPerSecond),
                "Target frames per second must be positive.");
        }

        TargetFramesPerSecond = targetFramesPerSecond;
    }

    public int TargetFramesPerSecond { get; }

    public TimeSpan TargetRefreshInterval => TimeSpan.FromSeconds(1d / TargetFramesPerSecond);
}
