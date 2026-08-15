namespace GameTranslator.Application.Pipeline;

/// <summary>
/// Observable state for the accepted candidate-region pipeline.
/// It is intentionally session-scoped and does not change profile/default state.
/// </summary>
public sealed class CandidatePipelineReadiness
{
    public static CandidatePipelineReadiness Disabled { get; } = new(
        CandidatePipelineReadinessStatus.Disabled,
        generation: 0,
        restartCount: 0,
        unavailableReason: null);

    public CandidatePipelineReadiness(
        CandidatePipelineReadinessStatus status,
        long generation,
        int restartCount,
        string? unavailableReason,
        DateTimeOffset? nextRetryAt = null)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (generation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation));
        }

        if (restartCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(restartCount));
        }

        Status = status;
        Generation = generation;
        RestartCount = restartCount;
        UnavailableReason = string.IsNullOrWhiteSpace(unavailableReason)
            ? null
            : unavailableReason.Trim();
        NextRetryAt = nextRetryAt;
    }

    public CandidatePipelineReadinessStatus Status { get; }

    /// <summary>
    /// Increments only after a successful detector/provider prewarm.
    /// Results from an invalidated generation must not be published.
    /// </summary>
    public long Generation { get; }

    public int RestartCount { get; }

    public string? UnavailableReason { get; }

    /// <summary>
    /// Next bounded re-prewarm attempt, or <see langword="null"/> when ready or exhausted.
    /// </summary>
    public DateTimeOffset? NextRetryAt { get; }

    public bool IsReady => Status == CandidatePipelineReadinessStatus.Ready;
}

public enum CandidatePipelineReadinessStatus
{
    Disabled,
    Prewarming,
    Ready,
    Degraded,
}
