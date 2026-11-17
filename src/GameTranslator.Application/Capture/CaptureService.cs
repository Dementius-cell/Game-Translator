namespace GameTranslator.Application.Capture;

public sealed class CaptureService
{
    private readonly ICaptureFrameSource frameSource;

    public CaptureService(ICaptureFrameSource frameSource)
    {
        this.frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
    }

    public Task<CapturedFrame> CaptureAsync(CaptureRegion region, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return frameSource.CaptureAsync(region, cancellationToken);
    }

    public CaptureSession CreateSession(CaptureRegion region, CaptureSessionOptions? options = null)
    {
        return new CaptureSession(frameSource, region, options);
    }

    /// <summary>Recording mode: reject queued/cached frames from before the overlay was hidden.</summary>
    public async Task<CapturedFrame> CaptureAfterAsync(CaptureRegion region, TimeSpan hiddenAfter,
        CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            while (true)
            {
                var frame = await CaptureRecentAsync(region, TimeSpan.FromMilliseconds(500), deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                if (frame.Origin is { } origin && origin.SourceTimestamp > hiddenAfter
                    && origin.FreshnessTimestamp > hiddenAfter) return frame;
                await Task.Delay(10, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CaptureFrameSourceException("No source frame after hiding the recording overlay was available.");
        }
    }

    public Task<CapturedFrame> CaptureRecentAsync(CaptureRegion region, TimeSpan maximumSourceAge,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumSourceAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumSourceAge));
        return frameSource is IRecentCaptureFrameSource recentSource
            ? recentSource.CaptureRecentAsync(region, maximumSourceAge, cancellationToken)
            : Task.FromException<CapturedFrame>(new CaptureFrameSourceException("The capture source cannot verify frame freshness."));
    }
}
