using System.Text.Json;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Pipeline;
using Xunit.Abstractions;

namespace GameTranslator.Tests.Application;

public sealed class MenuOcrLongRunTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(80, true)]
    [InlineData(700, false)]
    [InlineData(2500, false)]
    public void OneHourTwelveMovingAreas_RemainsBoundedAndNeverExtendsStaleAuthority(int ocrDurationMs, bool canSustainFreshness)
    {
        const int areas = 12, durationMs = 3_600_000, stepMs = 50;
        var ids = Enumerable.Range(0, areas).Select(i => i.ToString()).ToArray();
        var gates = ids.Select(_ => new MenuTextAuthority(2, TimeSpan.FromMilliseconds(200))).ToArray();
        var starts = new int[areas];
        var published = new bool[areas];
        var previousStart = Enumerable.Repeat(-500, areas).ToArray();
        var work = new List<(MenuOcrScheduler<int>.Lease Lease, int Due)>();
        var sourceId = Guid.NewGuid();
        using var scheduler = new MenuOcrScheduler<int>();
        foreach (var id in ids) scheduler.Register(id);
        int maxActive = 0, maxPending = 0, completed = 0, rejected = 0, accepted = 0, observedFresh = 0;
        double maximumAcceptedAge = 0;
        for (var time = 0; time <= durationMs; time += stepMs)
        {
            var now = TimeSpan.FromMilliseconds(time);
            for (var index = work.Count - 1; index >= 0; index--)
            {
                var running = work[index];
                if (running.Due > time) continue;
                work.RemoveAt(index);
                Assert.True(scheduler.Complete(running.Lease));
                completed++;
                var area = running.Lease.Work;
                if (gates[area].Observe(running.Lease.Origin, [$"Action {area}"], now))
                {
                    accepted++;
                    maximumAcceptedAge = Math.Max(maximumAcceptedAge, (now - running.Lease.Origin.FreshnessTimestamp).TotalMilliseconds);
                }
                else rejected++;
                published[area] |= gates[area].CanPublish(gates[area].Version, now);
            }
            var origin = new CaptureFrameOrigin(sourceId, now, DateTimeOffset.UnixEpoch.Add(now));
            for (var area = 0; area < areas; area++)
            {
                gates[area].Expire(now);
                if (gates[area].CanPublish(gates[area].Version, now)) observedFresh++;
                scheduler.Offer(ids[area], area, origin, pixelsChanged: true, now);
            }
            while (scheduler.TryStartNext(now) is { } lease)
            {
                var area = lease.Work;
                Assert.True(time - previousStart[area] >= 500);
                previousStart[area] = time;
                starts[area]++;
                work.Add((lease, time + ocrDurationMs));
            }
            maxActive = Math.Max(maxActive, scheduler.ActiveCount);
            maxPending = Math.Max(maxPending, scheduler.PendingCount);
        }
        Assert.Equal(3, maxActive);
        Assert.InRange(maxPending, 1, areas);
        Assert.All(starts, count => Assert.True(count > 100));
        Assert.All(published, value => Assert.Equal(canSustainFreshness, value));
        Assert.InRange(maximumAcceptedAge, 0, 2000);
        if (ocrDurationMs > 2000) { Assert.Equal(0, accepted); Assert.Equal(completed, rejected); }
        else Assert.Equal(0, rejected);
        scheduler.Dispose();
        foreach (var running in work)
        {
            Assert.True(running.Lease.CancellationToken.IsCancellationRequested);
            Assert.False(scheduler.Complete(running.Lease));
        }
        Assert.Equal(0, scheduler.ActiveCount);
        Assert.Equal(0, scheduler.PendingCount);
        output.WriteLine(JsonSerializer.Serialize(new { virtualSeconds = durationMs / 1000, areas, ocrDurationMs,
            starts = starts.Sum(), perAreaMin = starts.Min(), perAreaMax = starts.Max(), completed, accepted, rejected,
            maxActive, maxPending, scheduler.ReplacedPendingCount, maximumAcceptedAge, observedFresh, canSustainFreshness }));
    }
}
