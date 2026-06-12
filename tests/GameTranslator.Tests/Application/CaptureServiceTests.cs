using GameTranslator.Application.Capture;

namespace GameTranslator.Tests.Application;

public sealed class CaptureServiceTests
{
    private static readonly CaptureRegion SelectedRegion = new(120, 240, 320, 180);

    [Fact]
    public async Task CaptureAsync_WhenRegionIsSelected_UsesRegionAndReturnsFrame()
    {
        var frameSource = new FakeCaptureFrameSource();
        var service = new CaptureService(frameSource);

        var frame = await service.CaptureAsync(SelectedRegion);

        Assert.Equal(SelectedRegion, frame.Region);
        Assert.Equal(SelectedRegion.Width, frame.Width);
        Assert.Equal(SelectedRegion.Height, frame.Height);
        Assert.Equal(new[] { SelectedRegion }, frameSource.CapturedRegions);
        Assert.Equal("Bgra32", frame.PixelFormat);
        Assert.NotEmpty(frame.PixelData.ToArray());
    }

    [Fact]
    public async Task CaptureSession_RefreshAsync_UsesSameRegionAndReturnsLatestFrame()
    {
        var frameSource = new FakeCaptureFrameSource();
        var service = new CaptureService(frameSource);
        await using var session = service.CreateSession(SelectedRegion);

        var first = await session.RefreshAsync();
        var second = await session.RefreshAsync();

        Assert.Equal(2, frameSource.CapturedRegions.Count);
        Assert.All(frameSource.CapturedRegions, region => Assert.Equal(SelectedRegion, region));
        Assert.True(second.CapturedAt > first.CapturedAt);
        Assert.NotEqual(first.PixelData.ToArray()[0], second.PixelData.ToArray()[0]);
    }

    [Fact]
    public async Task CaptureAsync_WhenSourceFails_PropagatesCaptureFrameSourceException()
    {
        var expected = new CaptureFrameSourceException("Capture source is unavailable.");
        var frameSource = new FakeCaptureFrameSource
        {
            Failure = expected,
        };
        var service = new CaptureService(frameSource);

        var actual = await Assert.ThrowsAsync<CaptureFrameSourceException>(
            () => service.CaptureAsync(SelectedRegion));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task CaptureAsync_WhenCancellationIsRequested_DoesNotCallFrameSource()
    {
        var frameSource = new FakeCaptureFrameSource();
        var service = new CaptureService(frameSource);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.CaptureAsync(SelectedRegion, cancellation.Token));

        Assert.Empty(frameSource.CapturedRegions);
    }

    [Fact]
    public async Task CaptureSession_RefreshAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var frameSource = new FakeCaptureFrameSource();
        var service = new CaptureService(frameSource);
        var session = service.CreateSession(SelectedRegion);
        await session.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => session.RefreshAsync());

        Assert.True(session.IsDisposed);
        Assert.Empty(frameSource.CapturedRegions);
    }

    [Fact]
    public async Task CaptureSession_MeasureRefreshAsync_CapturesRequestedFramesAndReportsMetrics()
    {
        var frameSource = new FakeCaptureFrameSource();
        var service = new CaptureService(frameSource);
        await using var session = service.CreateSession(SelectedRegion);

        var result = await session.MeasureRefreshAsync(30);

        Assert.Equal(30, frameSource.CapturedRegions.Count);
        Assert.Equal(30, result.Metrics.CapturedFrameCount);
        Assert.Equal(30, result.Metrics.TargetFramesPerSecond);
        Assert.True(result.Metrics.FramesPerSecond > 0);
        Assert.True(result.Metrics.MeetsTarget);
        Assert.Equal(SelectedRegion, result.LatestFrame.Region);
    }

    [Fact]
    public async Task CaptureSession_MeasureRefreshAsync_WhenCanceled_StopsBeforeRequestingFrames()
    {
        var frameSource = new FakeCaptureFrameSource();
        var service = new CaptureService(frameSource);
        await using var session = service.CreateSession(SelectedRegion);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => session.MeasureRefreshAsync(30, cancellation.Token));

        Assert.Empty(frameSource.CapturedRegions);
    }

    [Fact]
    public void CaptureSessionOptions_DefaultToDocumentedMvpRefreshTarget()
    {
        var options = new CaptureSessionOptions();

        Assert.Equal(30, options.TargetFramesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(1d / 30), options.TargetRefreshInterval);
    }

    [Theory]
    [InlineData(0, 0, 0, 100)]
    [InlineData(0, 0, 100, 0)]
    [InlineData(-1, 0, 100, 100)]
    [InlineData(0, -1, 100, 100)]
    public void CaptureRegion_WhenBoundsAreInvalid_ThrowsArgumentOutOfRangeException(
        int x,
        int y,
        int width,
        int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureRegion(x, y, width, height));
    }

    private sealed class FakeCaptureFrameSource : ICaptureFrameSource
    {
        private static readonly DateTimeOffset FirstFrameTime = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

        public List<CaptureRegion> CapturedRegions { get; } = new();

        public Exception? Failure { get; init; }

        public Task<CapturedFrame> CaptureAsync(CaptureRegion region, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Failure is not null)
            {
                return Task.FromException<CapturedFrame>(Failure);
            }

            CapturedRegions.Add(region);

            var frameNumber = CapturedRegions.Count;
            return Task.FromResult(CreateFrame(region, frameNumber));
        }

        private static CapturedFrame CreateFrame(CaptureRegion region, int frameNumber)
        {
            var stride = checked(region.Width * 4);
            var pixels = Enumerable
                .Repeat((byte)frameNumber, checked(stride * region.Height))
                .ToArray();

            return new CapturedFrame(
                region,
                region.Width,
                region.Height,
                stride,
                "Bgra32",
                pixels,
                FirstFrameTime.AddMilliseconds(frameNumber));
        }
    }
}
