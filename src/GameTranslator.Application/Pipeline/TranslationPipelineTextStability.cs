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
        lastObservedAt: null);

    public TranslationPipelineTextStability(
        bool isRequired,
        bool isStable,
        DateTimeOffset? firstObservedAt,
        DateTimeOffset? lastObservedAt)
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

        IsRequired = isRequired;
        IsStable = isStable;
        FirstObservedAt = firstObservedAt;
        LastObservedAt = lastObservedAt;
    }

    public bool IsRequired { get; }

    public bool IsStable { get; }

    public DateTimeOffset? FirstObservedAt { get; }

    public DateTimeOffset? LastObservedAt { get; }

    public TimeSpan? ObservedDuration => FirstObservedAt is { } first && LastObservedAt is { } last
        ? last - first
        : null;
}
