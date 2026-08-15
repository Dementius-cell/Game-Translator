using GameTranslator.Application.Ocr;

namespace GameTranslator.Tests.Application;

public sealed class ComicGeometryCandidateDetectorBenchmarkValidatorTests
{
    private readonly ComicGeometryCandidateDetectorBenchmarkValidator validator = new();

    [Fact]
    public void Validate_WhenReportContainsAdr023Measurements_Passes()
    {
        var report = CreateValidReport();

        var result = validator.Validate(report);

        Assert.True(result.Passed);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_WhenReportCrossesForbiddenProductionBoundaries_Fails()
    {
        var report = CreateValidReport() with
        {
            ResearchOnly = false,
            ProductionPipelineWired = true,
            SavedProfileSchemaChanged = true,
            IOcrEngineContractChanged = true,
            UnconditionalFullFrameRetryEnabled = true,
            OutputScope = ComicGeometryCandidateDetectorOutputScope.TextRecognition,
            CandidateScope = ComicGeometryCandidateDetectorCandidateScope.PersistedProfileField,
        };

        var result = validator.Validate(report);

        Assert.False(result.Passed);
        Assert.Contains(result.Errors, error => error.Contains("research-only", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("production pipeline", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("persisted profile fields", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("IOcrEngine", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("full-frame retry", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("transient candidate bounds", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("saved OCR zone", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenHardwareIsBelowAdr023Floor_Fails()
    {
        var report = CreateValidReport() with
        {
            Hardware = new ComicGeometryCandidateDetectorBenchmarkHardware
            {
                CpuName = "Quad Core",
                CpuPhysicalCoreCount = 4,
                GpuName = "Small GPU",
                GpuVramMegabytes = 4096,
                DriverVersion = "1.0",
            },
        };

        var result = validator.Validate(report);

        Assert.False(result.Passed);
        Assert.Contains(result.Errors, error => error.Contains("6 physical cores", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("8192 MB", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenRequiredScenarioOrMeasurementsAreMissing_Fails()
    {
        var report = CreateValidReport() with
        {
            Scenarios = new[]
            {
                CreateScenario("S9") with
                {
                    EvidenceArtifactPath = string.Empty,
                    ExpectedReferenceCount = 0,
                    MatchedReferenceCount = 1,
                    OutsideCandidateCount = -1,
                    VisibleTextDurationSeconds = 5,
                    EndToEndRunCount = 0,
                    ColdDetectorLatency = new ComicGeometryCandidateDetectorLatencySummary
                    {
                        SampleCount = 0,
                        P50Milliseconds = 0,
                        P95Milliseconds = -1,
                    },
                    SteadyDetectorLatency = new ComicGeometryCandidateDetectorLatencySummary
                    {
                        SampleCount = 10,
                        P50Milliseconds = 40,
                        P95Milliseconds = 30,
                    },
                    ResourceUsage = new ComicGeometryCandidateDetectorResourceSummary
                    {
                        PeakCpuPercent = 101,
                        PeakGpuPercent = -1,
                        PeakVramMegabytes = -1,
                    },
                },
            },
        };

        var result = validator.Validate(report);

        Assert.False(result.Passed);
        Assert.Contains(result.Errors, error => error.Contains("S10 benchmark scenario", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("evidence artifact path", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("expected reference count", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("outside candidate noise", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("between 3 and 4 seconds", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("cold detector latency", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("P95 must be greater", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("end-to-end run count", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("peak CPU percent", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("peak GPU percent", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("peak VRAM MB", StringComparison.Ordinal));
    }

    private static ComicGeometryCandidateDetectorBenchmarkReport CreateValidReport()
    {
        return new ComicGeometryCandidateDetectorBenchmarkReport
        {
            SchemaVersion = 1,
            ResearchOnly = true,
            ProductionPipelineWired = false,
            SavedProfileSchemaChanged = false,
            IOcrEngineContractChanged = false,
            UnconditionalFullFrameRetryEnabled = false,
            OutputScope = ComicGeometryCandidateDetectorOutputScope.TransientCandidateBoundsOnly,
            CandidateScope = ComicGeometryCandidateDetectorCandidateScope.SavedOcrZoneOnly,
            DetectorName = "research-gpu-candidate-detector",
            DetectorVersion = "0.0-local",
            ReproducibleCommand = "pwsh ./outputs/track-d-gpu-candidate-detector/run-benchmark.ps1",
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            Hardware = new ComicGeometryCandidateDetectorBenchmarkHardware
            {
                CpuName = "AMD Ryzen 7 5700X3D",
                CpuPhysicalCoreCount = 8,
                GpuName = "NVIDIA GeForce RTX 3060",
                GpuVramMegabytes = 8192,
                DriverVersion = "610.47",
            },
            Scenarios = new[]
            {
                CreateScenario("S9"),
                CreateScenario("S10"),
            },
        };
    }

    private static ComicGeometryCandidateDetectorBenchmarkScenario CreateScenario(string scenarioId)
    {
        return new ComicGeometryCandidateDetectorBenchmarkScenario
        {
            ScenarioId = scenarioId,
            SourceImageId = $"{scenarioId}-owner-manga-page",
            EvidenceArtifactPath = $"outputs/track-d-gpu-candidate-detector/{scenarioId}-evidence.png",
            ExpectedReferenceCount = string.Equals(scenarioId, "S9", StringComparison.Ordinal) ? 10 : 6,
            DetectedCandidateCount = 12,
            MatchedReferenceCount = string.Equals(scenarioId, "S9", StringComparison.Ordinal) ? 8 : 5,
            OutsideCandidateCount = 2,
            VisibleTextDurationSeconds = 3.5,
            EndToEndRunCount = 30,
            EndToEndCompletedWithinVisibleWindowCount = 30,
            ColdDetectorLatency = new ComicGeometryCandidateDetectorLatencySummary
            {
                SampleCount = 1,
                P50Milliseconds = 180,
                P95Milliseconds = 180,
            },
            SteadyDetectorLatency = new ComicGeometryCandidateDetectorLatencySummary
            {
                SampleCount = 30,
                P50Milliseconds = 42,
                P95Milliseconds = 75,
            },
            EndToEndLatency = new ComicGeometryCandidateDetectorLatencySummary
            {
                SampleCount = 30,
                P50Milliseconds = 420,
                P95Milliseconds = 810,
            },
            ResourceUsage = new ComicGeometryCandidateDetectorResourceSummary
            {
                PeakCpuPercent = 35,
                PeakGpuPercent = 62,
                PeakVramMegabytes = 2100,
            },
        };
    }
}
