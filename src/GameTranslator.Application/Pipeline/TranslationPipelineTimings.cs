namespace GameTranslator.Application.Pipeline;

public sealed class TranslationPipelineTimings
{
    public static TranslationPipelineTimings Empty { get; } = new(
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero);

    public TranslationPipelineTimings(
        TimeSpan captureElapsed,
        TimeSpan ocrElapsed,
        TimeSpan credentialsElapsed,
        TimeSpan translationElapsed,
        TimeSpan cacheElapsed,
        TimeSpan overlayElapsed,
        TimeSpan totalElapsed)
    {
        CaptureElapsed = captureElapsed;
        OcrElapsed = ocrElapsed;
        CredentialsElapsed = credentialsElapsed;
        TranslationElapsed = translationElapsed;
        CacheElapsed = cacheElapsed;
        OverlayElapsed = overlayElapsed;
        TotalElapsed = totalElapsed;
    }

    public TimeSpan CaptureElapsed { get; }

    public TimeSpan OcrElapsed { get; }

    public TimeSpan CredentialsElapsed { get; }

    public TimeSpan TranslationElapsed { get; }

    public TimeSpan CacheElapsed { get; }

    public TimeSpan OverlayElapsed { get; }

    public TimeSpan TotalElapsed { get; }
}
