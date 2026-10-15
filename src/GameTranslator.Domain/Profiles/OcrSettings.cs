namespace GameTranslator.Domain.Profiles;

public sealed record OcrSettings
{
    public const string WindowsEngineId = "Windows";

    public const string TesseractEngineId = "Tesseract";

    public static OcrSettings Default { get; } = new();

    public WatermarkFilterMode WatermarkFilter { get; init; } = WatermarkFilterMode.Off;

    public string Engine { get; init; } = WindowsEngineId;

    public bool ExperimentalMultilingual { get; init; }

    public string MultilingualLanguages { get; init; } = ExperimentalMultilingualLanguages;

    public const string ExperimentalMultilingualLanguages = "jpn+kor+chi_sim+eng";

    public static string? GetMultilingualConfigurationError(bool enabled, string engine, OcrOrientationMode orientation, string provider)
    {
        if (!enabled) return null;
        if (!string.Equals(engine, TesseractEngineId, StringComparison.OrdinalIgnoreCase))
            return "Experimental multilingual OCR requires Tesseract.";
        return null;
    }

    public OcrOrientationMode OrientationMode { get; init; } = OcrOrientationMode.Auto;

    public static bool IsSupportedEngine(string? engine)
    {
        return string.Equals(engine, WindowsEngineId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(engine, TesseractEngineId, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedOrientationMode(OcrOrientationMode orientationMode)
    {
        return Enum.IsDefined(orientationMode);
    }
}
