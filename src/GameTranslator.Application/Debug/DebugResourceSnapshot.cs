namespace GameTranslator.Application.Debug;

public sealed class DebugResourceSnapshot
{
    public DebugResourceSnapshot(double? cpuPercent, long? workingSetBytes)
    {
        if (cpuPercent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cpuPercent), "CPU percent must not be negative.");
        }

        if (workingSetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workingSetBytes), "Working set bytes must not be negative.");
        }

        CpuPercent = cpuPercent;
        WorkingSetBytes = workingSetBytes;
    }

    public double? CpuPercent { get; }

    public long? WorkingSetBytes { get; }
}
