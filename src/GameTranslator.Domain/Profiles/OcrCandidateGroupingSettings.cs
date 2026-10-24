namespace GameTranslator.Domain.Profiles;

/// <summary>
/// Optional per-zone hard limits for detector candidate grouping.
/// A null value uses the content policy: writing-system grouping for dialogue,
/// independent detector elements for menus.
/// </summary>
public sealed record OcrCandidateGroupingSettings
{
    public const int MinimumLimit = 1;

    public const int MaximumLimit = 12;

    public static OcrCandidateGroupingSettings Default { get; } = new();

    public int? MaximumHorizontalLines { get; init; }

    public int? MaximumVerticalColumns { get; init; }
}
