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

    /// <summary>
    /// Candidate work starts immediately for the session; this is diagnostics-only state,
    /// not a provider or detector preflight barrier.
    /// </summary>
    public static CandidatePipelineReadiness Active { get; } = new(
        CandidatePipelineReadinessStatus.Ready,
        generation: 1,
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
    /// Identifies an active candidate session for diagnostics.
    /// </summary>
    public long Generation { get; }

    public int RestartCount { get; }

    public string? UnavailableReason { get; }

    /// <summary>
    /// Reserved for non-blocking diagnostics. Candidate work has no retry barrier.
    /// </summary>
    public DateTimeOffset? NextRetryAt { get; }

    public bool IsReady => Status == CandidatePipelineReadinessStatus.Ready;
}

public enum CandidatePipelineReadinessStatus
{
    Disabled,
    Ready,
}
