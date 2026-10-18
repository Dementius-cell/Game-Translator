namespace GameTranslator.Domain.Profiles;

public enum OcrPreprocessingMode { None, Manual, Adaptive }

public sealed record OcrPreprocessingSettings
{
    public static OcrPreprocessingSettings Default { get; } = new();

    public bool IsEnabled { get; init; }

    // Null resolves schema-1 profiles through their original IsEnabled flag.
    public OcrPreprocessingMode? Mode { get; init; }

    public OcrPreprocessingMode ResolveMode() => Mode ?? (IsEnabled ? OcrPreprocessingMode.Manual : OcrPreprocessingMode.None);

    public double Contrast { get; init; } = 1;

    public int Brightness { get; init; }

    public double Sharpness { get; init; }

    public bool ThresholdingEnabled { get; init; }

    public byte Threshold { get; init; } = 128;

    public double Scale { get; init; } = 1;

    // Optional schema-1 additions: absent fields preserve legacy preprocessing.
    public bool InvertColors { get; init; }

    public bool AdaptiveThresholding { get; init; }

    public bool AddWhiteBorder { get; init; }

    public bool NoiseReductionEnabled { get; init; }
}