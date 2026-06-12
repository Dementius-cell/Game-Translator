using System.Diagnostics;

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

    public async Task<CaptureRefreshResult> MeasureRefreshAsync(
        int frameCount,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (frameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), "Frame count must be positive.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        CapturedFrame? latestFrame = null;
        var startedAt = Stopwatch.GetTimestamp();

        for (var index = 0; index < frameCount; index++)
        {
            latestFrame = await RefreshAsync(cancellationToken);
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        return new CaptureRefreshResult(
            latestFrame ?? throw new InvalidOperationException("Capture refresh probe did not produce a frame."),
            new CaptureRefreshMetrics(frameCount, elapsed, Options.TargetFramesPerSecond));
    }

    public ValueTask DisposeAsync()
    {
        disposed = true;
        return ValueTask.CompletedTask;
    }
}
