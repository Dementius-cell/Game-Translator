using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Infrastructure.Ocr;

internal static class PaddleTextDetectionPresetResolver
{
    private const double StandardThreshold = 0.30d;
    private const double StandardBoxThreshold = 0.60d;
    private const double StandardUnclipRatio = 1.20d;

    public static PaddleTextDetectionPresetSettings Resolve(
        TextCandidateDetectorPreset requestedPreset,
        string language,
        OcrOrientationMode orientationMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        if (!Enum.IsDefined(requestedPreset))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedPreset));
        }

        if (!Enum.IsDefined(orientationMode))
        {
            throw new ArgumentOutOfRangeException(nameof(orientationMode));
        }

        var effectivePreset = IsChineseLanguage(language)
            ? requestedPreset
            : TextCandidateDetectorPreset.Standard;
        var boxThreshold = effectivePreset switch
        {
            TextCandidateDetectorPreset.ChineseExperimental => 0.65d,
            TextCandidateDetectorPreset.ChineseStrictExperimental => 0.70d,
            _ => StandardBoxThreshold,
        };

        return new PaddleTextDetectionPresetSettings(
            requestedPreset,
            effectivePreset,
            StandardThreshold,
            boxThreshold,
            StandardUnclipRatio);
    }

    private static bool IsChineseLanguage(string language)
    {
        var normalized = language.Trim();
        return normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("chi_sim", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("chi_tra", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record PaddleTextDetectionPresetSettings(
    TextCandidateDetectorPreset RequestedPreset,
    TextCandidateDetectorPreset EffectivePreset,
    double Threshold,
    double BoxThreshold,
    double UnclipRatio)
{
    public TextCandidateDetectionDiagnostics CreateDiagnostics(IReadOnlyList<TextCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var confidences = candidates.Select(candidate => candidate.Confidence).ToArray();
        return new TextCandidateDetectionDiagnostics(
            RequestedPreset,
            EffectivePreset,
            Threshold,
            BoxThreshold,
            UnclipRatio,
            candidates.Count,
            confidences.Length == 0 ? null : confidences.Min(),
            confidences.Length == 0 ? null : confidences.Max(),
            confidences.Length == 0 ? null : confidences.Average());
    }
}
