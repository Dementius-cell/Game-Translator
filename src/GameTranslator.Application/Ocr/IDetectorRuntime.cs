namespace GameTranslator.Application.Ocr;

/// <summary>Application-owned readiness contract, independent of frame processing.</summary>
public interface IDetectorRuntime
{
    DetectorRuntimeStatus Status { get; }
    Task InitializeAsync(bool retry = false);
    Task StopAsync();
}

public enum DetectorRuntimeState { NotStarted, Initializing, Ready, Unavailable, Stopped }
public enum DetectorRuntimeFailure { None, RuntimeMissing, CudaUnavailable, InitializationFailed, WorkerStopped }
public enum DetectorRuntimeStage { Starting, LoadingLibraries, LoadingModel }
public sealed record DetectorRuntimeStatus(DetectorRuntimeState State,
    DetectorRuntimeFailure Failure = DetectorRuntimeFailure.None,
    DetectorRuntimeStage Stage = DetectorRuntimeStage.Starting,
    TimeSpan Elapsed = default);
