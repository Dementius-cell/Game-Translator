namespace GameTranslator.Application.Pipeline;

public sealed class TranslationPipelineRunOptions
{
    public static TimeSpan DefaultStableTextInterval { get; } = TimeSpan.FromSeconds(1);

    public static TimeSpan DefaultMinimumCandidateOverlayVisibleDuration { get; } = TimeSpan.FromSeconds(2);

    public const int DefaultCandidateTranslationMaxParallelism = 3;

    public const int DefaultCandidatePrewarmMaximumAttempts = 3;

    public static TimeSpan DefaultCandidatePrewarmInitialRetryDelay { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Default product path: transient GPU-detected regions, bounded grouping and Tesseract crop recognition.
    /// </summary>
    public static TranslationPipelineRunOptions Default { get; } = new(
        enableCandidateDetectorPilot: true);

    /// <summary>
    /// Explicit diagnostic/compatibility path for tests and controlled troubleshooting only.
    /// Normal product entry points must use <see cref="Default"/>.
    /// </summary>
    public static TranslationPipelineRunOptions LegacyFullPage { get; } = new(
        enableCandidateDetectorPilot: false);

    public TranslationPipelineRunOptions(
        bool requireStableTextBeforeTranslation = false,
        TimeSpan? stableTextInterval = null,
        bool preservePreviousOverlayWhileWaitingForStableText = false,
        bool restorePreviousOverlayAfterCapture = false,
        bool enableCandidateDetectorPilot = true,
        bool requireCandidateReadinessBarrier = false,
        TimeSpan? minimumCandidateOverlayVisibleDuration = null,
        int candidateTranslationMaxParallelism = DefaultCandidateTranslationMaxParallelism,
        int candidatePrewarmMaximumAttempts = DefaultCandidatePrewarmMaximumAttempts,
        TimeSpan? candidatePrewarmInitialRetryDelay = null)
    {
        var effectiveStableTextInterval = stableTextInterval ?? DefaultStableTextInterval;
        if (effectiveStableTextInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stableTextInterval),
                "Stable text interval must not be negative.");
        }

        var effectiveMinimumCandidateOverlayVisibleDuration = minimumCandidateOverlayVisibleDuration
            ?? DefaultMinimumCandidateOverlayVisibleDuration;
        if (effectiveMinimumCandidateOverlayVisibleDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumCandidateOverlayVisibleDuration),
                "Minimum candidate overlay visible duration must not be negative.");
        }

        if (candidateTranslationMaxParallelism is < 1 or > DefaultCandidateTranslationMaxParallelism)
        {
            throw new ArgumentOutOfRangeException(
                nameof(candidateTranslationMaxParallelism),
                $"Candidate translation max parallelism must be 1 through {DefaultCandidateTranslationMaxParallelism}.");
        }

        if (candidatePrewarmMaximumAttempts is < 1 or > DefaultCandidatePrewarmMaximumAttempts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(candidatePrewarmMaximumAttempts),
                $"Candidate prewarm maximum attempts must be 1 through {DefaultCandidatePrewarmMaximumAttempts}.");
        }

        var effectiveCandidatePrewarmInitialRetryDelay = candidatePrewarmInitialRetryDelay
            ?? DefaultCandidatePrewarmInitialRetryDelay;
        if (effectiveCandidatePrewarmInitialRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(candidatePrewarmInitialRetryDelay),
                "Candidate prewarm initial retry delay must not be negative.");
        }

        RequireStableTextBeforeTranslation = requireStableTextBeforeTranslation;
        StableTextInterval = effectiveStableTextInterval;
        PreservePreviousOverlayWhileWaitingForStableText = preservePreviousOverlayWhileWaitingForStableText;
        RestorePreviousOverlayAfterCapture = restorePreviousOverlayAfterCapture;
        EnableCandidateDetectorPilot = enableCandidateDetectorPilot;
        RequireCandidateReadinessBarrier = requireCandidateReadinessBarrier;
        MinimumCandidateOverlayVisibleDuration = effectiveMinimumCandidateOverlayVisibleDuration;
        CandidateTranslationMaxParallelism = candidateTranslationMaxParallelism;
        CandidatePrewarmMaximumAttempts = candidatePrewarmMaximumAttempts;
        CandidatePrewarmInitialRetryDelay = effectiveCandidatePrewarmInitialRetryDelay;
    }

    public bool RequireStableTextBeforeTranslation { get; }

    public TimeSpan StableTextInterval { get; }

    public bool PreservePreviousOverlayWhileWaitingForStableText { get; }

    public bool RestorePreviousOverlayAfterCapture { get; }

    /// <summary>
    /// Uses the transient candidate-region pipeline for this in-memory run.
    /// The historical name is retained for source compatibility.
    /// </summary>
    public bool EnableCandidateDetectorPilot { get; }

    /// <summary>
    /// ADR-028 policy: live candidate work starts only after an asynchronous
    /// detector plus direct-provider prewarm has succeeded.
    /// </summary>
    public bool RequireCandidateReadinessBarrier { get; }

    /// <summary>
    /// Conditional readability grace after a candidate overlay is published.
    /// Safety invalidation still removes the overlay immediately.
    /// </summary>
    public TimeSpan MinimumCandidateOverlayVisibleDuration { get; }

    /// <summary>
    /// Shared upper bound for cache-miss candidate translations in one session.
    /// </summary>
    public int CandidateTranslationMaxParallelism { get; }

    /// <summary>
    /// Policy-limited number of prewarm attempts before a live session remains degraded.
    /// </summary>
    public int CandidatePrewarmMaximumAttempts { get; }

    /// <summary>
    /// Initial delay for the bounded exponential prewarm recovery policy.
    /// </summary>
    public TimeSpan CandidatePrewarmInitialRetryDelay { get; }
}
