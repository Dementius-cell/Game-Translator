using GameTranslator.Application.Ocr;

namespace GameTranslator.Application.Pipeline;

/// <summary>
/// Privacy-safe event emitted by a live candidate-region session. The event never contains OCR,
/// translated, provider-response, credential, or frame-pixel data.
/// </summary>
public sealed class LiveCandidateLifecycleEvent
{
    public LiveCandidateLifecycleEvent(
        long sequence,
        long refreshSequence,
        DateTimeOffset occurredAt,
        LiveCandidateLifecycleEventKind kind,
        string? zoneId = null,
        string? candidateId = null,
        BoundingBox? candidateBounds = null,
        IEnumerable<BoundingBox>? sourceCandidateBounds = null,
        int candidateRevision = 0,
        int workAttempt = 0,
        DateTimeOffset? frameCapturedAt = null,
        TimeSpan? elapsed = null,
        int? candidateCount = null,
        int? recognizedBlockCount = null,
        int? translationInputBlockCount = null,
        int? translatedBlockCount = null,
        TranslationPipelineTextStability? textStability = null,
        int? overlayTextItemCount = null,
        int? overlayMaskItemCount = null,
        TranslationPipelineStage? failureStage = null,
        LiveCandidateCancellationReason cancellationReason = LiveCandidateCancellationReason.None)
    {
        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (refreshSequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshSequence));
        }

        if (candidateRevision < 0 || workAttempt < 0)
        {
            throw new ArgumentOutOfRangeException(
                candidateRevision < 0 ? nameof(candidateRevision) : nameof(workAttempt));
        }

        ValidateNonNegative(candidateCount, nameof(candidateCount));
        ValidateNonNegative(recognizedBlockCount, nameof(recognizedBlockCount));
        ValidateNonNegative(translationInputBlockCount, nameof(translationInputBlockCount));
        ValidateNonNegative(translatedBlockCount, nameof(translatedBlockCount));
        ValidateNonNegative(overlayTextItemCount, nameof(overlayTextItemCount));
        ValidateNonNegative(overlayMaskItemCount, nameof(overlayMaskItemCount));

        Sequence = sequence;
        RefreshSequence = refreshSequence;
        OccurredAt = occurredAt;
        Kind = kind;
        ZoneId = string.IsNullOrWhiteSpace(zoneId) ? null : zoneId.Trim();
        CandidateId = string.IsNullOrWhiteSpace(candidateId) ? null : candidateId.Trim();
        CandidateBounds = candidateBounds;
        SourceCandidateBounds = (sourceCandidateBounds ?? Array.Empty<BoundingBox>()).ToArray();
        CandidateRevision = candidateRevision;
        WorkAttempt = workAttempt;
        FrameCapturedAt = frameCapturedAt;
        Elapsed = elapsed;
        CandidateCount = candidateCount;
        RecognizedBlockCount = recognizedBlockCount;
        TranslationInputBlockCount = translationInputBlockCount;
        TranslatedBlockCount = translatedBlockCount;
        TextStability = textStability;
        OverlayTextItemCount = overlayTextItemCount;
        OverlayMaskItemCount = overlayMaskItemCount;
        FailureStage = failureStage;
        CancellationReason = cancellationReason;
    }

    public long Sequence { get; }

    public long RefreshSequence { get; }

    public DateTimeOffset OccurredAt { get; }

    public LiveCandidateLifecycleEventKind Kind { get; }

    public string? ZoneId { get; }

    public string? CandidateId { get; }

    public BoundingBox? CandidateBounds { get; }

    public IReadOnlyList<BoundingBox> SourceCandidateBounds { get; }

    public int CandidateRevision { get; }

    public int WorkAttempt { get; }

    public DateTimeOffset? FrameCapturedAt { get; }

    public TimeSpan? Elapsed { get; }

    public int? CandidateCount { get; }

    public int? RecognizedBlockCount { get; }

    public int? TranslationInputBlockCount { get; }

    public int? TranslatedBlockCount { get; }

    public TranslationPipelineTextStability? TextStability { get; }

    public int? OverlayTextItemCount { get; }

    public int? OverlayMaskItemCount { get; }

    public TranslationPipelineStage? FailureStage { get; }

    public LiveCandidateCancellationReason CancellationReason { get; }

    private static void ValidateNonNegative(int? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public enum LiveCandidateLifecycleEventKind
{
    PreviousOverlayHiddenForCapture,
    CaptureStarted,
    CaptureCompleted,
    PreviousOverlayRestoredAfterCapture,
    CandidateDetectionStarted,
    CandidateDetectionCompleted,
    CandidateDiscovered,
    CandidateGroupingChanged,
    CandidateSourceChanged,
    CandidateWorkStarted,
    CandidateWorkCompleted,
    CandidateWorkDeferredForStability,
    CandidateWorkFailed,
    CandidateWorkCancelled,
    CandidateRemoved,
    OverlaySnapshotPublished,
}

public enum LiveCandidateCancellationReason
{
    None,
    CandidateSourceChanged,
    CandidateDisappeared,
    CaptureLost,
    CandidatePipelineDegraded,
}
