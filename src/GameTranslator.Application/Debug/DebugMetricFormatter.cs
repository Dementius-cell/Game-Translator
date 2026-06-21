using System.Globalization;

namespace GameTranslator.Application.Debug;

public sealed class DebugMetricFormatter
{
    public IReadOnlyList<string> Format(DebugMetricSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var lines = new List<string>
        {
            $"Zone: {RedactSensitive(snapshot.ZoneName)}",
            $"OCR boxes: {snapshot.OcrBoundingBoxCount} | translated: {snapshot.TranslatedTextCount}",
            $"Timings: capture {FormatMilliseconds(snapshot.CaptureElapsed)} | OCR {FormatMilliseconds(snapshot.OcrElapsed)} | translate {FormatMilliseconds(snapshot.TranslationElapsed)} | render {FormatMilliseconds(snapshot.RenderElapsed)} | total {FormatMilliseconds(snapshot.TotalElapsed)}",
            snapshot.FramesPerSecond is null
                ? "FPS: n/a"
                : $"FPS: {snapshot.FramesPerSecond.Value.ToString("F1", CultureInfo.InvariantCulture)}",
            $"CPU: {FormatCpu(snapshot.ResourceSnapshot.CpuPercent)} | RAM: {FormatMemory(snapshot.ResourceSnapshot.WorkingSetBytes)}",
            FormatCache(snapshot),
            FormatOptimization(snapshot),
        };

        return lines;
    }

    private static string FormatMilliseconds(TimeSpan value)
    {
        return $"{Math.Max(0, value.TotalMilliseconds).ToString("F0", CultureInfo.InvariantCulture)} ms";
    }

    private static string FormatCpu(double? value)
    {
        return value is null ? "n/a" : $"{value.Value.ToString("F1", CultureInfo.InvariantCulture)}%";
    }

    private static string FormatMemory(long? bytes)
    {
        if (bytes is null)
        {
            return "n/a";
        }

        var megabytes = bytes.Value / 1024d / 1024d;
        return $"{megabytes.ToString("F1", CultureInfo.InvariantCulture)} MB";
    }

    private static string FormatCache(DebugMetricSnapshot snapshot)
    {
        if (snapshot.CacheHitRate is null)
        {
            return "Cache: n/a";
        }

        return $"Cache: {snapshot.CacheHitCount}/{snapshot.CacheHitCount + snapshot.CacheMissCount} hits ({(snapshot.CacheHitRate.Value * 100).ToString("F1", CultureInfo.InvariantCulture)}%)";
    }

    private static string FormatOptimization(DebugMetricSnapshot snapshot)
    {
        return $"Optimization: OCR skipped {snapshot.SkippedOcrCount} | translation skipped {snapshot.SkippedTranslationCount} | debounced {snapshot.DebouncedZoneCount} | frame delta {FormatFrameDifference(snapshot.FrameDifferenceRatio)}";
    }

    private static string FormatFrameDifference(double? ratio)
    {
        return ratio is null
            ? "n/a"
            : $"{(ratio.Value * 100).ToString("F2", CultureInfo.InvariantCulture)}%";
    }

    private static string RedactSensitive(string value)
    {
        return value.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || value.Contains("token", StringComparison.OrdinalIgnoreCase)
            || value.Contains("api key", StringComparison.OrdinalIgnoreCase)
            ? "[redacted]"
            : value;
    }
}
