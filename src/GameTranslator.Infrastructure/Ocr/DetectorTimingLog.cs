using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace GameTranslator.Infrastructure.Ocr;

/// <summary>Opt-in, bounded numeric diagnostics. Disk failure never fails detection.</summary>
internal sealed class DetectorTimingLog : IDisposable
{
    internal const int MaximumFiles = 32;
    internal const long MaximumBytes = 8 * 1024 * 1024;
    private readonly Channel<DetectorTimingEntry> queue = Channel.CreateBounded<DetectorTimingEntry>(
        new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    internal Task Completion { get; }

    internal DetectorTimingLog(string directory, long maximumBytes = MaximumBytes)
    {
        Completion = Task.Run(() => WriteAsync(directory, maximumBytes));
    }

    internal void Write(DetectorTimingEntry entry) => queue.Writer.TryWrite(entry);

    public void Dispose() => queue.Writer.TryComplete();

    private async Task WriteAsync(string directory, long maximumBytes)
    {
        try
        {
            Directory.CreateDirectory(directory);
            if (Directory.EnumerateFiles(directory, "detector-*.jsonl").Take(MaximumFiles).Count() >= MaximumFiles)
                return;
            var path = Path.Combine(directory, $"detector-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Environment.ProcessId}-{Guid.NewGuid():N}.jsonl");
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await foreach (var entry in queue.Reader.ReadAllAsync())
            {
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n");
                if (stream.Length + bytes.Length > maximumBytes) return;
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            // Stop this optional sink; detection and cancellation must remain independent of disk access.
            System.Diagnostics.Trace.WriteLine("Detector timing diagnostics disabled after a storage failure.");
        }
        finally
        {
            queue.Writer.TryComplete();
        }
    }
}

internal sealed record DetectorTimingEntry
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public string Kind { get; init; } = "request";
    public long RequestId { get; init; }
    public DateTimeOffset? CapturedAt { get; init; }
    public Guid? SourceId { get; init; }
    public double? SourceTimestampMs { get; init; }
    public bool CachedReuse { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public long Generation { get; set; }
    public string Outcome { get; set; } = "started";
    public double TotalMs { get; set; }
    public double LockWaitMs { get; set; }
    public double StartupMs { get; set; }
    public double BitmapMs { get; set; }
    public double ExchangeMs { get; set; }
    public int Candidates { get; set; }
    public long? ResponseRequestId { get; set; }
    public DetectorWorkerTiming? Worker { get; set; }
}

internal sealed record DetectorWorkerTiming
{
    public double? ImportsMs { get; init; }
    public double? PredictorMs { get; init; }
    public double? DecodeMs { get; init; }
    public double? PrepareMs { get; init; }
    public double? InferAndTransferMs { get; init; }
    public double? PostprocessMs { get; init; }
    public double TotalMs { get; init; }
}
