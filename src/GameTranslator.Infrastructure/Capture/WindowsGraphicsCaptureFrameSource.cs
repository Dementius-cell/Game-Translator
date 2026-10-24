using System.Diagnostics;
using GameTranslator.Application.Capture;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace GameTranslator.Infrastructure.Capture;

public sealed class WindowsGraphicsCaptureFrameSource : IRecentCaptureFrameSource, IDisposable
{
    private const int BytesPerPixel = 4;
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(3);
    // Leave room for scheduler overhead while keeping the measured MVP path above 30 FPS.
    private static readonly TimeSpan CachedFrameRefreshInterval = TimeSpan.FromMilliseconds(25);
    private readonly Lazy<Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice> direct3DDevice = new(Direct3D11DeviceFactory.CreateDevice);
    private readonly Lazy<GraphicsCaptureItem> primaryMonitorItem = new(GraphicsCaptureItemFactory.CreateForPrimaryMonitor);
    private readonly SemaphoreSlim captureGate = new(1, 1);
    private readonly object pendingCaptureLock = new();
    private readonly Guid sourceId = Guid.NewGuid();
    private readonly RecentFrameAcquisition recentFrameAcquisition = new();
    private int captureGeneration;
    private bool itemClosed;
    private bool closedHandlerRegistered;
    private CaptureState? captureState;
    private PendingCapture? pendingCapture;
    private FullFrameSnapshot? latestFrame;
    private bool disposed;
    private readonly CaptureFrameDiagnosticRecorder? frameDiagnostics = CaptureFrameDiagnosticRecorder.CreateIfEnabled();

    public Task<CapturedFrame> CaptureAsync(CaptureRegion region, CancellationToken cancellationToken = default) =>
        CaptureCoreAsync(region, null, cancellationToken);

    public Task<CapturedFrame> CaptureRecentAsync(
        CaptureRegion region, TimeSpan maximumSourceAge, CancellationToken cancellationToken = default)
    {
        if (maximumSourceAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumSourceAge));
        return CaptureCoreAsync(region, maximumSourceAge, cancellationToken);
    }

    private async Task<CapturedFrame> CaptureCoreAsync(
        CaptureRegion region, TimeSpan? maximumSourceAge, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new CaptureFrameSourceException("Windows Graphics Capture is not supported on this device.");
        }

        try
        {
            await captureGate.WaitAsync(cancellationToken);
            try
            {
                CapturedFrame captured;
                if (maximumSourceAge is { } maximumAge)
                    captured = await recentFrameAcquisition.AcquireAsync(maximumAge,
                        token => CaptureNextFrameAsync(region, token), RestartCapture,
                        MonotonicNow, cancellationToken);
                else captured = await CaptureNextFrameAsync(region, cancellationToken);
                frameDiagnostics?.TryRecord(captured);
                return captured;
            }
            finally
            {
                captureGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new CaptureFrameSourceException("Timed out waiting for a Windows Graphics Capture frame.");
        }
        catch (CaptureFrameSourceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CaptureFrameSourceException("Windows Graphics Capture failed to capture the selected region.", exception);
        }
    }

    private async Task<CapturedFrame> CaptureNextFrameAsync(CaptureRegion region, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(latestFrame is null ? FrameTimeout : CachedFrameRefreshInterval);

        var frameCompletion = new TaskCompletionSource<CapturedFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new PendingCapture(region, frameCompletion);
        using var cancellationRegistration = timeout.Token.Register(
            static state =>
            {
                var pending = (PendingCapture)state!;
                pending.Completion.TrySetCanceled();
            },
            request);

        lock (pendingCaptureLock)
        {
            ThrowIfSourceUnavailable();
            pendingCapture = request;
        }

        try
        {
            // Register the waiter before StartCapture: a static source may produce only its first frame.
            EnsureCaptureState();
            return await frameCompletion.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && TryCreateCachedFrame(region, out var cachedFrame))
        {
            return cachedFrame;
        }
        finally
        {
            ClearPendingCapture(request);
        }
    }

    private void EnsureCaptureState()
    {
        lock (pendingCaptureLock)
        {
            ThrowIfSourceUnavailable();
            if (captureState is not null) return;
            if (!closedHandlerRegistered)
            {
                primaryMonitorItem.Value.Closed += OnItemClosed;
                closedHandlerRegistered = true;
            }
            var generation = captureGeneration;
            captureState = CaptureState.Start(
                direct3DDevice.Value,
                primaryMonitorItem.Value,
                (sender, args) => OnFrameArrived(sender, args, generation));
        }
    }

    private static TimeSpan MonotonicNow() =>
        TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);

    private void RestartCapture()
    {
        CaptureState? previous;
        lock (pendingCaptureLock)
        {
            ThrowIfSourceUnavailable();
            captureGeneration++;
            latestFrame = null;
            previous = captureState;
            captureState = null;
        }
        previous?.Dispose();
    }

    private void ThrowIfSourceUnavailable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (itemClosed) throw new CaptureFrameSourceException("The captured item has closed.");
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        lock (pendingCaptureLock)
        {
            itemClosed = true;
            captureGeneration++;
            latestFrame = null;
            pendingCapture?.Completion.TrySetException(new CaptureFrameSourceException("The captured item has closed."));
            pendingCapture = null;
        }
    }

    public void Dispose()
    {
        CaptureState? previous;
        lock (pendingCaptureLock)
        {
            if (disposed) return;
            disposed = true;
            captureGeneration++;
            latestFrame = null;
            pendingCapture?.Completion.TrySetCanceled();
            pendingCapture = null;
            previous = captureState;
            captureState = null;
        }
        if (closedHandlerRegistered) primaryMonitorItem.Value.Closed -= OnItemClosed;
        previous?.Dispose();
        // An in-flight caller must still release the gate after cancellation.
    }

    private async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args, int generation)
    {
        PendingCapture? request;
        lock (pendingCaptureLock)
        {
            if (disposed || itemClosed || generation != captureGeneration) return;
            request = pendingCapture;
            pendingCapture = null;
        }

        try
        {
            using var frame = sender.TryGetNextFrame();
            if (request is null) return;
            if (frame is null)
                throw new CaptureFrameSourceException("The capture frame pool returned no frame.");
            var sourceTimestamp = frame.SystemRelativeTime;
            var observedAt = MonotonicNow();
            var sourceAge = observedAt - sourceTimestamp;
            var origin = new CaptureFrameOrigin(sourceId, sourceTimestamp,
                DateTimeOffset.UtcNow - (sourceAge > TimeSpan.Zero ? sourceAge : TimeSpan.Zero))
                { ObservedAtTimestamp = observedAt };
            var snapshot = await CopyFrameAsync(frame, origin, CancellationToken.None);
            lock (pendingCaptureLock)
            {
                if (disposed || itemClosed || generation != captureGeneration)
                {
                    request.Completion.TrySetCanceled();
                    return;
                }
                if (latestFrame is null || latestFrame.Origin.SourceTimestamp <= sourceTimestamp)
                    latestFrame = snapshot;
                request.Completion.TrySetResult(snapshot.Crop(request.Region, DateTimeOffset.UtcNow));
            }
        }
        catch (OperationCanceledException)
        {
            request?.Completion.TrySetCanceled();
        }
        catch (Exception exception)
        {
            request?.Completion.TrySetException(exception);
        }
    }

    private void ClearPendingCapture(PendingCapture request)
    {
        lock (pendingCaptureLock)
        {
            if (ReferenceEquals(pendingCapture, request))
            {
                pendingCapture = null;
            }
        }
    }

    private bool TryCreateCachedFrame(CaptureRegion region, out CapturedFrame frame)
    {
        lock (pendingCaptureLock)
        {
            ThrowIfSourceUnavailable();
            var snapshot = latestFrame;
            if (snapshot is not null && snapshot.Contains(region))
            {
                frame = snapshot.Crop(region, DateTimeOffset.UtcNow, isCachedReuse: true);
                return true;
            }
        }

        frame = null!;
        return false;
    }

    private static async Task<FullFrameSnapshot> CopyFrameAsync(
        Direct3D11CaptureFrame frame,
        CaptureFrameOrigin origin,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var surfaceBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
            frame.Surface,
            BitmapAlphaMode.Premultiplied);
        SoftwareBitmap bitmapToCopy = surfaceBitmap;
        SoftwareBitmap? convertedBitmap = null;

        try
        {
            if (surfaceBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8
                || surfaceBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                convertedBitmap = SoftwareBitmap.Convert(
                    surfaceBitmap,
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied);
                bitmapToCopy = convertedBitmap;
            }

            var availableWidth = Math.Min(bitmapToCopy.PixelWidth, frame.ContentSize.Width);
            var availableHeight = Math.Min(bitmapToCopy.PixelHeight, frame.ContentSize.Height);
            var sourceStride = checked(bitmapToCopy.PixelWidth * BytesPerPixel);
            var fullFrameBytes = CopyBitmapBytes(bitmapToCopy, sourceStride, bitmapToCopy.PixelHeight);

            return new FullFrameSnapshot(availableWidth, availableHeight, sourceStride, fullFrameBytes, origin);
        }
        finally
        {
            convertedBitmap?.Dispose();
        }
    }

    private static byte[] CopyBitmapBytes(SoftwareBitmap bitmap, int stride, int height)
    {
        var byteCount = checked(stride * height);
        var buffer = new Windows.Storage.Streams.Buffer((uint)byteCount);
        bitmap.CopyToBuffer(buffer);

        var bytes = new byte[byteCount];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);

        return bytes;
    }

    private static byte[] CropFrame(
        byte[] sourceBytes,
        int sourceStride,
        CaptureRegion region,
        int targetStride)
    {
        var targetBytes = new byte[checked(targetStride * region.Height)];
        var sourceXOffset = checked(region.X * BytesPerPixel);

        for (var row = 0; row < region.Height; row++)
        {
            var sourceOffset = checked(((region.Y + row) * sourceStride) + sourceXOffset);
            var targetOffset = checked(row * targetStride);

            Array.Copy(sourceBytes, sourceOffset, targetBytes, targetOffset, targetStride);
        }

        return targetBytes;
    }

    private sealed record PendingCapture(
        CaptureRegion Region,
        TaskCompletionSource<CapturedFrame> Completion);

    internal sealed class FullFrameSnapshot
    {
        public FullFrameSnapshot(int width, int height, int stride, byte[] pixelData, CaptureFrameOrigin origin)
        {
            Width = width;
            Height = height;
            Stride = stride;
            PixelData = pixelData;
            Origin = origin;
        }

        public int Width { get; }

        public int Height { get; }

        public int Stride { get; }

        public byte[] PixelData { get; }

        public CaptureFrameOrigin Origin { get; }

        public bool Contains(CaptureRegion region)
        {
            return region.X + region.Width <= Width
                && region.Y + region.Height <= Height;
        }

        public CapturedFrame Crop(CaptureRegion region, DateTimeOffset deliveredAt, bool isCachedReuse = false)
        {
            if (!Contains(region))
            {
                throw new CaptureFrameSourceException(
                    $"Capture region {region.Width}x{region.Height} at {region.X},{region.Y} is outside the captured content {Width}x{Height}.");
            }

            var targetStride = checked(region.Width * BytesPerPixel);
            var croppedBytes = CropFrame(PixelData, Stride, region, targetStride);

            return new CapturedFrame(
                region,
                region.Width,
                region.Height,
                targetStride,
                "Bgra32",
                croppedBytes,
                deliveredAt,
                Origin,
                isCachedReuse);
        }
    }

    private sealed class CaptureState : IDisposable
    {
        private bool disposed;
        private readonly Direct3D11CaptureFramePool framePool;
        private readonly GraphicsCaptureSession session;

        private CaptureState(Direct3D11CaptureFramePool framePool, GraphicsCaptureSession session)
        {
            this.framePool = framePool;
            this.session = session;
        }

        public static CaptureState Start(
            Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice device,
            GraphicsCaptureItem item,
            Windows.Foundation.TypedEventHandler<Direct3D11CaptureFramePool, object> frameArrivedHandler)
        {
            var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                item.Size);
            GraphicsCaptureSession? session = null;
            try
            {
                session = framePool.CreateCaptureSession(item);
                session.IsCursorCaptureEnabled = false;
                framePool.FrameArrived += frameArrivedHandler;
                session.StartCapture();
                return new CaptureState(framePool, session);
            }
            catch
            {
                session?.Dispose();
                framePool.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            session.Dispose();
            framePool.Dispose();
        }
    }
}
