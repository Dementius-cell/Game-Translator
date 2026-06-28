namespace GameTranslator.Domain.Profiles;

public sealed record OcrZoneTextGroupingSettings
{
    public const double MinimumMergeDistancePercent = 0.5;
    public const double MaximumMergeDistancePercent = 15;
    public const double DefaultMergeDistancePercent = 4;

    public static OcrZoneTextGroupingSettings Default { get; } = new();

    public double MergeDistancePercent { get; init; } = DefaultMergeDistancePercent;
}
