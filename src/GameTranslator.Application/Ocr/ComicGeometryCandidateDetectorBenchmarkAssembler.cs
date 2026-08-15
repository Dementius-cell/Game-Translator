namespace GameTranslator.Application.Ocr;

/// <summary>
/// Assembles research-only candidate-detector benchmark measurements into the ADR-023 report contract.
/// </summary>
public sealed class ComicGeometryCandidateDetectorBenchmarkAssembler
{
    private readonly ComicGeometryQualityGate qualityGate;

    public ComicGeometryCandidateDetectorBenchmarkAssembler()
        : this(new ComicGeometryQualityGate())
    {
    }

    public ComicGeometryCandidateDetectorBenchmarkAssembler(ComicGeometryQualityGate qualityGate)
    {
        ArgumentNullException.ThrowIfNull(qualityGate);
        this.qualityGate = qualityGate;
    }

    public ComicGeometryCandidateDetectorBenchmarkReport Build(
        ComicGeometryCandidateDetectorBenchmarkInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateReportMetadata(input);

        var scenarios = input.Scenarios
            .Select(BuildScenario)
            .ToArray();

        return new ComicGeometryCandidateDetectorBenchmarkReport
        {
            DetectorName = input.DetectorName,
            DetectorVersion = input.DetectorVersion,
            ReproducibleCommand = input.ReproducibleCommand,
            GeneratedAtUtc = input.GeneratedAtUtc,
            Hardware = input.Hardware,
            Scenarios = scenarios,
        };
    }

    private ComicGeometryCandidateDetectorBenchmarkScenario BuildScenario(
        ComicGeometryCandidateDetectorBenchmarkScenarioInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateScenarioMetadata(input);

        var expectedBounds = input.ExpectedReferenceBounds.ToArray();
        var candidateBounds = input.DetectedCandidateBounds.ToArray();
        ValidateBoundsStayWithinSavedZone(expectedBounds, input.SavedOcrZoneBounds, "expected reference");
        ValidateBoundsStayWithinSavedZone(candidateBounds, input.SavedOcrZoneBounds, "detected candidate");

        var quality = qualityGate.Evaluate(candidateBounds, expectedBounds);
        var coldLatency = SummarizeLatency(input.ColdDetectorLatencyMilliseconds, "cold detector latency");
        var steadyLatency = SummarizeLatency(input.SteadyDetectorLatencyMilliseconds, "steady detector latency");
        var endToEndRuns = input.EndToEndRuns.ToArray();
        var endToEndLatency = SummarizeLatency(
            endToEndRuns.Select(run => run.LatencyMilliseconds),
            "end-to-end latency");

        return new ComicGeometryCandidateDetectorBenchmarkScenario
        {
            ScenarioId = input.ScenarioId,
            SourceImageId = input.SourceImageId,
            EvidenceArtifactPath = input.EvidenceArtifactPath,
            ExpectedReferenceCount = quality.ExpectedCount,
            DetectedCandidateCount = quality.DetectedCount,
            MatchedReferenceCount = quality.MatchedCount,
            OutsideCandidateCount = quality.ExtraDetectionCount,
            VisibleTextDurationSeconds = input.VisibleTextDurationSeconds,
            EndToEndRunCount = endToEndRuns.Length,
            EndToEndCompletedWithinVisibleWindowCount = endToEndRuns.Count(run => run.CompletedWithinVisibleTextWindow),
            ColdDetectorLatency = coldLatency,
            SteadyDetectorLatency = steadyLatency,
            EndToEndLatency = endToEndLatency,
            ResourceUsage = SummarizeResources(input.ResourceSamples),
        };
    }

    private static void ValidateReportMetadata(ComicGeometryCandidateDetectorBenchmarkInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.DetectorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.DetectorVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ReproducibleCommand);
        ArgumentNullException.ThrowIfNull(input.Hardware);
        ArgumentNullException.ThrowIfNull(input.Scenarios);
    }

    private static void ValidateScenarioMetadata(ComicGeometryCandidateDetectorBenchmarkScenarioInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ScenarioId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.SourceImageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.EvidenceArtifactPath);
        ArgumentNullException.ThrowIfNull(input.ExpectedReferenceBounds);
        ArgumentNullException.ThrowIfNull(input.DetectedCandidateBounds);
        ArgumentNullException.ThrowIfNull(input.ColdDetectorLatencyMilliseconds);
        ArgumentNullException.ThrowIfNull(input.SteadyDetectorLatencyMilliseconds);
        ArgumentNullException.ThrowIfNull(input.EndToEndRuns);
        ArgumentNullException.ThrowIfNull(input.ResourceSamples);

        if (!double.IsFinite(input.VisibleTextDurationSeconds) || input.VisibleTextDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.VisibleTextDurationSeconds),
                "Visible text duration must be positive and finite.");
        }

        ValidateBounds(input.SavedOcrZoneBounds, nameof(input.SavedOcrZoneBounds));
    }

    private static void ValidateBoundsStayWithinSavedZone(
        IEnumerable<BoundingBox> bounds,
        BoundingBox savedOcrZoneBounds,
        string label)
    {
        foreach (var candidate in bounds)
        {
            if (candidate.X < savedOcrZoneBounds.X
                || candidate.Y < savedOcrZoneBounds.Y
                || candidate.Right > savedOcrZoneBounds.Right
                || candidate.Bottom > savedOcrZoneBounds.Bottom)
            {
                throw new ArgumentException(
                    $"{label} bounds must stay within the saved OCR zone.",
                    nameof(bounds));
            }
        }
    }

    private static void ValidateBounds(BoundingBox bounds, string parameterName)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Bounding box dimensions must be positive.");
        }
    }

    private static ComicGeometryCandidateDetectorLatencySummary SummarizeLatency(
        IEnumerable<double> samples,
        string label)
    {
        var orderedSamples = samples.ToArray();
        if (orderedSamples.Length == 0)
        {
            throw new ArgumentException($"{label} requires at least one sample.", nameof(samples));
        }

        if (orderedSamples.Any(sample => !double.IsFinite(sample) || sample <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(samples), $"{label} samples must be positive and finite.");
        }

        Array.Sort(orderedSamples);
        return new ComicGeometryCandidateDetectorLatencySummary
        {
            SampleCount = orderedSamples.Length,
            P50Milliseconds = CalculatePercentile(orderedSamples, 0.5d),
            P95Milliseconds = CalculatePercentile(orderedSamples, 0.95d),
        };
    }

    private static ComicGeometryCandidateDetectorResourceSummary SummarizeResources(
        IEnumerable<ComicGeometryCandidateDetectorResourceSample> samples)
    {
        var resourceSamples = samples.ToArray();
        if (resourceSamples.Length == 0)
        {
            throw new ArgumentException("Resource usage requires at least one sample.", nameof(samples));
        }

        foreach (var sample in resourceSamples)
        {
            if (!double.IsFinite(sample.CpuPercent) || sample.CpuPercent is < 0 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(samples), "CPU resource samples must be between 0 and 100.");
            }

            if (!double.IsFinite(sample.GpuPercent) || sample.GpuPercent is < 0 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(samples), "GPU resource samples must be between 0 and 100.");
            }

            if (sample.VramMegabytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(samples), "VRAM resource samples must not be negative.");
            }
        }

        return new ComicGeometryCandidateDetectorResourceSummary
        {
            PeakCpuPercent = resourceSamples.Max(sample => sample.CpuPercent),
            PeakGpuPercent = resourceSamples.Max(sample => sample.GpuPercent),
            PeakVramMegabytes = resourceSamples.Max(sample => sample.VramMegabytes),
        };
    }

    private static double CalculatePercentile(IReadOnlyList<double> sortedSamples, double percentile)
    {
        var index = (sortedSamples.Count - 1) * percentile;
        var lowerIndex = (int)Math.Floor(index);
        var upperIndex = (int)Math.Ceiling(index);
        if (lowerIndex == upperIndex)
        {
            return sortedSamples[lowerIndex];
        }

        var fraction = index - lowerIndex;
        return sortedSamples[lowerIndex]
            + (sortedSamples[upperIndex] - sortedSamples[lowerIndex]) * fraction;
    }
}

/// <summary>
/// Raw, research-only detector measurements collected outside the production OCR pipeline.
/// </summary>
public sealed record ComicGeometryCandidateDetectorBenchmarkInput
{
    public string DetectorName { get; init; } = string.Empty;

    public string DetectorVersion { get; init; } = string.Empty;

    public string ReproducibleCommand { get; init; } = string.Empty;

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public ComicGeometryCandidateDetectorBenchmarkHardware Hardware { get; init; } = new();

    public IReadOnlyList<ComicGeometryCandidateDetectorBenchmarkScenarioInput> Scenarios { get; init; } =
        Array.Empty<ComicGeometryCandidateDetectorBenchmarkScenarioInput>();
}

public sealed record ComicGeometryCandidateDetectorBenchmarkScenarioInput
{
    public string ScenarioId { get; init; } = string.Empty;

    public string SourceImageId { get; init; } = string.Empty;

    public string EvidenceArtifactPath { get; init; } = string.Empty;

    public BoundingBox SavedOcrZoneBounds { get; init; }

    public IReadOnlyList<BoundingBox> ExpectedReferenceBounds { get; init; } = Array.Empty<BoundingBox>();

    public IReadOnlyList<BoundingBox> DetectedCandidateBounds { get; init; } = Array.Empty<BoundingBox>();

    public double VisibleTextDurationSeconds { get; init; }

    public IReadOnlyList<double> ColdDetectorLatencyMilliseconds { get; init; } = Array.Empty<double>();

    public IReadOnlyList<double> SteadyDetectorLatencyMilliseconds { get; init; } = Array.Empty<double>();

    public IReadOnlyList<ComicGeometryCandidateDetectorEndToEndRun> EndToEndRuns { get; init; } =
        Array.Empty<ComicGeometryCandidateDetectorEndToEndRun>();

    public IReadOnlyList<ComicGeometryCandidateDetectorResourceSample> ResourceSamples { get; init; } =
        Array.Empty<ComicGeometryCandidateDetectorResourceSample>();
}

public sealed record ComicGeometryCandidateDetectorEndToEndRun(
    double LatencyMilliseconds,
    bool CompletedWithinVisibleTextWindow);

public sealed record ComicGeometryCandidateDetectorResourceSample(
    double CpuPercent,
    double GpuPercent,
    int VramMegabytes);
