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
}
