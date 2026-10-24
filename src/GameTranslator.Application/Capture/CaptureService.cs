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
