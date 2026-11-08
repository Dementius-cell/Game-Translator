namespace GameTranslator.Application.Ocr;

/// <summary>Application-owned readiness contract, independent of frame processing.</summary>
public interface IDetectorRuntime
{
    DetectorRuntimeStatus Status { get; }
    IReadOnlyList<DetectorRuntimeDiagnostic> Diagnostics => Array.Empty<DetectorRuntimeDiagnostic>();
    DetectorRuntimeDiagnostic? LastFailure => null;
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

/// <summary>Bounded safe runtime evidence; never carries paths, OCR text, stderr or exception messages.</summary>
public sealed record DetectorRuntimeDiagnostic(
    DateTimeOffset At, string Event, string Code, string Stage, long Generation, long RequestId,
    int? ProcessId, bool? ExitedBeforeTermination, int? ExitCode, double ElapsedMs,
    double RequestTimeoutMs, string? ExceptionType = null, int? HResult = null, int? NativeErrorCode = null);
