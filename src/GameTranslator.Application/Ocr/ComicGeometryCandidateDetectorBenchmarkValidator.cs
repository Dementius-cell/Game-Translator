namespace GameTranslator.Application.Ocr;

public sealed class ComicGeometryCandidateDetectorBenchmarkValidator
{
    public const int SupportedSchemaVersion = 1;
    public const int MinimumCpuPhysicalCores = 6;
    public const int MinimumGpuVramMegabytes = 8 * 1024;
    public const double MinimumVisibleTextScenarioSeconds = 3d;
    public const double MaximumVisibleTextScenarioSeconds = 4d;

    public ComicGeometryCandidateDetectorBenchmarkValidationResult Validate(
        ComicGeometryCandidateDetectorBenchmarkReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var errors = new List<string>();
        ValidateReportBoundaries(report, errors);
        ValidateHardware(report.Hardware, errors);
        ValidateScenarios(report.Scenarios, errors);

        return new ComicGeometryCandidateDetectorBenchmarkValidationResult(errors);
    }

    private static void ValidateReportBoundaries(
        ComicGeometryCandidateDetectorBenchmarkReport report,
        ICollection<string> errors)
    {
        if (report.SchemaVersion != SupportedSchemaVersion)
        {
            errors.Add($"schemaVersion must be {SupportedSchemaVersion}.");
        }

        if (!report.ResearchOnly)
        {
            errors.Add("report must be marked research-only.");
        }

        if (report.ProductionPipelineWired)
        {
            errors.Add("benchmark must not be wired into the production pipeline.");
        }

        if (report.SavedProfileSchemaChanged)
        {
            errors.Add("benchmark must not add or require persisted profile fields.");
        }

        if (report.IOcrEngineContractChanged)
        {
            errors.Add("benchmark must not change the IOcrEngine contract.");
        }

        if (report.UnconditionalFullFrameRetryEnabled)
        {
            errors.Add("benchmark must not enable unconditional full-frame retry.");
        }

        if (report.OutputScope != ComicGeometryCandidateDetectorOutputScope.TransientCandidateBoundsOnly)
        {
            errors.Add("candidate detector output must be transient candidate bounds only.");
        }

        if (report.CandidateScope != ComicGeometryCandidateDetectorCandidateScope.SavedOcrZoneOnly)
        {
            errors.Add("candidate detector proposals must stay inside the configured saved OCR zone.");
        }

        if (string.IsNullOrWhiteSpace(report.DetectorName))
        {
            errors.Add("detector name is required.");
        }

        if (string.IsNullOrWhiteSpace(report.DetectorVersion))
        {
            errors.Add("detector version is required.");
        }

        if (string.IsNullOrWhiteSpace(report.ReproducibleCommand))
        {
            errors.Add("reproducible benchmark command is required.");
        }
    }

    private static void ValidateHardware(
        ComicGeometryCandidateDetectorBenchmarkHardware hardware,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(hardware.CpuName))
        {
            errors.Add("CPU name is required.");
        }

        if (hardware.CpuPhysicalCoreCount < MinimumCpuPhysicalCores)
        {
            errors.Add($"CPU must have at least {MinimumCpuPhysicalCores} physical cores.");
        }

        if (string.IsNullOrWhiteSpace(hardware.GpuName))
        {
            errors.Add("GPU name is required.");
        }

        if (hardware.GpuVramMegabytes < MinimumGpuVramMegabytes)
        {
            errors.Add($"GPU VRAM must be at least {MinimumGpuVramMegabytes} MB.");
        }

        if (string.IsNullOrWhiteSpace(hardware.DriverVersion))
        {
            errors.Add("GPU driver version is required.");
        }
    }

    private static void ValidateScenarios(
        IReadOnlyList<ComicGeometryCandidateDetectorBenchmarkScenario> scenarios,
        ICollection<string> errors)
    {
        if (scenarios.Count == 0)
        {
            errors.Add("at least one benchmark scenario is required.");
            return;
        }

        var scenarioIds = scenarios
            .Select(scenario => scenario.ScenarioId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!scenarioIds.Contains("S9"))
        {
            errors.Add("S9 benchmark scenario is required.");
        }

        if (!scenarioIds.Contains("S10"))
        {
            errors.Add("S10 benchmark scenario is required.");
        }

        foreach (var scenario in scenarios)
        {
            ValidateScenario(scenario, errors);
        }
    }

    private static void ValidateScenario(
        ComicGeometryCandidateDetectorBenchmarkScenario scenario,
        ICollection<string> errors)
    {
        var prefix = string.IsNullOrWhiteSpace(scenario.ScenarioId)
            ? "scenario"
            : scenario.ScenarioId;

        if (string.IsNullOrWhiteSpace(scenario.ScenarioId))
        {
            errors.Add("scenario id is required.");
        }

        if (string.IsNullOrWhiteSpace(scenario.SourceImageId))
        {
            errors.Add($"{prefix}: source image id is required.");
        }

        if (string.IsNullOrWhiteSpace(scenario.EvidenceArtifactPath))
        {
            errors.Add($"{prefix}: evidence artifact path is required.");
        }

        if (scenario.ExpectedReferenceCount <= 0)
        {
            errors.Add($"{prefix}: expected reference count must be positive.");
        }

        if (scenario.DetectedCandidateCount < 0)
        {
            errors.Add($"{prefix}: detected candidate count must not be negative.");
        }

        if (scenario.MatchedReferenceCount < 0
            || scenario.MatchedReferenceCount > scenario.ExpectedReferenceCount)
        {
            errors.Add($"{prefix}: matched reference count must be between zero and expected reference count.");
        }

        if (scenario.OutsideCandidateCount < 0)
        {
            errors.Add($"{prefix}: outside candidate noise count must not be negative.");
        }

        if (scenario.VisibleTextDurationSeconds is < MinimumVisibleTextScenarioSeconds or > MaximumVisibleTextScenarioSeconds)
        {
            errors.Add($"{prefix}: visible text product scenario must be between 3 and 4 seconds.");
        }

        ValidateLatency($"{prefix}: cold detector latency", scenario.ColdDetectorLatency, errors);
        ValidateLatency($"{prefix}: steady detector latency", scenario.SteadyDetectorLatency, errors);
        ValidateLatency($"{prefix}: end-to-end latency", scenario.EndToEndLatency, errors);
        ValidateEndToEndCompletion(scenario, prefix, errors);
        ValidateResourceUsage(scenario.ResourceUsage, prefix, errors);
    }

    private static void ValidateLatency(
        string label,
        ComicGeometryCandidateDetectorLatencySummary latency,
        ICollection<string> errors)
    {
        if (latency.SampleCount <= 0)
        {
            errors.Add($"{label}: sample count must be positive.");
        }

        if (!IsPositiveFinite(latency.P50Milliseconds))
        {
            errors.Add($"{label}: P50 must be positive and finite.");
        }

        if (!IsPositiveFinite(latency.P95Milliseconds))
        {
            errors.Add($"{label}: P95 must be positive and finite.");
        }

        if (IsPositiveFinite(latency.P50Milliseconds)
            && IsPositiveFinite(latency.P95Milliseconds)
            && latency.P95Milliseconds < latency.P50Milliseconds)
        {
            errors.Add($"{label}: P95 must be greater than or equal to P50.");
        }
    }

    private static void ValidateEndToEndCompletion(
        ComicGeometryCandidateDetectorBenchmarkScenario scenario,
        string prefix,
        ICollection<string> errors)
    {
        if (scenario.EndToEndRunCount <= 0)
        {
            errors.Add($"{prefix}: end-to-end run count must be positive.");
        }

        if (scenario.EndToEndCompletedWithinVisibleWindowCount < 0
            || scenario.EndToEndCompletedWithinVisibleWindowCount > scenario.EndToEndRunCount)
        {
            errors.Add($"{prefix}: completed end-to-end count must be between zero and run count.");
        }
    }

    private static void ValidateResourceUsage(
        ComicGeometryCandidateDetectorResourceSummary resources,
        string prefix,
        ICollection<string> errors)
    {
        ValidatePercent($"{prefix}: peak CPU percent", resources.PeakCpuPercent, errors);
        ValidatePercent($"{prefix}: peak GPU percent", resources.PeakGpuPercent, errors);

        if (resources.PeakVramMegabytes < 0)
        {
            errors.Add($"{prefix}: peak VRAM MB must not be negative.");
        }
    }

    private static void ValidatePercent(
        string label,
        double value,
        ICollection<string> errors)
    {
        if (!double.IsFinite(value) || value is < 0 or > 100)
        {
            errors.Add($"{label} must be between 0 and 100.");
        }
    }

    private static bool IsPositiveFinite(double value)
    {
        return double.IsFinite(value) && value > 0;
    }
}

public sealed class ComicGeometryCandidateDetectorBenchmarkValidationResult
{
    public ComicGeometryCandidateDetectorBenchmarkValidationResult(IEnumerable<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors.ToArray();
    }

    public bool Passed => Errors.Count == 0;

    public IReadOnlyList<string> Errors { get; }
}
