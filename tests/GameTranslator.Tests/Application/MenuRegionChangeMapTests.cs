using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;

namespace GameTranslator.Tests.Application;

public sealed class MenuRegionChangeMapTests
{
    private static readonly Guid Source = Guid.NewGuid();
    private static CapturedFrame Frame(int ms, Action<byte[]>? edit = null, Guid? source = null, bool cached = false)
    {
        var data = Enumerable.Repeat((byte)42, 1024 * 512 * 4).ToArray();
        edit?.Invoke(data);
        return new(new CaptureRegion(0, 0, 1024, 512), 1024, 512, 4096, "Bgra32", data,
            DateTimeOffset.UnixEpoch, new(source ?? Source, TimeSpan.FromMilliseconds(ms), DateTimeOffset.UnixEpoch), cached);
    }
    private static void Pixel(byte[] data, int x, int y) => data[(y * 1024 + x) * 4]++;
    private static MenuRegionChangeMap Initialized()
    {
        var map = new MenuRegionChangeMap(); var first = Frame(0);
        map.Commit(first, map.Plan(first, [], TimeSpan.Zero), true);
        return map;
    }

    [Fact]
    public void KnownCachedSourceSkipsDetectionWithoutMovingAuditOrLosingBaseline()
    {
        var map = Initialized();
        var cached = Frame(0, cached: true);
        var repeat = map.Plan(cached, [], TimeSpan.FromMilliseconds(500));
        Assert.Equal("cached-source-reused", repeat.Reason);
        Assert.False(repeat.FullScan);
        Assert.Empty(repeat.Areas);
        map.Commit(cached, repeat, false);
        Assert.Equal("local-change", map.Plan(Frame(700, p => Pixel(p, 100, 100)), [], TimeSpan.FromMilliseconds(700)).Reason);
        Assert.Equal("periodic-audit", map.Plan(cached, [], TimeSpan.FromSeconds(2)).Reason);
        Assert.Equal("untrusted-source", map.Plan(cached, [], TimeSpan.FromSeconds(3)).Reason);
    }

    [Theory]
    [InlineData("pixels")]
    [InlineData("source")]
    [InlineData("timestamp")]
    [InlineData("reset")]
    public void CachedReuseRequiresExactSuccessfullyProcessedSource(string change)
    {
        var map = Initialized();
        if (change == "reset") map.Reset();
        var cached = Frame(change == "timestamp" ? 100 : 0,
            p => { if (change == "pixels") Pixel(p, 100, 100); },
            source: change == "source" ? Guid.NewGuid() : null, cached: true);
        var plan = map.Plan(cached, [], TimeSpan.FromMilliseconds(500));
        Assert.Equal("untrusted-source", plan.Reason);
        Assert.Equal(change switch
        {
            "pixels" => "cached-pixels-mismatch",
            "source" => "cached-source-mismatch",
            "timestamp" => "cached-timestamp-mismatch",
            _ => "cached-no-baseline",
        }, plan.Detail);
    }

    [Fact]
    public void DiagnosticsDistinguishExpiredSourceAndBaselineDiscardFromConfirmationReset()
    {
        var map = Initialized();
        Assert.Equal("available", map.BaselineState);
        var frame = Frame(0, cached: true);
        var expired = map.Plan(frame, [], TimeSpan.FromSeconds(3));
        Assert.Equal("expired-source", expired.Detail);
        map.Commit(frame, expired, true);
        Assert.Equal("discarded-after-full-scan", map.BaselineState);
        Assert.Equal("cached-no-baseline", map.Plan(Frame(4000, cached: true), [], TimeSpan.FromSeconds(4)).Detail);
        map.Reset("no-confirmed-translation");
        Assert.Equal("no-confirmed-translation", map.BaselineState);
        Assert.Equal("future-source", map.Plan(Frame(5000), [], TimeSpan.FromSeconds(4)).Detail);
    }

    [Fact]
    public void FullDetectionOfNewCachedSourceEstablishesReusableBaselineOnlyAfterCommit()
    {
        var map = Initialized();
        var frame = Frame(500, cached: true);
        var plan = map.Plan(frame, [], TimeSpan.FromMilliseconds(600));
        Assert.True(plan.FullScan);
        Assert.Equal("cached-timestamp-mismatch", plan.Detail);
        Assert.True(map.Plan(frame, [], TimeSpan.FromMilliseconds(650)).FullScan);
        map.Commit(frame, plan, true);
        Assert.Equal("available", map.BaselineState);
        Assert.Equal("cached-source-reused", map.Plan(frame, [], TimeSpan.FromMilliseconds(700)).Reason);
        Assert.Equal("local-change", map.Plan(Frame(800, p => Pixel(p, 100, 100)), [], TimeSpan.FromMilliseconds(800)).Reason);
        Assert.Equal("untrusted-source", map.Plan(frame, [], TimeSpan.FromSeconds(3)).Reason);
    }

    [Fact]
    public void CachedBootstrapDoesNotAuthorizeRegressedOrInconsistentSource()
    {
        var map = new MenuRegionChangeMap();
        var frame = Frame(500, cached: true);
        var plan = map.Plan(frame, [], TimeSpan.FromMilliseconds(600));
        Assert.Equal("cached-no-baseline", plan.Detail);
        map.Commit(frame, plan, true);
        var regressed = map.Plan(Frame(400, cached: true), [], TimeSpan.FromMilliseconds(700));
        Assert.Equal("cached-source-regressed", regressed.Detail);
        Assert.Null(regressed.Hashes);
        Assert.Null(map.Plan(Frame(500, p => Pixel(p, 100, 100), cached: true), [], TimeSpan.FromMilliseconds(700)).Hashes);
        Assert.Null(map.Plan(Frame(500, p => Pixel(p, 100, 100), cached: true), [], TimeSpan.FromMilliseconds(2500)).Hashes);
        map.Reset("detector-unavailable");
        Assert.True(map.Plan(frame, [], TimeSpan.FromMilliseconds(700)).FullScan);
    }

    [Fact]
    public void SeparateChangesStaySeparateAndExpandToWholeTextBlocks()
    {
        var map = Initialized();
        var block = new BoundingBox(90, 90, 250, 28);
        var frame = Frame(500, p => { Pixel(p, 100, 100); Pixel(p, 800, 400); });
        var plan = map.Plan(frame, [block], TimeSpan.FromMilliseconds(500));
        Assert.False(plan.FullScan);
        Assert.Equal(2, plan.ChangedTiles);
        Assert.Equal(2, plan.Areas.Length);
        var left = plan.Areas.Single(a => a.X < 400);
        Assert.True(left.X <= block.X && left.Right >= block.Right && left.Bottom >= block.Bottom);
        Assert.DoesNotContain(plan.Areas, a => a.X < 400 && a.Right > 700);
    }

    [Fact]
    public void OnePixelIncludingLastPixelCannotBeMissedBySampling()
    {
        var map = Initialized();
        var plan = map.Plan(Frame(500, p => Pixel(p, 1023, 511)), [], TimeSpan.FromMilliseconds(500));
        Assert.False(plan.FullScan);
        Assert.Equal(1, plan.ChangedTiles);
        Assert.Equal(1024, Assert.Single(plan.Areas).Right);
    }

    [Fact]
    public void OverlappingExpansionMergesWithoutSplittingAnExistingParagraph()
    {
        var map = Initialized();
        var paragraph = new BoundingBox(90, 90, 300, 40);
        var plan = map.Plan(Frame(500, p => { Pixel(p, 100, 100); Pixel(p, 350, 100); }),
            [paragraph], TimeSpan.FromMilliseconds(500));
        var area = Assert.Single(plan.Areas);
        Assert.False(plan.FullScan);
        Assert.True(area.X < paragraph.X && area.Right > paragraph.Right);
    }

    [Fact]
    public void HashingHonorsCancellationWithoutReplacingBaseline()
    {
        var map = Initialized();
        Assert.Throws<OperationCanceledException>(() => map.Plan(Frame(500), [], TimeSpan.FromMilliseconds(500),
            new CancellationToken(true)));
        Assert.Equal("unchanged", map.Plan(Frame(600), [], TimeSpan.FromMilliseconds(600)).Reason);
    }

    [Theory]
    [InlineData("cached", "untrusted-source")]
    [InlineData("context", "initial-or-context")]
    [InlineData("duplicate", "source-gap")]
    [InlineData("gap", "source-gap")]
    [InlineData("audit", "periodic-audit")]
    public void UnsafeProvenanceAndAuditRequireFullDetection(string mode, string reason)
    {
        var map = Initialized();
        var ms = mode == "gap" ? 2500 : mode == "audit" ? 2000 : mode == "duplicate" ? 0 : 500;
        var plan = map.Plan(Frame(ms, source: mode == "context" ? Guid.NewGuid() : null, cached: mode == "cached"),
            [], TimeSpan.FromMilliseconds(ms));
        Assert.True(plan.FullScan);
        Assert.Equal(reason, plan.Reason);
    }

    [Fact]
    public void BroadMovingBandIsClassifiedButNeverIgnoredEvenWithChangedLetter()
    {
        var map = Initialized();
        var plan = map.Plan(Frame(500, p =>
        {
            for (var y = 200; y < 220; y++) for (var x = 0; x < 1024; x++) Pixel(p, x, y);
            Pixel(p, 100, 100); // A simultaneous letter change must not be concealed by the band.
        }), [], TimeSpan.FromMilliseconds(500));
        Assert.True(plan.FullScan);
        Assert.Equal("broad-change", plan.Reason);
        Assert.True(plan.ChangedTiles >= 17);
    }

    [Fact]
    public void SmallChangesAreComparedToLastSuccessfullyProcessedFrame()
    {
        var map = Initialized();
        var f = Frame(500);
        var plan = map.Plan(f, [], TimeSpan.FromMilliseconds(500));
        Assert.Empty(plan.Areas);
        map.Commit(f, plan, false);
        var changed = map.Plan(Frame(700, p => Pixel(p, 100, 100)), [], TimeSpan.FromMilliseconds(700));
        Assert.Single(changed.Areas);
        map.Reset();
        Assert.True(map.Plan(f, [], TimeSpan.FromMilliseconds(800)).FullScan);
    }
}
