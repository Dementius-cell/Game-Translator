using GameTranslator.Application.Capture;
using GameTranslator.Application.Pipeline;

namespace GameTranslator.Tests.Application;

public sealed class MenuOcrSchedulerTests
{
    private static readonly Guid SourceId = Guid.Parse("56d877bc-e97a-4588-b799-4d842aa7f718");
    private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);
    private static CaptureFrameOrigin Origin(int milliseconds) => new(SourceId, Ms(milliseconds), DateTimeOffset.UnixEpoch);

    private static void Offer(MenuOcrScheduler<int> scheduler, string id, int time, bool changed = false)
        => Assert.Equal(MenuOcrScheduler<int>.OfferOutcome.Queued,
            scheduler.Offer(id, time, Origin(time), changed, Ms(time)));

    [Fact]
    public void MovingBackground_UsesBoundedWaitAndStaysPeriodic()
    {
        using var scheduler = new MenuOcrScheduler<int>();
        scheduler.Register("a");
        for (var time = 0; time < 2000; time += 100)
        {
            Offer(scheduler, "a", time, changed: true);
            Assert.Null(scheduler.TryStartNext(Ms(time)));
        }
        Offer(scheduler, "a", 2000, changed: true);
        var first = Assert.IsType<MenuOcrScheduler<int>.Lease>(scheduler.TryStartNext(Ms(2000)));
        Assert.True(scheduler.IsPeriodic("a"));
        Assert.Equal(2000, first.Work);
        Assert.True(scheduler.Complete(first));
        Offer(scheduler, "a", 2499, changed: true);
        Assert.Null(scheduler.TryStartNext(Ms(2499)));
        Offer(scheduler, "a", 2500, changed: true);
        var second = Assert.IsType<MenuOcrScheduler<int>.Lease>(scheduler.TryStartNext(Ms(2500)));
        Assert.True(scheduler.Complete(second));
        Offer(scheduler, "a", 3000);
        Assert.True(scheduler.IsPeriodic("a"));
        Assert.NotNull(scheduler.TryStartNext(Ms(3000)));
    }

    [Fact]
    public void PeriodicTransition_RequiresThreeDistinctChangedFrames()
    {
        using var scheduler = new MenuOcrScheduler<int>(visualQuietInterval: Ms(10000));
        scheduler.Register("a");
        Offer(scheduler, "a", 0);
        Offer(scheduler, "a", 1000, true);
        Offer(scheduler, "a", 2000, true);
        for (var i = 0; i < 100; i++)
            Assert.Equal(MenuOcrScheduler<int>.OfferOutcome.DuplicateOrOutOfOrder,
                scheduler.Offer("a", -1, Origin(2000), true, Ms(2000)));
        Assert.Null(scheduler.TryStartNext(Ms(2000)));
        Assert.False(scheduler.IsPeriodic("a"));
        Offer(scheduler, "a", 2100, true);
        Assert.True(scheduler.IsPeriodic("a"));
        Assert.NotNull(scheduler.TryStartNext(Ms(2100)));
    }

    [Fact]
    public async Task SlowOcr_IsNotCancelledByMotion_AndOnlyLatestFrameWaits()
    {
        using var scheduler = new MenuOcrScheduler<int>();
        scheduler.Register("a");
        Offer(scheduler, "a", 0);
        var lease = Assert.IsType<MenuOcrScheduler<int>.Lease>(scheduler.TryStartNext(Ms(200)));
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ocr = release.Task.WaitAsync(lease.CancellationToken);
        for (var time = 201; time <= 10000; time++)
        {
            Offer(scheduler, "a", time, true);
            Assert.Null(scheduler.TryStartNext(Ms(time)));
            Assert.Equal(1, scheduler.ActiveCount);
            Assert.Equal(1, scheduler.PendingCount);
        }
        Assert.False(lease.CancellationToken.IsCancellationRequested);
        Assert.False(ocr.IsCompleted);
        Assert.Equal(9799, scheduler.ReplacedPendingCount);
        release.SetResult(7);
        Assert.Equal(7, await ocr);
        Assert.True(scheduler.Complete(lease));
        var next = Assert.IsType<MenuOcrScheduler<int>.Lease>(scheduler.TryStartNext(Ms(10000)));
        Assert.Equal(10000, next.Work);
    }

    [Fact]
    public void StaticPixels_StillReceivePeriodicFreshObservations()
    {
        using var scheduler = new MenuOcrScheduler<int>();
        scheduler.Register("a");
        Offer(scheduler, "a", 0);
        Assert.Null(scheduler.TryStartNext(Ms(199)));
        var first = Assert.IsType<MenuOcrScheduler<int>.Lease>(scheduler.TryStartNext(Ms(200)));
        scheduler.Complete(first);
        for (var time = 700; time <= 10700; time += 500)
        {
            Offer(scheduler, "a", time);
            var lease = Assert.IsType<MenuOcrScheduler<int>.Lease>(scheduler.TryStartNext(Ms(time)));
            Assert.Equal(time, lease.Work);
            scheduler.Complete(lease);
        }
    }

    [Fact]
    public void SharedLimit_RetainsCancelledSlotsUntilActualCompletion_AndRejectsOldGeneration()
    {
        using var scheduler = new MenuOcrScheduler<int>();
        foreach (var id in new[] { "a", "b", "c", "d" })
        {
            scheduler.Register(id);
            Offer(scheduler, id, 0);
        }
        var leases = Enumerable.Range(0, 3).Select(_ => scheduler.TryStartNext(Ms(200))!).ToArray();
        Assert.Equal(new[] { "a", "b", "c" }, leases.Select(l => l.TrackId));
        Assert.Null(scheduler.TryStartNext(Ms(200)));
        scheduler.Remove("a");
        Assert.True(leases[0].CancellationToken.IsCancellationRequested);
        var newGeneration = scheduler.Register("a");
        Offer(scheduler, "a", 200);
        Assert.NotEqual(leases[0].Generation, newGeneration);
        Assert.Null(scheduler.TryStartNext(Ms(400)));
        Assert.Equal(3, scheduler.ActiveCount);
        Assert.False(scheduler.Complete(leases[0]));
        Assert.False(scheduler.Complete(leases[0]));
        Assert.Equal(2, scheduler.ActiveCount);
        Assert.Equal("d", scheduler.TryStartNext(Ms(400))!.TrackId);
        Assert.True(scheduler.Complete(leases[1]));
        Assert.Equal(newGeneration, scheduler.TryStartNext(Ms(400))!.Generation);
        Assert.False(leases[2].CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void ReplacingPending_DoesNotMoveTrackToBackOfQueue()
    {
        using var scheduler = new MenuOcrScheduler<int>(concurrency: 1);
        scheduler.Register("a");
        scheduler.Register("b");
        Offer(scheduler, "a", 0);
        Offer(scheduler, "b", 1);
        Offer(scheduler, "a", 100);
        var first = scheduler.TryStartNext(Ms(201))!;
        Assert.Equal("a", first.TrackId);
        Assert.Equal(100, first.Work);
        Offer(scheduler, "a", 701);
        scheduler.Complete(first);
        Assert.Equal("b", scheduler.TryStartNext(Ms(701))!.TrackId);
    }

    [Fact]
    public void M10_ContinuousLargeZoneCannotStarveSmallZoneWhileRetiredWorkHoldsSlot()
    {
        using var scheduler = new MenuOcrScheduler<int>();
        var ids = new[] { "retired-zone" }.Concat(Enumerable.Range(0, 20).Select(i => $"large-zone-{i}"))
            .Append("small-zone").ToArray();
        foreach (var id in ids) { scheduler.Register(id); Offer(scheduler, id, 0); }
        var held = scheduler.TryStartNext(Ms(200))!;
        Assert.Equal("retired-zone", held.TrackId);
        scheduler.Remove(held.TrackId);
        Assert.True(held.CancellationToken.IsCancellationRequested);
        var running = new List<MenuOcrScheduler<int>.Lease>();
        var last = new Dictionary<string, int>();
        var starts = ids.Skip(1).ToDictionary(id => id, _ => 0);
        for (var time = 250; time <= 120_000; time += 50)
        {
            foreach (var lease in running.Where(l => Ms(time) - l.StartedAt >= Ms(100)).ToArray())
            {
                Assert.True(scheduler.Complete(lease));
                running.Remove(lease);
            }
            if (time == 5000) Assert.False(scheduler.Complete(held));
            foreach (var id in ids.Skip(1)) Offer(scheduler, id, time, changed: true);
            while (scheduler.TryStartNext(Ms(time)) is { } next)
            {
                Assert.Equal(time, next.Work); // newest source, without losing queue position
                if (last.TryGetValue(next.TrackId, out var previous)) Assert.InRange(time - previous, 500, 1200);
                last[next.TrackId] = time;
                starts[next.TrackId]++;
                running.Add(next);
            }
            Assert.InRange(scheduler.ActiveCount, 0, 3);
            Assert.InRange(scheduler.PendingCount, 0, 21);
            if (time >= 3500) Assert.Equal(21, last.Count);
        }
        Assert.All(starts.Values, n => Assert.True(n >= 100));
        Assert.InRange(starts.Values.Max() - starts.Values.Min(), 0, 1);
        Assert.Equal(0, scheduler.ExpiredPendingCount);
        foreach (var lease in running) Assert.True(scheduler.Complete(lease));
        foreach (var id in ids.Skip(1)) scheduler.Remove(id);
        Assert.Equal(0, scheduler.ActiveCount);
        Assert.Equal(0, scheduler.PendingCount);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(2000, true)]
    [InlineData(2001, false)]
    public void SourceAge_IsBounded(int now, bool accepted)
    {
        using var scheduler = new MenuOcrScheduler<int>();
        scheduler.Register("a");
        Assert.Equal(accepted ? MenuOcrScheduler<int>.OfferOutcome.Queued : MenuOcrScheduler<int>.OfferOutcome.ExpiredOrFuture,
            scheduler.Offer("a", 0, Origin(0), false, Ms(now)));
    }

    [Fact]
    public void CachedAndOutOfOrderFrames_DoNotCreateWorkOrReplaceLatest()
    {
        using var scheduler = new MenuOcrScheduler<int>();
        scheduler.Register("a");
        Offer(scheduler, "a", 100);
        Assert.Equal(MenuOcrScheduler<int>.OfferOutcome.DuplicateOrOutOfOrder,
            scheduler.Offer("a", 200, Origin(100) with { CapturedAt = DateTimeOffset.MaxValue }, true, Ms(200)));
        Assert.Equal(MenuOcrScheduler<int>.OfferOutcome.DuplicateOrOutOfOrder,
            scheduler.Offer("a", 300, Origin(50), true, Ms(300)));
        var lease = scheduler.TryStartNext(Ms(300))!;
        Assert.Equal(100, lease.Work);
        scheduler.Complete(lease);
        Assert.Null(scheduler.TryStartNext(Ms(800)));
    }

    [Fact]
    public void UnknownOrigin_DoesNotStartVisualWait_AndPendingExpiresBeforeStart()
    {
        using var scheduler = new MenuOcrScheduler<int>();
        scheduler.Register("a");
        Assert.Equal(MenuOcrScheduler<int>.OfferOutcome.UnknownOrigin,
            scheduler.Offer("a", 0, null, true, Ms(0)));
        Assert.Null(scheduler.TryStartNext(Ms(10000)));
        Offer(scheduler, "a", 10000);
        Assert.Null(scheduler.TryStartNext(Ms(10000)));
        Assert.Null(scheduler.TryStartNext(Ms(12001)));
        Assert.Equal(0, scheduler.PendingCount);
        Assert.Equal(1, scheduler.ExpiredPendingCount);
    }

    [Fact]
    public void LeadingCompositorTimestamp_DoesNotExtendAgePastActualArrival()
    {
        using var scheduler = new MenuOcrScheduler<int>();
        scheduler.Register("a");
        var origin = Origin(10) with { ObservedAtTimestamp = Ms(0) };
        Assert.Equal(MenuOcrScheduler<int>.OfferOutcome.Queued, scheduler.Offer("a", 0, origin, false, Ms(0)));
        Assert.Null(scheduler.TryStartNext(Ms(2001)));
        Assert.Equal(1, scheduler.ExpiredPendingCount);
    }

    [Fact]
    public void SourceSwitch_InvalidatesTrackAndCancelsOnlyItsWork()
    {
        using var scheduler = new MenuOcrScheduler<int>();
        scheduler.Register("a");
        Offer(scheduler, "a", 0);
        var lease = scheduler.TryStartNext(Ms(200))!;
        Assert.Equal(MenuOcrScheduler<int>.OfferOutcome.ContextChanged,
            scheduler.Offer("a", 300, Origin(300) with { SourceId = Guid.NewGuid() }, false, Ms(300)));
        Assert.True(lease.CancellationToken.IsCancellationRequested);
        Assert.False(scheduler.Complete(lease));
        Assert.Equal(0, scheduler.PendingCount);
    }

    [Fact]
    public void Dispose_CancelsWorkAndDropsPending_ButCompletionStillDrainsSlots()
    {
        var scheduler = new MenuOcrScheduler<int>();
        scheduler.Register("a");
        Offer(scheduler, "a", 0);
        var lease = scheduler.TryStartNext(Ms(200))!;
        Offer(scheduler, "a", 300);
        scheduler.Dispose();
        scheduler.Dispose();
        Assert.True(lease.CancellationToken.IsCancellationRequested);
        Assert.Equal(0, scheduler.PendingCount);
        Assert.Equal(1, scheduler.ActiveCount);
        Assert.False(scheduler.Complete(lease));
        Assert.Equal(0, scheduler.ActiveCount);
        Assert.Throws<ObjectDisposedException>(() => scheduler.TryStartNext(Ms(500)));
    }

    [Fact]
    public void StricterVisualQuietInterval_IsHonored_AndClockCannotGoBackwards()
    {
        using var scheduler = new MenuOcrScheduler<int>(visualQuietInterval: Ms(800));
        scheduler.Register("a");
        Offer(scheduler, "a", 0);
        Assert.Null(scheduler.TryStartNext(Ms(799)));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.TryStartNext(Ms(798)));
        Assert.NotNull(scheduler.TryStartNext(Ms(800)));
    }
}
