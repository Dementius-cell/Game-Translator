using GameTranslator.Application.Overlay;

namespace GameTranslator.Application.Pipeline;

public sealed class TranslationPipelineBatchResult
{
    public TranslationPipelineBatchResult(
        string profileId,
        IEnumerable<TranslationPipelineResult> zoneResults,
        IEnumerable<TranslationPipelineZoneFailure> zoneFailures,
        OverlaySnapshot overlaySnapshot)
    {
        ProfileId = profileId?.Trim() ?? string.Empty;
        ZoneResults = (zoneResults ?? throw new ArgumentNullException(nameof(zoneResults))).ToArray();
        ZoneFailures = (zoneFailures ?? throw new ArgumentNullException(nameof(zoneFailures))).ToArray();
        OverlaySnapshot = overlaySnapshot ?? throw new ArgumentNullException(nameof(overlaySnapshot));
    }

    public string ProfileId { get; }

    public IReadOnlyList<TranslationPipelineResult> ZoneResults { get; }

    public IReadOnlyList<TranslationPipelineZoneFailure> ZoneFailures { get; }

    public OverlaySnapshot OverlaySnapshot { get; }

    public int SucceededZoneCount => ZoneResults.Count;

    public int FailedZoneCount => ZoneFailures.Count;

    public int TotalZoneCount => SucceededZoneCount + FailedZoneCount;

    public bool HasFailures => FailedZoneCount > 0;

    public int RecognizedBlockCount => ZoneResults.Sum(result => result.RecognizedBlockCount);

    public int TranslatedBlockCount => ZoneResults.Sum(result => result.TranslatedBlockCount);

    public int SkippedOcrCount => ZoneResults.Count(result => result.Optimization.OcrSkipped);

    public int SkippedTranslationCount => ZoneResults.Count(result => result.Optimization.TranslationSkipped);

    public int DebouncedZoneCount => ZoneResults.Count(result => result.Optimization.Debounced);

    public double? AverageFrameDifferenceRatio
    {
        get
        {
            var ratios = ZoneResults
                .Select(result => result.Optimization.FrameDifferenceRatio)
                .Where(ratio => ratio.HasValue)
                .Select(ratio => ratio!.Value)
                .ToArray();

            return ratios.Length == 0 ? null : ratios.Average();
        }
    }
}
