namespace GameTranslator.Domain.Profiles;

public sealed record OcrPreprocessingSettings
{
    public static OcrPreprocessingSettings Default { get; } = new();

    public bool IsEnabled { get; init; }

    public double Contrast { get; init; } = 1;

    public int Brightness { get; init; }

    public double Sharpness { get; init; }

    public bool ThresholdingEnabled { get; init; }

    public byte Threshold { get; init; } = 128;

    public double Scale { get; init; } = 1;

    public bool NoiseReductionEnabled { get; init; }
}