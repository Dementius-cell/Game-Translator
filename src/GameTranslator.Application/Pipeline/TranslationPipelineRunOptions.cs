namespace GameTranslator.Application.Pipeline;

public sealed class TranslationPipelineRunOptions
{
    public static TimeSpan DefaultStableTextInterval { get; } = TimeSpan.FromSeconds(1);

    public static TranslationPipelineRunOptions Default { get; } = new();

    public TranslationPipelineRunOptions(
        bool requireStableTextBeforeTranslation = false,
        TimeSpan? stableTextInterval = null,
        bool preservePreviousOverlayWhileWaitingForStableText = false)
    {
        var effectiveStableTextInterval = stableTextInterval ?? DefaultStableTextInterval;
        if (effectiveStableTextInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stableTextInterval),
                "Stable text interval must not be negative.");
        }

        RequireStableTextBeforeTranslation = requireStableTextBeforeTranslation;
        StableTextInterval = effectiveStableTextInterval;
        PreservePreviousOverlayWhileWaitingForStableText = preservePreviousOverlayWhileWaitingForStableText;
    }

    public bool RequireStableTextBeforeTranslation { get; }

    public TimeSpan StableTextInterval { get; }

    public bool PreservePreviousOverlayWhileWaitingForStableText { get; }
}
