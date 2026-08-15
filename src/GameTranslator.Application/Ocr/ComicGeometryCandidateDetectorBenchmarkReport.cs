namespace GameTranslator.Application.Ocr;

/// <summary>
/// Research-only report contract for ADR-023 GPU candidate-detector benchmarks.
/// </summary>
public sealed record ComicGeometryCandidateDetectorBenchmarkReport
{
    public int SchemaVersion { get; init; } = 1;

    public bool ResearchOnly { get; init; } = true;

    public bool ProductionPipelineWired { get; init; }

    public bool SavedProfileSchemaChanged { get; init; }

    public bool IOcrEngineContractChanged { get; init; }

    public bool UnconditionalFullFrameRetryEnabled { get; init; }

    public ComicGeometryCandidateDetectorOutputScope OutputScope { get; init; } =
        ComicGeometryCandidateDetectorOutputScope.TransientCandidateBoundsOnly;

    public ComicGeometryCandidateDetectorCandidateScope CandidateScope { get; init; } =
        ComicGeometryCandidateDetectorCandidateScope.SavedOcrZoneOnly;

    public string DetectorName { get; init; } = string.Empty;

    public string DetectorVersion { get; init; } = string.Empty;

    public string ReproducibleCommand { get; init; } = string.Empty;

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public ComicGeometryCandidateDetectorBenchmarkHardware Hardware { get; init; } = new();

    public IReadOnlyList<ComicGeometryCandidateDetectorBenchmarkScenario> Scenarios { get; init; } =
        Array.Empty<ComicGeometryCandidateDetectorBenchmarkScenario>();
}

public sealed record ComicGeometryCandidateDetectorBenchmarkHardware
{
    public string CpuName { get; init; } = string.Empty;

    public int CpuPhysicalCoreCount { get; init; }

    public string GpuName { get; init; } = string.Empty;

    public int GpuVramMegabytes { get; init; }

    public string DriverVersion { get; init; } = string.Empty;
}

public sealed record ComicGeometryCandidateDetectorBenchmarkScenario
{
    public string ScenarioId { get; init; } = string.Empty;

    public string SourceImageId { get; init; } = string.Empty;

    public string EvidenceArtifactPath { get; init; } = string.Empty;

    public int ExpectedReferenceCount { get; init; }

    public int DetectedCandidateCount { get; init; }

    public int MatchedReferenceCount { get; init; }

    public int OutsideCandidateCount { get; init; }

    public double VisibleTextDurationSeconds { get; init; }

    public int EndToEndRunCount { get; init; }

    public int EndToEndCompletedWithinVisibleWindowCount { get; init; }

    public ComicGeometryCandidateDetectorLatencySummary ColdDetectorLatency { get; init; } = new();

    public ComicGeometryCandidateDetectorLatencySummary SteadyDetectorLatency { get; init; } = new();

    public ComicGeometryCandidateDetectorLatencySummary EndToEndLatency { get; init; } = new();

    public ComicGeometryCandidateDetectorResourceSummary ResourceUsage { get; init; } = new();

    public double ReferenceRecall => ExpectedReferenceCount == 0
        ? 0
        : MatchedReferenceCount / (double)ExpectedReferenceCount;
}

public sealed record ComicGeometryCandidateDetectorLatencySummary
{
    public int SampleCount { get; init; }

    public double P50Milliseconds { get; init; }

    public double P95Milliseconds { get; init; }
}

public sealed record ComicGeometryCandidateDetectorResourceSummary
{
    public double PeakCpuPercent { get; init; } = -1;

    public double PeakGpuPercent { get; init; } = -1;

    public int PeakVramMegabytes { get; init; } = -1;
}

public enum ComicGeometryCandidateDetectorOutputScope
{
    TransientCandidateBoundsOnly,
    TextRecognition,
}

public enum ComicGeometryCandidateDetectorCandidateScope
{
    SavedOcrZoneOnly,
    FullFrame,
    PersistedProfileField,
}
