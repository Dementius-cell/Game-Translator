namespace GameTranslator.Application.Pipeline;

public sealed class TranslationPipelineOptimizationInfo
{
    public static TranslationPipelineOptimizationInfo None { get; } = new(
        ocrSkipped: false,
        translationSkipped: false,
        debounced: false,
        frameDifferenceRatio: null);

    public TranslationPipelineOptimizationInfo(
        bool ocrSkipped,
        bool translationSkipped,
        bool debounced,
        double? frameDifferenceRatio)
    {
        if (frameDifferenceRatio is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameDifferenceRatio),
                "Frame difference ratio must be between 0 and 1.");
        }

        OcrSkipped = ocrSkipped;
        TranslationSkipped = translationSkipped;
        Debounced = debounced;
        FrameDifferenceRatio = frameDifferenceRatio;
    }

    public bool OcrSkipped { get; }

    public bool TranslationSkipped { get; }

    public bool Debounced { get; }

    public double? FrameDifferenceRatio { get; }
}
