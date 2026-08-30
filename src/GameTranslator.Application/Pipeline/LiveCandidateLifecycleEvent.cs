using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;
using System.Security.Cryptography;
using System.Text;

namespace GameTranslator.Application.Pipeline;

/// <summary>
/// Bounded diagnostic event emitted by a live candidate-region session. OCR, translation-input,
/// and translated text may be retained for the local live report; provider responses, credentials,
/// and frame pixels are never stored.
/// </summary>
public sealed class LiveCandidateLifecycleEvent
{
    private const int MaximumOrderedGeometryBounds = 128;
    private const int MaximumDiagnosticTextEntries = 16;
    private const int MaximumDiagnosticTextLength = 512;

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
        int? groupingObservationCount = null,
        int? requiredGroupingObservationCount = null,
        DateTimeOffset? groupingFirstObservedAt = null,
        DateTimeOffset? groupingLastObservedAt = null,
        TimeSpan? requiredGroupingDuration = null,
        int? translationMemoryCacheHitCount = null,
        int? translationPersistentCacheHitCount = null,
        int? translationCacheMissCount = null,
        int? translationCacheStoredCount = null,
        string? translationProviderId = null,
        DateTimeOffset? providerRequestStartedAt = null,
        DateTimeOffset? providerRequestCompletedAt = null,
        int? overlayTextItemCount = null,
        int? overlayMaskItemCount = null,
        TranslationPipelineStage? failureStage = null,
        string? failureExceptionType = null,
        string? failureExceptionMessage = null,
        string? failureRootCauseType = null,
        string? failureRootCauseMessage = null,
        LiveCandidateCancellationReason cancellationReason = LiveCandidateCancellationReason.None,
        int? translationOutputSanitizedCount = null,
        IEnumerable<BoundingBox>? orderedOcrBlockBounds = null,
        IEnumerable<BoundingBox>? orderedGroupedMemberBounds = null,
        WritingSystemGroupingProfile? writingSystemGroupingProfile = null,
        OcrOrientationMode? ocrOrientationMode = null,
        TextCandidateDetectorPreset? requestedDetectorPreset = null,
        TextCandidateDetectorPreset? effectiveDetectorPreset = null,
        double? detectorThreshold = null,
        double? detectorBoxThreshold = null,
        double? detectorUnclipRatio = null,
        int? rawDetectorCandidateCount = null,
        double? minimumDetectorConfidence = null,
        double? maximumDetectorConfidence = null,
        double? averageDetectorConfidence = null,
        IEnumerable<string>? ocrTexts = null,
        IEnumerable<string>? translationInputTexts = null,
        IEnumerable<string>? translatedTexts = null)
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
        ValidateNonNegative(groupingObservationCount, nameof(groupingObservationCount));
        ValidateNonNegative(requiredGroupingObservationCount, nameof(requiredGroupingObservationCount));
        if (groupingFirstObservedAt is null != groupingLastObservedAt is null)
        {
            throw new ArgumentException(
                "Grouping-stability timestamps must either both be present or both be absent.");
        }

        if (groupingFirstObservedAt is { } groupingFirst
            && groupingLastObservedAt is { } groupingLast
            && groupingLast < groupingFirst)
        {
            throw new ArgumentOutOfRangeException(
                nameof(groupingLastObservedAt),
                "The last grouping-stability observation cannot precede the first observation.");
        }

        if (requiredGroupingDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredGroupingDuration));
        }

        ValidateNonNegative(translationMemoryCacheHitCount, nameof(translationMemoryCacheHitCount));
        ValidateNonNegative(translationPersistentCacheHitCount, nameof(translationPersistentCacheHitCount));
        ValidateNonNegative(translationCacheMissCount, nameof(translationCacheMissCount));
        ValidateNonNegative(translationCacheStoredCount, nameof(translationCacheStoredCount));
        ValidateNonNegative(translationOutputSanitizedCount, nameof(translationOutputSanitizedCount));
        ValidateNonNegative(overlayTextItemCount, nameof(overlayTextItemCount));
        ValidateNonNegative(overlayMaskItemCount, nameof(overlayMaskItemCount));
        if (writingSystemGroupingProfile is { } groupingProfile && !Enum.IsDefined(groupingProfile))
        {
            throw new ArgumentOutOfRangeException(nameof(writingSystemGroupingProfile));
        }

        if (ocrOrientationMode is { } orientationMode && !Enum.IsDefined(orientationMode))
        {
            throw new ArgumentOutOfRangeException(nameof(ocrOrientationMode));
        }

        ValidateOptionalEnum(requestedDetectorPreset, nameof(requestedDetectorPreset));
        ValidateOptionalEnum(effectiveDetectorPreset, nameof(effectiveDetectorPreset));
        ValidatePositiveFinite(detectorThreshold, nameof(detectorThreshold));
        ValidatePositiveFinite(detectorBoxThreshold, nameof(detectorBoxThreshold));
        ValidatePositiveFinite(detectorUnclipRatio, nameof(detectorUnclipRatio));
        ValidateNonNegative(rawDetectorCandidateCount, nameof(rawDetectorCandidateCount));
        ValidateConfidence(minimumDetectorConfidence, nameof(minimumDetectorConfidence));
        ValidateConfidence(maximumDetectorConfidence, nameof(maximumDetectorConfidence));
        ValidateConfidence(averageDetectorConfidence, nameof(averageDetectorConfidence));

        var orderedOcrGeometry = CreateBoundedGeometryDiagnostics(orderedOcrBlockBounds);
        var orderedGroupedGeometry = CreateBoundedGeometryDiagnostics(orderedGroupedMemberBounds);
        var boundedOcrTexts = CreateBoundedTextDiagnostics(ocrTexts);
        var boundedTranslationInputTexts = CreateBoundedTextDiagnostics(translationInputTexts);
        var boundedTranslatedTexts = CreateBoundedTextDiagnostics(translatedTexts);

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
        GroupingObservationCount = groupingObservationCount;
        RequiredGroupingObservationCount = requiredGroupingObservationCount;
        GroupingFirstObservedAt = groupingFirstObservedAt;
        GroupingLastObservedAt = groupingLastObservedAt;
        RequiredGroupingDuration = requiredGroupingDuration;
        TranslationMemoryCacheHitCount = translationMemoryCacheHitCount;
        TranslationPersistentCacheHitCount = translationPersistentCacheHitCount;
        TranslationCacheMissCount = translationCacheMissCount;
        TranslationCacheStoredCount = translationCacheStoredCount;
        TranslationOutputSanitizedCount = translationOutputSanitizedCount;
        TranslationProviderId = NormalizeDiagnosticValue(translationProviderId, maximumLength: 128);
        ProviderRequestStartedAt = providerRequestStartedAt;
        ProviderRequestCompletedAt = providerRequestCompletedAt;
        OverlayTextItemCount = overlayTextItemCount;
        OverlayMaskItemCount = overlayMaskItemCount;
        FailureStage = failureStage;
        FailureExceptionType = NormalizeDiagnosticValue(failureExceptionType, maximumLength: 256);
        FailureExceptionMessage = NormalizeDiagnosticValue(failureExceptionMessage, maximumLength: 1_024);
        FailureRootCauseType = NormalizeDiagnosticValue(failureRootCauseType, maximumLength: 256);
        FailureRootCauseMessage = NormalizeDiagnosticValue(failureRootCauseMessage, maximumLength: 1_024);
        CancellationReason = cancellationReason;
        OrderedOcrBlockBounds = orderedOcrGeometry.Bounds;
        OrderedOcrBlockBoundsCount = orderedOcrGeometry.Count;
        OrderedOcrBlockBoundsFingerprint = orderedOcrGeometry.Fingerprint;
        OrderedGroupedMemberBounds = orderedGroupedGeometry.Bounds;
        OrderedGroupedMemberBoundsCount = orderedGroupedGeometry.Count;
        OrderedGroupedMemberBoundsFingerprint = orderedGroupedGeometry.Fingerprint;
        WritingSystemGroupingProfile = writingSystemGroupingProfile;
        OcrOrientationMode = ocrOrientationMode;
        RequestedDetectorPreset = requestedDetectorPreset;
        EffectiveDetectorPreset = effectiveDetectorPreset;
        DetectorThreshold = detectorThreshold;
        DetectorBoxThreshold = detectorBoxThreshold;
        DetectorUnclipRatio = detectorUnclipRatio;
        RawDetectorCandidateCount = rawDetectorCandidateCount;
        MinimumDetectorConfidence = minimumDetectorConfidence;
        MaximumDetectorConfidence = maximumDetectorConfidence;
        AverageDetectorConfidence = averageDetectorConfidence;
        OcrTexts = boundedOcrTexts.Values;
        OcrTextCount = boundedOcrTexts.Count;
        TranslationInputTexts = boundedTranslationInputTexts.Values;
        TranslationInputTextCount = boundedTranslationInputTexts.Count;
        TranslatedTexts = boundedTranslatedTexts.Values;
        TranslatedTextCount = boundedTranslatedTexts.Count;
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

    public int? GroupingObservationCount { get; }

    public int? RequiredGroupingObservationCount { get; }

    public DateTimeOffset? GroupingFirstObservedAt { get; }

    public DateTimeOffset? GroupingLastObservedAt { get; }

    public TimeSpan? GroupingObservedDuration =>
        GroupingFirstObservedAt is { } first && GroupingLastObservedAt is { } last
            ? last - first
            : null;

    public TimeSpan? RequiredGroupingDuration { get; }

    public int? TranslationMemoryCacheHitCount { get; }

    public int? TranslationPersistentCacheHitCount { get; }

    public int? TranslationCacheMissCount { get; }

    public int? TranslationCacheStoredCount { get; }

    public int? TranslationOutputSanitizedCount { get; }

    public string? TranslationProviderId { get; }

    public DateTimeOffset? ProviderRequestStartedAt { get; }

    public DateTimeOffset? ProviderRequestCompletedAt { get; }

    public int? OverlayTextItemCount { get; }

    public int? OverlayMaskItemCount { get; }

    public TranslationPipelineStage? FailureStage { get; }

    /// <summary>
    /// Immediate operational exception details for an OCR-stage failure. Values are bounded and
    /// single-line; provider responses, credentials and pixels are not stored.
    /// </summary>
    public string? FailureExceptionType { get; }

    public string? FailureExceptionMessage { get; }

    public string? FailureRootCauseType { get; }

    public string? FailureRootCauseMessage { get; }

    public LiveCandidateCancellationReason CancellationReason { get; }

    public IReadOnlyList<BoundingBox> OrderedOcrBlockBounds { get; }

    public int OrderedOcrBlockBoundsCount { get; }

    public string? OrderedOcrBlockBoundsFingerprint { get; }

    public IReadOnlyList<BoundingBox> OrderedGroupedMemberBounds { get; }

    public int OrderedGroupedMemberBoundsCount { get; }

    public string? OrderedGroupedMemberBoundsFingerprint { get; }

    public WritingSystemGroupingProfile? WritingSystemGroupingProfile { get; }

    public OcrOrientationMode? OcrOrientationMode { get; }

    public TextCandidateDetectorPreset? RequestedDetectorPreset { get; }

    public TextCandidateDetectorPreset? EffectiveDetectorPreset { get; }

    public double? DetectorThreshold { get; }

    public double? DetectorBoxThreshold { get; }

    public double? DetectorUnclipRatio { get; }

    public int? RawDetectorCandidateCount { get; }

    public double? MinimumDetectorConfidence { get; }

    public double? MaximumDetectorConfidence { get; }

    public double? AverageDetectorConfidence { get; }

    public IReadOnlyList<string> OcrTexts { get; }

    public int OcrTextCount { get; }

    public IReadOnlyList<string> TranslationInputTexts { get; }

    public int TranslationInputTextCount { get; }

    public IReadOnlyList<string> TranslatedTexts { get; }

    public int TranslatedTextCount { get; }

    private static GeometryDiagnostics CreateBoundedGeometryDiagnostics(
        IEnumerable<BoundingBox>? bounds)
    {
        var materialized = (bounds ?? Array.Empty<BoundingBox>()).ToArray();
        if (materialized.Length == 0)
        {
            return new GeometryDiagnostics(Array.Empty<BoundingBox>(), 0, null);
        }

        var fingerprintSource = string.Join(
            ';',
            materialized.Select(bound => $"{bound.X},{bound.Y},{bound.Width},{bound.Height}"));
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintSource)));
        return new GeometryDiagnostics(
            materialized.Take(MaximumOrderedGeometryBounds).ToArray(),
            materialized.Length,
            fingerprint);
    }

    private static string? NormalizeDiagnosticValue(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
        if (normalized.Length <= maximumLength)
        {
            return normalized;
        }

        var boundedLength = maximumLength;
        if (char.IsHighSurrogate(normalized[boundedLength - 1]))
        {
            boundedLength--;
        }

        return normalized[..boundedLength];
    }

    private static TextDiagnostics CreateBoundedTextDiagnostics(IEnumerable<string>? texts)
    {
        var normalizedTexts = (texts ?? Array.Empty<string>())
            .Select(text => NormalizeDiagnosticValue(text, MaximumDiagnosticTextLength))
            .Where(text => text is not null)
            .Cast<string>()
            .ToArray();
        return new TextDiagnostics(
            normalizedTexts.Take(MaximumDiagnosticTextEntries).ToArray(),
            normalizedTexts.Length);
    }

    private static void ValidateNonNegative(int? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateOptionalEnum<TEnum>(TEnum? value, string parameterName)
        where TEnum : struct, Enum
    {
        if (value is { } enumValue && !Enum.IsDefined(enumValue))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidatePositiveFinite(double? value, string parameterName)
    {
        if (value is { } number && (!double.IsFinite(number) || number <= 0d))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateConfidence(double? value, string parameterName)
    {
        if (value is { } confidence && (!double.IsFinite(confidence) || confidence is < 0d or > 1d))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private sealed record GeometryDiagnostics(
        IReadOnlyList<BoundingBox> Bounds,
        int Count,
        string? Fingerprint);

    private sealed record TextDiagnostics(
        IReadOnlyList<string> Values,
        int Count);
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
    CandidateGeometryJitterMatched,
    CandidateGroupingChanged,
    CandidateGroupingAwaitingConfirmation,
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
    CandidateGroupingChanged,
    CandidateSourceChanged,
    CandidateDisappeared,
    CaptureLost,
    CandidatePipelineDegraded,
}
