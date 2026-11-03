namespace GameTranslator.Tests.Infrastructure;

// These tests measure bounded pipe exchanges with real PowerShell child processes.
// Run them apart from CPU-heavy suites so runner contention is not measured as
// worker protocol latency. Production deadlines and timeout assertions stay intact.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DetectorProcessCollection
{
    public const string Name = "Detector process timing";
}
