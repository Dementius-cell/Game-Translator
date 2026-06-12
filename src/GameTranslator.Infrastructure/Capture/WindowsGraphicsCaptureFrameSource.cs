using GameTranslator.Application.Capture;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace GameTranslator.Infrastructure.Capture;

public sealed class WindowsGraphicsCaptureFrameSource : ICaptureFrameSource, IDisposable
{
    private const int BytesPerPixel = 4;
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(3);
    // Leave room for scheduler overhead while keeping the measured MVP path above 30 FPS.
    private static readonly TimeSpan CachedFrameRefreshInterval = TimeSpan.FromMilliseconds(25);
    private readonly Lazy<Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice> direct3DDevice = new(Direct3D11DeviceFactory.CreateDevice);
    private readonly Lazy<GraphicsCaptureItem> primaryMonitorItem = new(GraphicsCaptureItemFactory.CreateForPrimaryMonitor);
    private readonly SemaphoreSlim captureGate = new(1, 1);
    private readonly object pendingCaptureLock = new();
    private CaptureState? captureState;
    private PendingCapture? pendingCapture;
    private FullFrameSnapshot? latestFrame;
    private bool disposed;

    public async Task<CapturedFrame> CaptureAsync(CaptureRegion region, CancellationToken cancellationToken = default)
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
                return await CaptureNextFrameAsync(region, cancellationToken);
            }
            finally
            {
                captureGate.Release();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
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

        EnsureCaptureState();
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
            pendingCapture = request;
        }

        try
        {
            return await frameCompletion.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && TryCreateCachedFrame(region, out var cachedFrame))
        {
            ClearPendingCapture(request);
            return cachedFrame;
        }
    }

    private void EnsureCaptureState()
    {
        captureState ??= CaptureState.Start(
            direct3DDevice.Value,
            primaryMonitorItem.Value,
            OnFrameArrived);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        captureState?.Dispose();
        captureGate.Dispose();
    }

    private async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        PendingCapture? request;
        lock (pendingCaptureLock)
        {
            request = pendingCapture;
            pendingCapture = null;
        }

        using var frame = sender.TryGetNextFrame();
        if (request is null)
        {
            return;
        }

        try
        {
            var snapshot = await CopyFrameAsync(frame, CancellationToken.None);
            latestFrame = snapshot;
            request.Completion.TrySetResult(snapshot.Crop(request.Region, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException)
        {
            request.Completion.TrySetCanceled();
        }
        catch (Exception exception)
        {
            request.Completion.TrySetException(exception);
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
        var snapshot = latestFrame;
        if (snapshot is not null && snapshot.Contains(region))
        {
            frame = snapshot.Crop(region, DateTimeOffset.UtcNow);
            return true;
        }

        frame = null!;
        return false;
    }

    private static async Task<FullFrameSnapshot> CopyFrameAsync(
        Direct3D11CaptureFrame frame,
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

            return new FullFrameSnapshot(availableWidth, availableHeight, sourceStride, fullFrameBytes);
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

    private sealed class FullFrameSnapshot
    {
        public FullFrameSnapshot(int width, int height, int stride, byte[] pixelData)
        {
            Width = width;
            Height = height;
            Stride = stride;
            PixelData = pixelData;
        }

        public int Width { get; }

        public int Height { get; }

        public int Stride { get; }

        public byte[] PixelData { get; }

        public bool Contains(CaptureRegion region)
        {
            return region.X + region.Width <= Width
                && region.Y + region.Height <= Height;
        }

        public CapturedFrame Crop(CaptureRegion region, DateTimeOffset capturedAt)
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
                capturedAt);
        }
    }

    private sealed class CaptureState : IDisposable
    {
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
            var session = framePool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            framePool.FrameArrived += frameArrivedHandler;
            session.StartCapture();

            return new CaptureState(framePool, session);
        }

        public void Dispose()
        {
            session.Dispose();
            framePool.Dispose();
        }
    }
}
