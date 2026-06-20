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
        int cacheMissCount)
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

    public double? CacheHitRate => CacheHitCount + CacheMissCount == 0
        ? null
        : CacheHitCount / (double)(CacheHitCount + CacheMissCount);
}
