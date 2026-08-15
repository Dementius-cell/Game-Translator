namespace GameTranslator.Application.Pipeline;

public sealed class LiveTranslationPipelineUpdate
{
    public LiveTranslationPipelineUpdate(
        TranslationPipelineBatchResult batchResult,
        bool overlayChanged,
        IEnumerable<string>? cancelledZoneIds = null,
        CandidatePipelineReadiness? candidateReadiness = null)
    {
        BatchResult = batchResult ?? throw new ArgumentNullException(nameof(batchResult));
        OverlayChanged = overlayChanged;
        CancelledZoneIds = (cancelledZoneIds ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        CandidateReadiness = candidateReadiness ?? CandidatePipelineReadiness.Disabled;
    }

    public TranslationPipelineBatchResult BatchResult { get; }

    public bool OverlayChanged { get; }

    public IReadOnlyList<string> CancelledZoneIds { get; }

    /// <summary>
    /// Session-only readiness telemetry for the candidate-region policy.
    /// </summary>
    public CandidatePipelineReadiness CandidateReadiness { get; }
}
