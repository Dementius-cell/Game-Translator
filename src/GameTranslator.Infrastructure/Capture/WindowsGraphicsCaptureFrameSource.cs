using GameTranslator.Application.Capture;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace GameTranslator.Infrastructure.Capture;

public sealed class WindowsGraphicsCaptureFrameSource : ICaptureFrameSource
{
    private const int BytesPerPixel = 4;
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(3);
    private readonly Lazy<Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice> direct3DDevice = new(Direct3D11DeviceFactory.CreateDevice);
    private readonly Lazy<GraphicsCaptureItem> primaryMonitorItem = new(GraphicsCaptureItemFactory.CreateForPrimaryMonitor);

    public async Task<CapturedFrame> CaptureAsync(CaptureRegion region, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new CaptureFrameSourceException("Windows Graphics Capture is not supported on this device.");
        }

        try
        {
            return await CaptureFirstFrameAsync(region, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CaptureFrameSourceException("Timed out waiting for the first Windows Graphics Capture frame.");
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

    private async Task<CapturedFrame> CaptureFirstFrameAsync(CaptureRegion region, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FirstFrameTimeout);

        var item = primaryMonitorItem.Value;
        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            direct3DDevice.Value,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            item.Size);
        using var session = framePool.CreateCaptureSession(item);

        var frameCompletion = new TaskCompletionSource<CapturedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = timeout.Token.Register(
            static state => ((TaskCompletionSource<CapturedFrame>)state!).TrySetCanceled(),
            frameCompletion);

        var isProcessingFrame = 0;
        framePool.FrameArrived += OnFrameArrived;
        try
        {
            session.IsCursorCaptureEnabled = false;
            session.StartCapture();

            return await frameCompletion.Task.WaitAsync(timeout.Token);
        }
        finally
        {
            framePool.FrameArrived -= OnFrameArrived;
        }

        async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            if (Interlocked.Exchange(ref isProcessingFrame, 1) != 0)
            {
                return;
            }

            try
            {
                using var frame = sender.TryGetNextFrame();
                var capturedFrame = await ConvertFrameAsync(frame, region, timeout.Token);
                frameCompletion.TrySetResult(capturedFrame);
            }
            catch (OperationCanceledException)
            {
                frameCompletion.TrySetCanceled();
            }
            catch (Exception exception)
            {
                frameCompletion.TrySetException(exception);
            }
        }
    }

    private static async Task<CapturedFrame> ConvertFrameAsync(
        Direct3D11CaptureFrame frame,
        CaptureRegion region,
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
            if (region.X + region.Width > availableWidth || region.Y + region.Height > availableHeight)
            {
                throw new CaptureFrameSourceException(
                    $"Capture region {region.Width}x{region.Height} at {region.X},{region.Y} is outside the captured content {availableWidth}x{availableHeight}.");
            }

            var sourceStride = checked(bitmapToCopy.PixelWidth * BytesPerPixel);
            var targetStride = checked(region.Width * BytesPerPixel);
            var fullFrameBytes = CopyBitmapBytes(bitmapToCopy, sourceStride, bitmapToCopy.PixelHeight);
            var croppedBytes = CropFrame(fullFrameBytes, sourceStride, region, targetStride);

            return new CapturedFrame(
                region,
                region.Width,
                region.Height,
                targetStride,
                "Bgra32",
                croppedBytes,
                DateTimeOffset.UtcNow);
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
}
