using GameTranslator.Application.Content;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class ContentLayoutPolicyTests
{
    [Fact]
    public void Resolve_DialogComic_ReturnsCurrentCandidatePipelinePolicy()
    {
        var policy = ContentLayoutPolicyResolver.Resolve(ContentLayoutMode.DialogComic);

        Assert.Equal(ContentLayoutMode.DialogComic, policy.Mode);
        Assert.Equal(ContentCandidateGroupingPolicy.BoundedWritingSystem, policy.CandidateGrouping);
        Assert.Equal(OverlayTextLayoutMode.ExpandFromSourceCenter, policy.CandidateOverlayLayout);
        Assert.Equal(TimeSpan.Zero, policy.MinimumLiveRefreshInterval);
    }

    [Fact]
    public void IsLiveRefreshDue_WhenIntervalIsZero_RefreshesEveryCycle()
    {
        var policy = ContentLayoutPolicyResolver.Resolve(ContentLayoutMode.DialogComic);
        var now = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

        Assert.True(policy.IsLiveRefreshDue(now, now));
    }

    [Fact]
    public void IsLiveRefreshDue_WithNonZeroInterval_ProvidesFutureStaticContentCadenceSeam()
    {
        var policy = new ContentLayoutPolicy(
            ContentLayoutMode.DialogComic,
            ContentCandidateGroupingPolicy.BoundedWritingSystem,
            OverlayTextLayoutMode.ExpandFromSourceCenter,
            TimeSpan.FromSeconds(2));
        var lastRefreshAt = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

        Assert.False(policy.IsLiveRefreshDue(lastRefreshAt, lastRefreshAt.AddSeconds(1)));
        Assert.True(policy.IsLiveRefreshDue(lastRefreshAt, lastRefreshAt.AddSeconds(2)));
    }

    [Fact]
    public void Resolve_WhenModeIsUnsupported_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ContentLayoutPolicyResolver.Resolve((ContentLayoutMode)999));
    }
}
