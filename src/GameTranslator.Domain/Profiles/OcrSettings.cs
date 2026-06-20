namespace GameTranslator.Domain.Profiles;

public sealed record OcrSettings
{
    public const string WindowsEngineId = "Windows";

    public const string TesseractEngineId = "Tesseract";

    public static OcrSettings Default { get; } = new();

    public string Engine { get; init; } = WindowsEngineId;

    public static bool IsSupportedEngine(string? engine)
    {
        return string.Equals(engine, WindowsEngineId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(engine, TesseractEngineId, StringComparison.OrdinalIgnoreCase);
    }
}