namespace GameTranslator.Application.Capture;

/// <summary>
/// Represents a logical refresh session for one selected capture region.
/// </summary>
public sealed class CaptureSession : IAsyncDisposable
{
    private readonly ICaptureFrameSource frameSource;
    private bool disposed;

    public CaptureSession(
        ICaptureFrameSource frameSource,
        CaptureRegion region,
        CaptureSessionOptions? options = null)
    {
        this.frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        Region = region;
        Options = options ?? new CaptureSessionOptions();
    }

    public CaptureRegion Region { get; }

    public CaptureSessionOptions Options { get; }

    public bool IsDisposed => disposed;

    public Task<CapturedFrame> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        return frameSource.CaptureAsync(Region, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        disposed = true;
        return ValueTask.CompletedTask;
    }
}
