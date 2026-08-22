using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Content;

/// <summary>
/// Selects how transient candidates are grouped inside a saved OCR zone.
/// </summary>
public enum ContentCandidateGroupingPolicy
{
    BoundedWritingSystem = 0,
}

/// <summary>
/// Coordinates the Application behavior owned by one content layout mode.
/// </summary>
public sealed class ContentLayoutPolicy
{
    public ContentLayoutPolicy(
        ContentLayoutMode mode,
        ContentCandidateGroupingPolicy candidateGrouping,
        OverlayTextLayoutMode candidateOverlayLayout,
        TimeSpan minimumLiveRefreshInterval)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (!Enum.IsDefined(candidateGrouping))
        {
            throw new ArgumentOutOfRangeException(nameof(candidateGrouping));
        }

        if (!Enum.IsDefined(candidateOverlayLayout))
        {
            throw new ArgumentOutOfRangeException(nameof(candidateOverlayLayout));
        }

        if (minimumLiveRefreshInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumLiveRefreshInterval),
                "Minimum live refresh interval must not be negative.");
        }

        Mode = mode;
        CandidateGrouping = candidateGrouping;
        CandidateOverlayLayout = candidateOverlayLayout;
        MinimumLiveRefreshInterval = minimumLiveRefreshInterval;
    }

    public ContentLayoutMode Mode { get; }

    public ContentCandidateGroupingPolicy CandidateGrouping { get; }

    public OverlayTextLayoutMode CandidateOverlayLayout { get; }

    public TimeSpan MinimumLiveRefreshInterval { get; }

    public bool IsLiveRefreshDue(DateTimeOffset? lastRefreshAt, DateTimeOffset now)
    {
        return lastRefreshAt is null
            || MinimumLiveRefreshInterval == TimeSpan.Zero
            || now < lastRefreshAt.Value
            || now - lastRefreshAt.Value >= MinimumLiveRefreshInterval;
    }
}

/// <summary>
/// Resolves the coordinated policy for a saved per-zone content layout mode.
/// </summary>
public static class ContentLayoutPolicyResolver
{
    private static readonly ContentLayoutPolicy DialogComicPolicy = new(
        ContentLayoutMode.DialogComic,
        ContentCandidateGroupingPolicy.BoundedWritingSystem,
        OverlayTextLayoutMode.ExpandFromSourceCenter,
        TimeSpan.Zero);

    public static ContentLayoutPolicy Resolve(ContentLayoutMode mode)
    {
        return mode switch
        {
            ContentLayoutMode.DialogComic => DialogComicPolicy,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Content layout mode is not supported."),
        };
    }
}
