namespace GameTranslator.Infrastructure.Capture;

/// <summary>Called under the capture gate; changes retire cached frames before becoming current.</summary>
internal sealed class CaptureColorMonitor
{
    private TimeSpan? lastCheck;
    internal CaptureColorInfo Current { get; private set; } = CaptureColorInfo.Unknown;

    internal bool Refresh(TimeSpan now, Func<CaptureColorInfo> read, Action retireFrames)
    {
        if (lastCheck is { } last && now - last < TimeSpan.FromSeconds(1)) return false;
        var next = read();
        if (next == Current) { lastCheck = now; return false; }
        retireFrames(); // If retirement fails, retain the old mode and retry on the next call.
        Current = next;
        lastCheck = now;
        return true;
    }
}
