using GameTranslator.Application.Capture;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Ocr;

/// <summary>
/// Proposes transient text regions inside one already captured OCR zone.
/// </summary>
public interface ITextCandidateDetector
{
    Task<TextCandidateDetectionResult> DetectAsync(
        TextCandidateDetectionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class TextCandidateDetectionRequest
{
    public TextCandidateDetectionRequest(
        CapturedFrame frame,
        string language,
        OcrOrientationMode orientationMode,
        OcrLayoutMode layoutMode)
    {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        if (!Enum.IsDefined(orientationMode))
        {
            throw new ArgumentOutOfRangeException(nameof(orientationMode));
        }

        if (!Enum.IsDefined(layoutMode))
        {
            throw new ArgumentOutOfRangeException(nameof(layoutMode));
        }

        Language = language.Trim();
        OrientationMode = orientationMode;
        LayoutMode = layoutMode;
    }

    public CapturedFrame Frame { get; }

    public string Language { get; }

    public OcrOrientationMode OrientationMode { get; }

    public OcrLayoutMode LayoutMode { get; }
}

public sealed class TextCandidateDetectionResult
{
    private TextCandidateDetectionResult(
        TextCandidateDetectorAvailability availability,
        string detectorId,
        string? unavailableReason,
        IEnumerable<TextCandidate> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detectorId);
        ArgumentNullException.ThrowIfNull(candidates);

        if (!Enum.IsDefined(availability))
        {
            throw new ArgumentOutOfRangeException(nameof(availability));
        }

        if (availability == TextCandidateDetectorAvailability.Unavailable
            && string.IsNullOrWhiteSpace(unavailableReason))
        {
            throw new ArgumentException("An unavailable detector must provide a reason.", nameof(unavailableReason));
        }

        Availability = availability;
        DetectorId = detectorId.Trim();
        UnavailableReason = string.IsNullOrWhiteSpace(unavailableReason)
            ? null
            : unavailableReason.Trim();
        Candidates = candidates.ToArray();
    }

    public TextCandidateDetectorAvailability Availability { get; }

    public string DetectorId { get; }

    public string? UnavailableReason { get; }

    public IReadOnlyList<TextCandidate> Candidates { get; }

    public static TextCandidateDetectionResult Available(
        string detectorId,
        IEnumerable<TextCandidate> candidates)
    {
        return new TextCandidateDetectionResult(
            TextCandidateDetectorAvailability.Available,
            detectorId,
            unavailableReason: null,
            candidates);
    }

    public static TextCandidateDetectionResult Unavailable(string detectorId, string reason)
    {
        return new TextCandidateDetectionResult(
            TextCandidateDetectorAvailability.Unavailable,
            detectorId,
            reason,
            Array.Empty<TextCandidate>());
    }
}

public enum TextCandidateDetectorAvailability
{
    Available,
    Unavailable,
}

public sealed record TextCandidate(BoundingBox Bounds, double Confidence)
{
    /// <summary>
    /// Gets the original detector rectangles represented by this bounded candidate.
    /// A raw detector result has one member; bounded grouping retains every member for diagnostics.
    /// </summary>
    public IReadOnlyList<BoundingBox> SourceCandidateBounds { get; init; } = new[] { Bounds };

    public int SourceCandidateCount => SourceCandidateBounds.Count;

    public bool HasValidConfidence => double.IsFinite(Confidence)
        && Confidence is >= 0d and <= 1d;
}
