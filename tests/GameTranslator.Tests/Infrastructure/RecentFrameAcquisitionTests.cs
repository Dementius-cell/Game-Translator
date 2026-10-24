using GameTranslator.Application.Capture;
using GameTranslator.Infrastructure.Capture;

namespace GameTranslator.Tests.Infrastructure;

public sealed class RecentFrameAcquisitionTests
{
    [Fact]
    public async Task LeadingCompositorTime_IsBoundedByActualArrival_NotExtendedOnReuse()
    {
        var origin = new CaptureFrameOrigin(Guid.NewGuid(), Now + TimeSpan.FromMilliseconds(6), DateTimeOffset.UtcNow)
            { ObservedAtTimestamp = Now };
        var frame = new CapturedFrame(new CaptureRegion(0, 0, 1, 1), 1, 1, 4, "Bgra32", new byte[4],
            DateTimeOffset.UtcNow, origin, true);
        var restarts = 0;
        var policy = new RecentFrameAcquisition();
        var accepted = await policy.AcquireAsync(Budget, _ => Task.FromResult(frame), () => restarts++,
            () => Now, CancellationToken.None);
        Assert.Same(frame, accepted);
        Assert.Equal(0, restarts);
        await Assert.ThrowsAsync<CaptureFrameSourceException>(() => policy.AcquireAsync(Budget,
            _ => Task.FromResult(frame), () => restarts++, () => Now + TimeSpan.FromMilliseconds(501), CancellationToken.None));
        Assert.Equal(1, restarts);
        Assert.Equal(Now + TimeSpan.FromMilliseconds(6), origin.SourceTimestamp);
    }

    private static readonly TimeSpan Now = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(500);
    private static CapturedFrame Frame(TimeSpan sourceTime, bool cached = true, bool knownOrigin = true) =>
        new(new CaptureRegion(0, 0, 1, 1), 1, 1, 4, "Bgra32", new byte[4], DateTimeOffset.UtcNow,
            knownOrigin ? new CaptureFrameOrigin(Guid.NewGuid(), sourceTime, DateTimeOffset.MinValue) : null, cached);

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public async Task RecentCachedSourceIsAcceptedWithoutRestart_RegardlessOfUtcTime(int ageMs)
    {
        var frame = Frame(Now - TimeSpan.FromMilliseconds(ageMs));
        var restarts = 0;
        var result = await new RecentFrameAcquisition().AcquireAsync(Budget, _ => Task.FromResult(frame),
            () => restarts++, () => Now, CancellationToken.None);
        Assert.Same(frame, result);
        Assert.Equal(0, restarts);
    }

    [Theory]
    [InlineData(501, true)]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    public async Task StaleFutureOrUnknownOrigin_RequiresOneNewSourceFrame(int ageMs, bool known)
    {
        var old = Frame(Now - TimeSpan.FromMilliseconds(ageMs), knownOrigin: known);
        var fresh = Frame(Now, cached: false);
        var calls = 0;
        var restarts = 0;
        var result = await new RecentFrameAcquisition().AcquireAsync(Budget,
            _ => Task.FromResult(++calls == 1 ? old : fresh), () => restarts++, () => Now, CancellationToken.None);
        Assert.Same(fresh, result);
        Assert.Equal(2, calls);
        Assert.Equal(1, restarts);
    }

    [Fact]
    public async Task RestartStillReturnsOldSource_FailsWithoutLoopOrCachedSuccess()
    {
        var old = Frame(TimeSpan.Zero);
        var calls = 0;
        var restarts = 0;
        await Assert.ThrowsAsync<CaptureFrameSourceException>(() => new RecentFrameAcquisition().AcquireAsync(Budget,
            _ => { calls++; return Task.FromResult(old); }, () => restarts++, () => Now, CancellationToken.None));
        Assert.Equal(2, calls);
        Assert.Equal(1, restarts);
    }

    [Fact]
    public async Task FailedVerificationIsRateLimitedAcrossRequests_ThenCanRecover()
    {
        var policy = new RecentFrameAcquisition();
        var clock = Now;
        var old = Frame(TimeSpan.Zero);
        var restarts = 0;
        Task<CapturedFrame> Read(CancellationToken _) => Task.FromResult(old);
        await Assert.ThrowsAsync<CaptureFrameSourceException>(() => policy.AcquireAsync(Budget, Read,
            () => restarts++, () => clock, CancellationToken.None));
        clock += TimeSpan.FromMilliseconds(499);
        await Assert.ThrowsAsync<CaptureFrameSourceException>(() => policy.AcquireAsync(Budget, Read,
            () => restarts++, () => clock, CancellationToken.None));
        Assert.Equal(1, restarts);
        clock += TimeSpan.FromMilliseconds(1);
        var reads = 0;
        var fresh = Frame(clock, false);
        var result = await policy.AcquireAsync(Budget, _ => Task.FromResult(++reads == 1 ? old : fresh),
            () => restarts++, () => clock, CancellationToken.None);
        Assert.Same(fresh, result);
        Assert.Equal(2, restarts);
    }

    [Fact]
    public async Task CancellationAfterInitialRead_DoesNotRestart()
    {
        using var cancellation = new CancellationTokenSource();
        var restarts = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RecentFrameAcquisition().AcquireAsync(Budget,
            _ => { cancellation.Cancel(); return Task.FromResult(Frame(TimeSpan.Zero)); },
            () => restarts++, () => Now, cancellation.Token));
        Assert.Equal(0, restarts);
    }

    [Fact]
    public async Task CancellationDuringFreshRead_PropagatesWithoutRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var reads = 0;
        var restarts = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RecentFrameAcquisition().AcquireAsync(Budget,
            token =>
            {
                if (++reads == 1) return Task.FromResult(Frame(TimeSpan.Zero));
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(Frame(Now));
            }, () => restarts++, () => Now, cancellation.Token));
        Assert.Equal(2, reads);
        Assert.Equal(1, restarts);
    }

    [Fact]
    public async Task SourceFailurePropagatesWithoutRestart()
    {
        var failure = new CaptureFrameSourceException("Source closed");
        var restarts = 0;
        var actual = await Assert.ThrowsAsync<CaptureFrameSourceException>(() => new RecentFrameAcquisition().AcquireAsync(Budget,
            _ => Task.FromException<CapturedFrame>(failure), () => restarts++, () => Now, CancellationToken.None));
        Assert.Same(failure, actual);
        Assert.Equal(0, restarts);
    }

    [Fact]
    public async Task ReacquisitionTimeout_DoesNotFallBackToPreviouslyReadableCache()
    {
        var reads = 0;
        var failure = new CaptureFrameSourceException("No new WGC frame arrived within the timeout");
        var actual = await Assert.ThrowsAsync<CaptureFrameSourceException>(() => new RecentFrameAcquisition().AcquireAsync(Budget,
            _ => ++reads == 1 ? Task.FromResult(Frame(TimeSpan.Zero)) : Task.FromException<CapturedFrame>(failure),
            () => { }, () => Now, CancellationToken.None));
        Assert.Same(failure, actual);
        Assert.Equal(2, reads);
    }
}
