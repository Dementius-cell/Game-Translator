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
