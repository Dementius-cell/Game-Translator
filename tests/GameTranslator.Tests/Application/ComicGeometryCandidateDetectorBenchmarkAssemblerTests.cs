using GameTranslator.Application.Ocr;

namespace GameTranslator.Tests.Application;

public sealed class ComicGeometryCandidateDetectorBenchmarkAssemblerTests
{
    private readonly ComicGeometryCandidateDetectorBenchmarkAssembler assembler = new();

    [Fact]
    public void Build_WhenDetectorMeasurementsCoverBothOwnerScenarios_ProducesAValidatedReport()
    {
        var report = assembler.Build(CreateInput());
        var s9 = report.Scenarios.Single(scenario => scenario.ScenarioId == "S9");
        var s10 = report.Scenarios.Single(scenario => scenario.ScenarioId == "S10");

        Assert.True(new ComicGeometryCandidateDetectorBenchmarkValidator().Validate(report).Passed);
        Assert.Equal(10, s9.ExpectedReferenceCount);
        Assert.Equal(11, s9.DetectedCandidateCount);
        Assert.Equal(10, s9.MatchedReferenceCount);
        Assert.Equal(1, s9.OutsideCandidateCount);
        Assert.Equal(30d, s9.SteadyDetectorLatency.P50Milliseconds);
        Assert.Equal(48d, s9.SteadyDetectorLatency.P95Milliseconds);
        Assert.Equal(3, s9.EndToEndRunCount);
        Assert.Equal(2, s9.EndToEndCompletedWithinVisibleWindowCount);
        Assert.Equal(400d, s9.EndToEndLatency.P50Milliseconds);
        Assert.Equal(490d, s9.EndToEndLatency.P95Milliseconds);
        Assert.Equal(35d, s9.ResourceUsage.PeakCpuPercent);
        Assert.Equal(60d, s9.ResourceUsage.PeakGpuPercent);
        Assert.Equal(2200, s9.ResourceUsage.PeakVramMegabytes);
        Assert.Equal(6, s10.ExpectedReferenceCount);
        Assert.Equal(6, s10.MatchedReferenceCount);
    }

    [Fact]
    public void Build_WhenDetectorCandidateEscapesSavedOcrZone_Throws()
    {
        var input = CreateInput() with
        {
            Scenarios = new[]
            {
                CreateScenario("S9", new[] { new BoundingBox(1191, 1491, 10, 10) }),
                CreateScenario("S10", CreateS10Bounds()),
            },
        };

        var exception = Assert.Throws<ArgumentException>(() => assembler.Build(input));

        Assert.Contains("saved OCR zone", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WhenLatencyMeasurementIsNotPositiveAndFinite_Throws()
    {
        var input = CreateInput() with
        {
            Scenarios = new[]
            {
                CreateScenario("S9", CreateS9Bounds()) with
                {
                    SteadyDetectorLatencyMilliseconds = new[] { 0d },
                },
                CreateScenario("S10", CreateS10Bounds()),
            },
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => assembler.Build(input));
    }

    private static ComicGeometryCandidateDetectorBenchmarkInput CreateInput()
    {
        var s9 = CreateS9Bounds();
        return new ComicGeometryCandidateDetectorBenchmarkInput
        {
            DetectorName = "research-gpu-candidate-detector",
            DetectorVersion = "0.0-local",
            ReproducibleCommand = "pwsh ./tools/run-candidate-detector-benchmark.ps1 -InputPath <detector-input.json> -OutputPath <report.json>",
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            Hardware = new ComicGeometryCandidateDetectorBenchmarkHardware
            {
                CpuName = "AMD Ryzen 7 5700X3D",
                CpuPhysicalCoreCount = 8,
                GpuName = "NVIDIA GeForce RTX 3080",
                GpuVramMegabytes = 10240,
                DriverVersion = "610.47",
            },
            Scenarios = new[]
            {
                CreateScenario("S9", s9.Concat(new[] { new BoundingBox(10, 10, 20, 20) }).ToArray()),
                CreateScenario("S10", CreateS10Bounds()),
            },
        };
    }

    private static ComicGeometryCandidateDetectorBenchmarkScenarioInput CreateScenario(
        string scenarioId,
        IReadOnlyList<BoundingBox> detectedBounds)
    {
        var expectedBounds = string.Equals(scenarioId, "S9", StringComparison.Ordinal)
            ? CreateS9Bounds()
            : CreateS10Bounds();

        return new ComicGeometryCandidateDetectorBenchmarkScenarioInput
        {
            ScenarioId = scenarioId,
            SourceImageId = $"{scenarioId}-owner-manga-page",
            EvidenceArtifactPath = $"outputs/track-d-gpu-candidate-detector/{scenarioId}-evidence.png",
            SavedOcrZoneBounds = new BoundingBox(0, 0, 1200, 1500),
            ExpectedReferenceBounds = expectedBounds,
            DetectedCandidateBounds = detectedBounds,
            VisibleTextDurationSeconds = 3.5d,
            ColdDetectorLatencyMilliseconds = new[] { 200d },
            SteadyDetectorLatencyMilliseconds = new[] { 10d, 20d, 30d, 40d, 50d },
            EndToEndRuns = new[]
            {
                new ComicGeometryCandidateDetectorEndToEndRun(300d, true),
                new ComicGeometryCandidateDetectorEndToEndRun(400d, true),
                new ComicGeometryCandidateDetectorEndToEndRun(500d, false),
            },
            ResourceSamples = new[]
            {
                new ComicGeometryCandidateDetectorResourceSample(25d, 45d, 1800),
                new ComicGeometryCandidateDetectorResourceSample(35d, 60d, 2200),
            },
        };
    }

    private static IReadOnlyList<BoundingBox> CreateS9Bounds()
    {
        return new[]
        {
            new BoundingBox(606, 65, 85, 143),
            new BoundingBox(373, 82, 34, 118),
            new BoundingBox(188, 85, 61, 142),
            new BoundingBox(555, 430, 100, 102),
            new BoundingBox(452, 503, 28, 101),
            new BoundingBox(117, 537, 28, 48),
            new BoundingBox(588, 668, 63, 156),
            new BoundingBox(286, 793, 54, 124),
            new BoundingBox(102, 803, 34, 71),
            new BoundingBox(421, 937, 60, 73),
        };
    }

    private static IReadOnlyList<BoundingBox> CreateS10Bounds()
    {
        return new[]
        {
            new BoundingBox(999, 143, 87, 246),
            new BoundingBox(412, 545, 80, 97),
            new BoundingBox(250, 717, 52, 177),
            new BoundingBox(726, 939, 107, 154),
            new BoundingBox(336, 979, 61, 257),
            new BoundingBox(1011, 993, 66, 204),
        };
    }
}
