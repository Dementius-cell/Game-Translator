namespace GameTranslator.Application.Pipeline;

public sealed class TranslationPipelineRunOptions
{
    private TimeSpan minimumCandidateGroupingDuration;

    public static TimeSpan DefaultStableTextInterval { get; } = TimeSpan.FromSeconds(1);

    public static TimeSpan DefaultMinimumCandidateOverlayVisibleDuration { get; } = TimeSpan.FromSeconds(2);

    public const int DefaultCandidateTranslationMaxParallelism = 3;

    public const int DefaultMinimumCandidateGroupingObservations = 2;

    public const int DefaultMinimumStableTextObservations = 1;

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
        TimeSpan? minimumCandidateOverlayVisibleDuration = null,
        int candidateTranslationMaxParallelism = DefaultCandidateTranslationMaxParallelism,
        int minimumCandidateGroupingObservations = DefaultMinimumCandidateGroupingObservations,
        int minimumStableTextObservations = DefaultMinimumStableTextObservations)
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

        if (minimumCandidateGroupingObservations is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumCandidateGroupingObservations),
                "Minimum candidate grouping observations must be 1 through 8.");
        }

        if (minimumStableTextObservations is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumStableTextObservations),
                "Minimum stable text observations must be 1 through 8.");
        }

        RequireStableTextBeforeTranslation = requireStableTextBeforeTranslation;
        StableTextInterval = effectiveStableTextInterval;
        PreservePreviousOverlayWhileWaitingForStableText = preservePreviousOverlayWhileWaitingForStableText;
        RestorePreviousOverlayAfterCapture = restorePreviousOverlayAfterCapture;
        EnableCandidateDetectorPilot = enableCandidateDetectorPilot;
        MinimumCandidateOverlayVisibleDuration = effectiveMinimumCandidateOverlayVisibleDuration;
        CandidateTranslationMaxParallelism = candidateTranslationMaxParallelism;
        MinimumCandidateGroupingObservations = minimumCandidateGroupingObservations;
        MinimumStableTextObservations = minimumStableTextObservations;
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
    /// Conditional readability grace after a candidate overlay is published.
    /// Safety invalidation still removes the overlay immediately.
    /// </summary>
    public TimeSpan MinimumCandidateOverlayVisibleDuration { get; }

    /// <summary>
    /// Shared upper bound for cache-miss candidate translations in one session.
    /// </summary>
    public int CandidateTranslationMaxParallelism { get; }

    /// <summary>
    /// Consecutive matching detector-group observations required before crop OCR starts.
    /// </summary>
    public int MinimumCandidateGroupingObservations { get; }

    /// <summary>
    /// Minimum wall-clock duration covered by matching detector-group observations before crop OCR starts.
    /// This prevents a faster detector cadence from shortening the effective grouping-stability window.
    /// </summary>
    public TimeSpan MinimumCandidateGroupingDuration
    {
        get => minimumCandidateGroupingDuration;
        init
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MinimumCandidateGroupingDuration),
                    "Minimum candidate grouping duration must not be negative.");
            }

            minimumCandidateGroupingDuration = value;
        }
    }

    /// <summary>
    /// Consecutive matching normalized OCR observations required before translation starts.
    /// </summary>
    public int MinimumStableTextObservations { get; }

}
