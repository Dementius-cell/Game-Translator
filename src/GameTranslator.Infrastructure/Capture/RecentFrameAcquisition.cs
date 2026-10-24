using GameTranslator.Application.Capture;

namespace GameTranslator.Infrastructure.Capture;

/// <summary>Called under the source capture gate; never converts cached delivery time into freshness.</summary>
internal sealed class RecentFrameAcquisition
{
    internal static readonly TimeSpan MinimumRestartInterval = TimeSpan.FromMilliseconds(500);
    private TimeSpan? lastRestart;

    public async Task<CapturedFrame> AcquireAsync(
        TimeSpan maximumSourceAge,
        Func<CancellationToken, Task<CapturedFrame>> capture,
        Action restart,
        Func<TimeSpan> monotonicNow,
        CancellationToken cancellationToken)
    {
        if (maximumSourceAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumSourceAge));

        cancellationToken.ThrowIfCancellationRequested();
        var frame = await capture(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsRecent(frame, monotonicNow(), maximumSourceAge)) return frame;

        var now = monotonicNow();
        if (lastRestart is { } previous && now - previous < MinimumRestartInterval)
            throw new CaptureFrameSourceException("A fresh source frame is unavailable; capture verification is rate limited.");

        lastRestart = now;
        restart();
        cancellationToken.ThrowIfCancellationRequested();
        frame = await capture(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRecent(frame, monotonicNow(), maximumSourceAge))
            throw new CaptureFrameSourceException("Capture verification did not produce a recent source frame.");
        return frame;
    }

    private static bool IsRecent(CapturedFrame frame, TimeSpan now, TimeSpan maximumAge)
    {
        if (frame.Origin is not { } origin) return false;
        var age = now - origin.FreshnessTimestamp;
        return age >= TimeSpan.Zero && age <= maximumAge;
    }
}
