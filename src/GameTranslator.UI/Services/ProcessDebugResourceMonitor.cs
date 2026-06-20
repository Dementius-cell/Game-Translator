using System.Diagnostics;
using GameTranslator.Application.Debug;

namespace GameTranslator.UI.Services;

public sealed class ProcessDebugResourceMonitor : IDebugResourceMonitor
{
    private readonly Lock syncRoot = new();
    private DateTimeOffset? previousSampleAt;
    private TimeSpan? previousProcessorTime;

    public DebugResourceSnapshot Sample()
    {
        lock (syncRoot)
        {
            using var process = Process.GetCurrentProcess();
            var sampledAt = DateTimeOffset.UtcNow;
            var processorTime = process.TotalProcessorTime;
            var workingSet = process.WorkingSet64;
            double? cpuPercent = null;

            if (previousSampleAt is not null && previousProcessorTime is not null)
            {
                var elapsed = sampledAt - previousSampleAt.Value;
                var cpuElapsed = processorTime - previousProcessorTime.Value;
                if (elapsed.TotalMilliseconds > 0)
                {
                    cpuPercent = Math.Clamp(
                        cpuElapsed.TotalMilliseconds / elapsed.TotalMilliseconds / Environment.ProcessorCount * 100,
                        0,
                        100);
                }
            }

            previousSampleAt = sampledAt;
            previousProcessorTime = processorTime;

            return new DebugResourceSnapshot(cpuPercent, workingSet);
        }
    }
}
