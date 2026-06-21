using GameTranslator.Application.Debug;

namespace GameTranslator.Tests.Application;

public sealed class DebugMetricFormatterTests
{
    [Fact]
    public void Format_IncludesTimingsFpsResourcesAndCacheRate()
    {
        var formatter = new DebugMetricFormatter();
        var snapshot = new DebugMetricSnapshot(
            "Dialogue",
            ocrBoundingBoxCount: 2,
            translatedTextCount: 2,
            captureElapsed: TimeSpan.FromMilliseconds(11),
            ocrElapsed: TimeSpan.FromMilliseconds(22),
            translationElapsed: TimeSpan.FromMilliseconds(33),
            renderElapsed: TimeSpan.FromMilliseconds(4),
            totalElapsed: TimeSpan.FromMilliseconds(70),
            framesPerSecond: 59.94,
            new DebugResourceSnapshot(12.34, 150 * 1024 * 1024),
            cacheHitCount: 3,
            cacheMissCount: 1,
            skippedOcrCount: 1,
            skippedTranslationCount: 1,
            debouncedZoneCount: 1,
            frameDifferenceRatio: 0.0012d);

        var lines = formatter.Format(snapshot);

        Assert.Contains("Zone: Dialogue", lines);
        Assert.Contains("OCR boxes: 2 | translated: 2", lines);
        Assert.Contains(lines, line => line.Contains("capture 11 ms", StringComparison.Ordinal));
        Assert.Contains("FPS: 59.9", lines);
        Assert.Contains("CPU: 12.3% | RAM: 150.0 MB", lines);
        Assert.Contains("Cache: 3/4 hits (75.0%)", lines);
        Assert.Contains("Optimization: OCR skipped 1 | translation skipped 1 | debounced 1 | frame delta 0.12%", lines);
    }

    [Fact]
    public void Format_DoesNotIncludeCredentialLikeSecrets()
    {
        var formatter = new DebugMetricFormatter();
        var snapshot = new DebugMetricSnapshot(
            "SECRET_TRANSLATOR_TOKEN",
            ocrBoundingBoxCount: 1,
            translatedTextCount: 1,
            captureElapsed: TimeSpan.Zero,
            ocrElapsed: TimeSpan.Zero,
            translationElapsed: TimeSpan.Zero,
            renderElapsed: TimeSpan.Zero,
            totalElapsed: TimeSpan.Zero,
            framesPerSecond: null,
            new DebugResourceSnapshot(null, null),
            cacheHitCount: 0,
            cacheMissCount: 0);

        var lines = formatter.Format(snapshot);

        Assert.DoesNotContain(lines, line => line.Contains("SECRET_TRANSLATOR_TOKEN", StringComparison.Ordinal));
    }
}
