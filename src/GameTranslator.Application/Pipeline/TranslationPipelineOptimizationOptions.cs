namespace GameTranslator.Application.Pipeline;

public sealed class TranslationPipelineOptimizationOptions
{
    public const double DefaultFrameDifferenceThreshold = 0.002d;

    public static TimeSpan DefaultDebounceInterval { get; } = TimeSpan.FromMilliseconds(250);

    public static TranslationPipelineOptimizationOptions Disabled { get; } = new(isEnabled: false);

    public TranslationPipelineOptimizationOptions(
        double frameDifferenceThreshold = DefaultFrameDifferenceThreshold,
        TimeSpan? debounceInterval = null,
        bool isEnabled = true)
    {
        if (frameDifferenceThreshold is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameDifferenceThreshold),
                "Frame difference threshold must be between 0 and 1.");
        }

        var effectiveDebounceInterval = debounceInterval ?? DefaultDebounceInterval;
        if (effectiveDebounceInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(debounceInterval),
                "Debounce interval must not be negative.");
        }

        IsEnabled = isEnabled;
        FrameDifferenceThreshold = frameDifferenceThreshold;
        DebounceInterval = effectiveDebounceInterval;
    }

    public bool IsEnabled { get; }

    public double FrameDifferenceThreshold { get; }

    public TimeSpan DebounceInterval { get; }
}
