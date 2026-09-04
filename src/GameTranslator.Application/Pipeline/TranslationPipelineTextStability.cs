namespace GameTranslator.Application.Pipeline;

/// <summary>
/// Privacy-safe timing information about the live text-stability gate.
/// It deliberately carries no OCR or translated text.
/// </summary>
public sealed class TranslationPipelineTextStability
{
    public static TranslationPipelineTextStability NotRequired { get; } = new(
        isRequired: false,
        isStable: true,
        firstObservedAt: null,
        lastObservedAt: null,
        observationCount: 0,
        requiredObservationCount: 0,
        requiredDuration: TimeSpan.Zero,
        typewriterGrowthGuardApplied: false);

    public TranslationPipelineTextStability(
        bool isRequired,
        bool isStable,
        DateTimeOffset? firstObservedAt,
        DateTimeOffset? lastObservedAt,
        int observationCount = 0,
        int requiredObservationCount = 0,
        TimeSpan? requiredDuration = null,
        bool typewriterGrowthGuardApplied = false)
    {
        if (firstObservedAt is null != lastObservedAt is null)
        {
            throw new ArgumentException(
                "Text stability timestamps must either both be present or both be absent.");
        }

        if (firstObservedAt is { } first && lastObservedAt is { } last && last < first)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastObservedAt),
                "The last text-stability observation cannot precede the first observation.");
        }

        if (observationCount < 0 || requiredObservationCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                observationCount < 0 ? nameof(observationCount) : nameof(requiredObservationCount));
        }

        if (requiredDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredDuration));
        }

        if (observationCount > 0 && firstObservedAt is null)
        {
            throw new ArgumentException(
                "A positive text-stability observation count requires timestamps.",
                nameof(observationCount));
        }

        IsRequired = isRequired;
        IsStable = isStable;
        FirstObservedAt = firstObservedAt;
        LastObservedAt = lastObservedAt;
        ObservationCount = observationCount;
        RequiredObservationCount = requiredObservationCount;
        RequiredDuration = requiredDuration ?? TimeSpan.Zero;
        TypewriterGrowthGuardApplied = typewriterGrowthGuardApplied;
    }

    public bool IsRequired { get; }

    public bool IsStable { get; }

    public DateTimeOffset? FirstObservedAt { get; }

    public DateTimeOffset? LastObservedAt { get; }

    public int ObservationCount { get; }

    public int RequiredObservationCount { get; }

    public TimeSpan RequiredDuration { get; }

    public bool TypewriterGrowthGuardApplied { get; }

    public TimeSpan? ObservedDuration => FirstObservedAt is { } first && LastObservedAt is { } last
        ? last - first
        : null;
}
