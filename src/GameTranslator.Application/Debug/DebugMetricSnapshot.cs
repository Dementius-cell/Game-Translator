namespace GameTranslator.Application.Debug;

public sealed class DebugMetricSnapshot
{
    public DebugMetricSnapshot(
        string zoneName,
        int ocrBoundingBoxCount,
        int translatedTextCount,
        TimeSpan captureElapsed,
        TimeSpan ocrElapsed,
        TimeSpan translationElapsed,
        TimeSpan renderElapsed,
        TimeSpan totalElapsed,
        double? framesPerSecond,
        DebugResourceSnapshot resourceSnapshot,
        int cacheHitCount,
        int cacheMissCount,
        int skippedOcrCount = 0,
        int skippedTranslationCount = 0,
        int debouncedZoneCount = 0,
        double? frameDifferenceRatio = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneName);

        if (ocrBoundingBoxCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ocrBoundingBoxCount));
        }

        if (translatedTextCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(translatedTextCount));
        }

        if (framesPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        }

        if (cacheHitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheHitCount));
        }

        if (cacheMissCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheMissCount));
        }

        if (skippedOcrCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skippedOcrCount));
        }

        if (skippedTranslationCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skippedTranslationCount));
        }

        if (debouncedZoneCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(debouncedZoneCount));
        }

        if (frameDifferenceRatio is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(frameDifferenceRatio));
        }

        ZoneName = zoneName.Trim();
        OcrBoundingBoxCount = ocrBoundingBoxCount;
        TranslatedTextCount = translatedTextCount;
        CaptureElapsed = captureElapsed;
        OcrElapsed = ocrElapsed;
        TranslationElapsed = translationElapsed;
        RenderElapsed = renderElapsed;
        TotalElapsed = totalElapsed;
        FramesPerSecond = framesPerSecond;
        ResourceSnapshot = resourceSnapshot ?? throw new ArgumentNullException(nameof(resourceSnapshot));
        CacheHitCount = cacheHitCount;
        CacheMissCount = cacheMissCount;
        SkippedOcrCount = skippedOcrCount;
        SkippedTranslationCount = skippedTranslationCount;
        DebouncedZoneCount = debouncedZoneCount;
        FrameDifferenceRatio = frameDifferenceRatio;
    }

    public string ZoneName { get; }

    public int OcrBoundingBoxCount { get; }

    public int TranslatedTextCount { get; }

    public TimeSpan CaptureElapsed { get; }

    public TimeSpan OcrElapsed { get; }

    public TimeSpan TranslationElapsed { get; }

    public TimeSpan RenderElapsed { get; }

    public TimeSpan TotalElapsed { get; }

    public double? FramesPerSecond { get; }

    public DebugResourceSnapshot ResourceSnapshot { get; }

    public int CacheHitCount { get; }

    public int CacheMissCount { get; }

    public int SkippedOcrCount { get; }

    public int SkippedTranslationCount { get; }

    public int DebouncedZoneCount { get; }

    public double? FrameDifferenceRatio { get; }

    public double? CacheHitRate => CacheHitCount + CacheMissCount == 0
        ? null
        : CacheHitCount / (double)(CacheHitCount + CacheMissCount);
}
