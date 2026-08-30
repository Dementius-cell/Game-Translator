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
        : this(
            frame,
            language,
            orientationMode,
            layoutMode,
            TextCandidateDetectorPreset.Standard)
    {
    }

    public TextCandidateDetectionRequest(
        CapturedFrame frame,
        string language,
        OcrOrientationMode orientationMode,
        OcrLayoutMode layoutMode,
        TextCandidateDetectorPreset detectorPreset)
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

        if (!Enum.IsDefined(detectorPreset))
        {
            throw new ArgumentOutOfRangeException(nameof(detectorPreset));
        }

        Language = language.Trim();
        OrientationMode = orientationMode;
        LayoutMode = layoutMode;
        DetectorPreset = detectorPreset;
    }

    public CapturedFrame Frame { get; }

    public string Language { get; }

    public OcrOrientationMode OrientationMode { get; }

    public OcrLayoutMode LayoutMode { get; }

    public TextCandidateDetectorPreset DetectorPreset { get; }
}

public sealed record TextCandidateDetectionDiagnostics(
    TextCandidateDetectorPreset RequestedPreset,
    TextCandidateDetectorPreset EffectivePreset,
    double Threshold,
    double BoxThreshold,
    double UnclipRatio,
    int RawCandidateCount = 0,
    double? MinimumConfidence = null,
    double? MaximumConfidence = null,
    double? AverageConfidence = null)
{
    public TextCandidateDetectionDiagnostics Validate()
    {
        if (!Enum.IsDefined(RequestedPreset) || !Enum.IsDefined(EffectivePreset))
        {
            throw new ArgumentOutOfRangeException(nameof(EffectivePreset));
        }

        if (!double.IsFinite(Threshold) || Threshold <= 0d
            || !double.IsFinite(BoxThreshold) || BoxThreshold <= 0d
            || !double.IsFinite(UnclipRatio) || UnclipRatio <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(Threshold));
        }

        if (RawCandidateCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RawCandidateCount));
        }

        ValidateConfidence(MinimumConfidence, nameof(MinimumConfidence));
        ValidateConfidence(MaximumConfidence, nameof(MaximumConfidence));
        ValidateConfidence(AverageConfidence, nameof(AverageConfidence));

        return this;
    }

    private static void ValidateConfidence(double? value, string parameterName)
    {
        if (value is { } confidence && (!double.IsFinite(confidence) || confidence is < 0d or > 1d))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public sealed class TextCandidateDetectionResult
{
    private TextCandidateDetectionResult(
        TextCandidateDetectorAvailability availability,
        string detectorId,
        string? unavailableReason,
        IEnumerable<TextCandidate> candidates,
        TextCandidateDetectionDiagnostics? diagnostics)
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
        Diagnostics = diagnostics?.Validate();
    }

    public TextCandidateDetectorAvailability Availability { get; }

    public string DetectorId { get; }

    public string? UnavailableReason { get; }

    public IReadOnlyList<TextCandidate> Candidates { get; }

    public TextCandidateDetectionDiagnostics? Diagnostics { get; }

    public static TextCandidateDetectionResult Available(
        string detectorId,
        IEnumerable<TextCandidate> candidates,
        TextCandidateDetectionDiagnostics? diagnostics = null)
    {
        return new TextCandidateDetectionResult(
            TextCandidateDetectorAvailability.Available,
            detectorId,
            unavailableReason: null,
            candidates,
            diagnostics);
    }

    public static TextCandidateDetectionResult Unavailable(string detectorId, string reason)
    {
        return new TextCandidateDetectionResult(
            TextCandidateDetectorAvailability.Unavailable,
            detectorId,
            reason,
            Array.Empty<TextCandidate>(),
            diagnostics: null);
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
