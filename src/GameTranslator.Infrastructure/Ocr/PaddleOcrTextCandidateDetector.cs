using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Abstractions;
using GameTranslator.Application.Settings;

namespace GameTranslator.Infrastructure.Ocr;

/// <summary>
/// Runs the approved PaddleOCR detector worker and returns only transient text candidate bounds.
/// </summary>
public sealed class PaddleOcrTextCandidateDetector : ITextCandidateDetector, IDetectorRuntime, IDisposable
{
    public const string DetectorId = "PaddleOCR-TextDetection";
    private const string SupportedPixelFormat = "Bgra32";
    private const int BytesPerPixel = 4;
    private readonly PaddleOcrTextCandidateDetectorOptions options;
    private readonly SemaphoreSlim workerLock = new(1, 1);
    private readonly JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web);
    private readonly StringBuilder standardError = new();
    private Process? worker;
    private volatile bool disposed;
    private readonly DetectorTimingLog? timingLog;
    private long nextRequestId;
    private long workerGeneration;
    private readonly CancellationTokenSource lifetime = new();
    private Task disposal = Task.CompletedTask;
    private readonly object initializationLock = new();
    private Task initialization = Task.CompletedTask;
    private volatile bool managedInitialization;
    private volatile DetectorRuntimeStatus runtimeStatus = new(DetectorRuntimeState.NotStarted);
    private DetectorRuntimeFailure startupFailure;
    private long initializationStarted;
    private readonly IApplicationLogger? logger;
    private readonly LiveDiagnosticsPreferences? diagnostics;
    private readonly object diagnosticLock = new();
    private readonly Queue<DetectorRuntimeDiagnostic> diagnosticHistory = new();
    private DetectorRuntimeDiagnostic? lastFailure;
    private string lastStartupStage = "starting";
    private int? lastProcessId;
    private bool? exitedBeforeTermination;
    private int? observedExitCode;
    public IReadOnlyList<DetectorRuntimeDiagnostic> Diagnostics { get { lock (diagnosticLock) return diagnosticHistory.ToArray(); } }
    public DetectorRuntimeDiagnostic? LastFailure { get { lock (diagnosticLock) return lastFailure; } }
    public DetectorRuntimeStatus Status
    {
        get
        {
            var status = runtimeStatus;
            return status.State == DetectorRuntimeState.Initializing
                ? status with { Elapsed = Stopwatch.GetElapsedTime(Interlocked.Read(ref initializationStarted)) }
                : status;
        }
    }
    internal Task TimingDiagnosticsCompletion => Task.WhenAll(disposal, timingLog?.Completion ?? Task.CompletedTask);

    public PaddleOcrTextCandidateDetector(PaddleOcrTextCandidateDetectorOptions options, IApplicationLogger? logger = null, LiveDiagnosticsPreferences? diagnostics = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger;
        this.diagnostics = diagnostics;
        ValidateOptions(this.options);
        if (options.TimingDiagnosticsEnabled) timingLog = new DetectorTimingLog(options.TimingDiagnosticsDirectory, preferences: diagnostics);
    }

    public async Task<TextCandidateDetectionResult> DetectAsync(
        TextCandidateDetectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (managedInitialization && Status.State != DetectorRuntimeState.Ready)
            return TextCandidateDetectionResult.Unavailable(DetectorId,
                Status.State == DetectorRuntimeState.Initializing
                    ? "OCR is preparing in the background."
                    : RuntimeUnavailableReason());
        var operation = DetectCoreAsync(request, cancellationToken);
        try
        {
            return await operation.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller stops immediately. The owner below still drains this exact response
            // before another request may use the pipe or remove the input bitmap.
            _ = operation.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    public Task InitializeAsync(bool retry = false)
    {
        lock (initializationLock)
        {
            if (disposed) return Task.CompletedTask;
            managedInitialization = true;
            if (!initialization.IsCompleted || Status.State == DetectorRuntimeState.Ready
                || (Status.State == DetectorRuntimeState.Unavailable && !retry)) return initialization;
            Interlocked.Exchange(ref initializationStarted, Stopwatch.GetTimestamp());
            runtimeStatus = new(DetectorRuntimeState.Initializing);
            RecordDiagnostic("initialization", retry ? "explicit-retry" : "requested");
            initialization = Task.Run(InitializeCoreAsync);
            return initialization;
        }
    }

    public Task StopAsync()
    {
        lock (initializationLock)
        {
            Dispose();
            return Task.WhenAll(initialization, disposal);
        }
    }

    private async Task InitializeCoreAsync()
    {
        var held = false;
        try
        {
            await workerLock.WaitAsync(lifetime.Token);
            held = true;
            lifetime.Token.ThrowIfCancellationRequested();
            if (!IsConfigured(out _))
            {
                RecordDiagnostic("failure", !File.Exists(options.PythonExecutablePath) ? "runtime-missing" : "worker-script-missing", failure: true);
                runtimeStatus = new(DetectorRuntimeState.Unavailable, DetectorRuntimeFailure.RuntimeMissing);
                return;
            }
            startupFailure = DetectorRuntimeFailure.InitializationFailed;
            var process = await EnsureWorkerAsync(new DetectorTimingEntry { Outcome = "startup" }, lifetime.Token);
            runtimeStatus = process is null
                ? new(DetectorRuntimeState.Unavailable, startupFailure)
                : new(DetectorRuntimeState.Ready);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            runtimeStatus = new(DetectorRuntimeState.Stopped);
        }
        catch (Exception exception)
        {
            // A runtime/driver/process failure must not escape into application startup.
            // Raw exception text can contain local paths; expose only the fixed category.
            runtimeStatus = new(DetectorRuntimeState.Unavailable, DetectorRuntimeFailure.InitializationFailed);
            RecordDiagnostic("failure", "initialization-exception",
                elapsedMs: Stopwatch.GetElapsedTime(Interlocked.Read(ref initializationStarted)).TotalMilliseconds,
                exception: exception, failure: true);
        }
        finally
        {
            runtimeStatus = runtimeStatus with { Elapsed = Stopwatch.GetElapsedTime(Interlocked.Read(ref initializationStarted)) };
            if (held)
            {
                if (Status.State != DetectorRuntimeState.Ready || disposed) TerminateWorker("initialization-ended");
                workerLock.Release();
            }
            if (disposed) runtimeStatus = new(DetectorRuntimeState.Stopped);
        }
    }

    private async Task<TextCandidateDetectionResult> DetectCoreAsync(
        TextCandidateDetectionRequest request,
        CancellationToken callerToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(disposed, this);
        using var callerLifetime = CancellationTokenSource.CreateLinkedTokenSource(callerToken, lifetime.Token);
        var cancellationToken = callerLifetime.Token;
        cancellationToken.ThrowIfCancellationRequested();

        var timing = new DetectorTimingEntry
        {
            RequestId = Interlocked.Increment(ref nextRequestId),
            CapturedAt = request.Frame.CapturedAt,
            SourceId = request.Frame.Origin?.SourceId,
            SourceTimestampMs = request.Frame.Origin?.SourceTimestamp.TotalMilliseconds,
            CachedReuse = request.Frame.IsCachedReuse,
            Width = request.Frame.Width,
            Height = request.Frame.Height,
        };
        var requestStarted = Stopwatch.GetTimestamp();
        var lockHeld = false;
        var responseDrained = false;
        Exception? requestException = null;
        string? workerErrorCode = null;
        var exchangePhase = "not-started";
        try
        {
            if (!IsConfigured(out var unavailableReason))
            {
                timing.Outcome = "not-configured";
                return TextCandidateDetectionResult.Unavailable(DetectorId, unavailableReason);
            }

            if (!string.Equals(request.Frame.PixelFormat, SupportedPixelFormat, StringComparison.OrdinalIgnoreCase))
            {
                timing.Outcome = "unsupported-pixels";
                return TextCandidateDetectionResult.Unavailable(
                    DetectorId,
                    $"PaddleOCR candidate detection requires {SupportedPixelFormat} captured frames.");
            }

            var preset = PaddleTextDetectionPresetResolver.Resolve(
                request.DetectorPreset,
                request.Language,
                request.OrientationMode);

            var lockStarted = Stopwatch.GetTimestamp();
            try { await workerLock.WaitAsync(cancellationToken); }
            finally { timing.LockWaitMs = Stopwatch.GetElapsedTime(lockStarted).TotalMilliseconds; }
            lockHeld = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (managedInitialization && Status.State != DetectorRuntimeState.Ready)
            {
                timing.Outcome = "runtime-unavailable";
                return TextCandidateDetectionResult.Unavailable(DetectorId, RuntimeUnavailableReason());
            }
            if (worker is { HasExited: true })
            {
                // Do not hide a persistent-worker loss by charging the next live frame for a
                // replacement startup. Report it for this frame; the next scheduled capture
                // can start a replacement worker and continue normal candidate processing.
                timing.Outcome = "worker-exited";
                TerminateWorker("worker-exited", timing.RequestId);
                return TextCandidateDetectionResult.Unavailable(
                    DetectorId,
                    managedInitialization
                        ? "OCR worker stopped. Use Retry OCR to initialize it again."
                        : "PaddleOCR candidate detector worker exited; it will restart on the next capture.");
            }

            timing.Outcome = "startup";
            var startupStarted = Stopwatch.GetTimestamp();
            Process? process;
            try { process = await EnsureWorkerAsync(timing, cancellationToken); }
            finally { timing.StartupMs = Stopwatch.GetElapsedTime(startupStarted).TotalMilliseconds; }
            if (process is null)
            {
                timing.Outcome = "startup-unavailable";
                workerErrorCode = LastFailure?.Code;
                return TextCandidateDetectionResult.Unavailable(DetectorId, GetWorkerFailureReason());
            }

            timing.Outcome = "bitmap";
            var bitmapStarted = Stopwatch.GetTimestamp();
            var inputPath = CreateTemporaryBitmap(request.Frame);
            timing.BitmapMs = Stopwatch.GetElapsedTime(bitmapStarted).TotalMilliseconds;
            try
            {
                timing.Outcome = "exchange";
                var exchangeStarted = Stopwatch.GetTimestamp();
                // Keep the request deadline independent: a late caller cancellation
                // may shorten the drain, but must never extend this deadline.
                using var requestDeadline = new CancellationTokenSource(options.RequestTimeout);
                using var exchangeLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, requestDeadline.Token);
                using var cancellationRegistration = callerToken.Register(
                    () => exchangeLifetime.CancelAfter(options.CancelledResponseDrainTimeout));
                exchangePhase = "request-write";
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(
                    new PaddleWorkerRequest(
                        inputPath,
                        preset.Threshold,
                        preset.BoxThreshold,
                        preset.UnclipRatio,
                        timing.RequestId),
                    serializerOptions).AsMemory(), exchangeLifetime.Token);
                await process.StandardInput.FlushAsync(exchangeLifetime.Token);
                exchangePhase = "response-read";

                PaddleWorkerResponse? response;
                try { response = await ReadWorkerResponseAsync(process, exchangeLifetime.Token); }
                finally { timing.ExchangeMs = Stopwatch.GetElapsedTime(exchangeStarted).TotalMilliseconds; }
                timing.Worker = response?.Timing;
                timing.ResponseRequestId = response?.RequestId;
                if (response is null || !string.Equals(response.Status, "ok", StringComparison.OrdinalIgnoreCase)
                    || response.RequestId != timing.RequestId)
                {
                    timing.Outcome = response is null ? "response-ended"
                        : response.RequestId != timing.RequestId ? "response-id-mismatch" : "worker-error";
                    workerErrorCode = SafeWorkerErrorCode(response?.ErrorCode);
                    TerminateWorker(timing.Outcome, timing.RequestId);
                    return TextCandidateDetectionResult.Unavailable(
                        DetectorId,
                        $"OCR detector failed ({timing.Outcome}; {workerErrorCode}). See OCR runtime diagnostics.");
                }

                responseDrained = true;
                cancellationToken.ThrowIfCancellationRequested();

                var candidates = response.Candidates
                    .Select(CreateCandidate)
                    .Where(candidate => candidate is not null)
                    .Cast<TextCandidate>()
                    .ToArray();
                timing.Outcome = "ok";
                timing.Candidates = candidates.Length;
                return TextCandidateDetectionResult.Available(
                    DetectorId,
                    candidates,
                    preset.CreateDiagnostics(candidates));
            }
            finally
            {
                TryDelete(inputPath);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            timing.Outcome = responseDrained ? "cancelled-drained"
                : disposed ? "disposed-" + timing.Outcome
                : lockHeld && timing.Outcome == "exchange" ? "cancelled-drain-timeout"
                : lockHeld ? "cancelled-" + timing.Outcome : "cancelled-lock-wait";
            if (lockHeld && (!responseDrained || disposed)) TerminateWorker(timing.Outcome, timing.RequestId);
            throw;
        }
        catch (OperationCanceledException)
        {
            timing.Outcome = "timeout-" + timing.Outcome;
            if (lockHeld) TerminateWorker(timing.Outcome, timing.RequestId);
            return TextCandidateDetectionResult.Unavailable(DetectorId,
                timing.Outcome == "timeout-exchange"
                    ? managedInitialization
                        ? "OCR request timed out. Use Retry OCR to initialize it again."
                        : "PaddleOCR candidate detector response timed out; it will restart on the next capture."
                    : timing.Outcome == "timeout-startup"
                        ? "PaddleOCR candidate detector startup timed out; it will restart on the next capture."
                        : GetWorkerFailureReason());
        }
        catch (Exception exception) when (exception is IOException
            or InvalidOperationException
            or JsonException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            requestException = exception;
            timing.Outcome = exception.GetType().Name + "-" + timing.Outcome;
            if (lockHeld) TerminateWorker(timing.Outcome, timing.RequestId);
            return TextCandidateDetectionResult.Unavailable(DetectorId, GetWorkerFailureReason());
        }
        finally
        {
            timing.Generation = workerGeneration;
            timing.TotalMs = Stopwatch.GetElapsedTime(requestStarted).TotalMilliseconds;
            timingLog?.Write(timing);
            if (lockHeld && !disposed && (timing.Outcome == "cancelled-drain-timeout"
                || timing.Outcome is not ("ok" or "runtime-unavailable") && !timing.Outcome.StartsWith("cancelled", StringComparison.Ordinal)
                && !timing.Outcome.StartsWith("disposed", StringComparison.Ordinal)))
                RecordDiagnostic("request-failure", workerErrorCode is null ? timing.Outcome : timing.Outcome + ":" + workerErrorCode,
                    timing.RequestId, timing.TotalMs, requestException, true,
                    exchangePhase == "not-started" ? lastStartupStage : exchangePhase);
            if (managedInitialization && lockHeld && worker is null && !disposed)
                runtimeStatus = new(DetectorRuntimeState.Unavailable, DetectorRuntimeFailure.WorkerStopped);
            if (lockHeld) workerLock.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        runtimeStatus = new(DetectorRuntimeState.Stopped);
        lifetime.Cancel();
        disposal = DisposeWorkerAsync();
    }

    private async Task DisposeWorkerAsync()
    {
        await workerLock.WaitAsync();
        try { TerminateWorker("disposed"); }
        finally
        {
            timingLog?.Dispose();
            workerLock.Release();
        }
        // Waiters may still be unwinding their cancelled waits. Do not dispose their semaphore.
    }

    private bool IsConfigured(out string unavailableReason)
    {
        if (!File.Exists(options.PythonExecutablePath))
        {
            unavailableReason = "PaddleOCR Python runtime is not packaged for the candidate-region pipeline.";
            return false;
        }

        if (!File.Exists(options.WorkerScriptPath))
        {
            unavailableReason = "PaddleOCR detector worker is not packaged for the candidate-region pipeline.";
            return false;
        }

        unavailableReason = string.Empty;
        return true;
    }

    private async Task<Process?> EnsureWorkerAsync(DetectorTimingEntry timing, CancellationToken cancellationToken)
    {
        if (worker is { HasExited: false })
        {
            return worker;
        }

        TerminateWorker("replace", timing.RequestId);
        lock (standardError) standardError.Clear();
        lastStartupStage = "starting";
        lastProcessId = null;
        exitedBeforeTermination = null;
        observedExitCode = null;
        var startInfo = new ProcessStartInfo
        {
            FileName = options.PythonExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            WorkingDirectory = Path.GetDirectoryName(options.WorkerScriptPath)!,
        };
        startInfo.ArgumentList.Add(options.WorkerScriptPath);
        startInfo.ArgumentList.Add("--worker");
        startInfo.ArgumentList.Add("--progress");
        if (timingLog is not null) startInfo.ArgumentList.Add("--timings");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.ErrorDataReceived += OnStandardError;
        try
        {
            if (!process.Start())
            {
                RecordDiagnostic("failure", "process-start-failed", timing.RequestId, failure: true);
                process.Dispose();
                return null;
            }
        }
        catch { process.Dispose(); throw; }

        worker = process;
        workerGeneration++;
        lastProcessId = process.Id;
        process.BeginErrorReadLine();
        RecordDiagnostic("worker-start", "started", timing.RequestId);
        timingLog?.Write(new DetectorTimingEntry { Kind = "worker-start", RequestId = timing.RequestId, Generation = workerGeneration, Outcome = "started" });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!managedInitialization) timeout.CancelAfter(options.StartupTimeout);
        var startupClock = Stopwatch.GetTimestamp();
        var recordedStages = new HashSet<string>(StringComparer.Ordinal);
        PaddleWorkerResponse? ready;
        do
        {
            timeout.Token.ThrowIfCancellationRequested();
            ready = await ReadWorkerResponseAsync(process, timeout.Token);
            if (ready?.Status != "startup-progress") break;
            // Only fixed stage codes are recorded, once per worker. Never log arbitrary worker text.
            if (ready.Stage is "import-cv2" or "import-numpy" or "import-paddle" or "import-paddlex"
                or "configure-model" or "create-predictor" or "configure-processors"
                && recordedStages.Add(ready.Stage))
            {
                lastStartupStage = ready.Stage;
                RecordDiagnostic("startup-stage", ready.Stage, timing.RequestId, Stopwatch.GetElapsedTime(startupClock).TotalMilliseconds);
                if (managedInitialization)
                    runtimeStatus = runtimeStatus with { Stage = ready.Stage is "configure-model" or "create-predictor" or "configure-processors"
                        ? DetectorRuntimeStage.LoadingModel : DetectorRuntimeStage.LoadingLibraries };
                timingLog?.Write(new DetectorTimingEntry
                {
                    Kind = "worker-startup-stage", RequestId = timing.RequestId,
                    Generation = workerGeneration, Outcome = ready.Stage,
                    StartupMs = Stopwatch.GetElapsedTime(startupClock).TotalMilliseconds,
                });
            }
        } while (true);
        if (ready is null || !string.Equals(ready.Status, "ready", StringComparison.OrdinalIgnoreCase))
        {
            startupFailure = ready?.ErrorCode == "cuda-unavailable"
                ? DetectorRuntimeFailure.CudaUnavailable : DetectorRuntimeFailure.InitializationFailed;
            RecordDiagnostic("failure", ready is null ? "startup-response-ended" : SafeWorkerErrorCode(ready.ErrorCode),
                timing.RequestId, Stopwatch.GetElapsedTime(startupClock).TotalMilliseconds, failure: true);
            TerminateWorker("startup-response-invalid", timing.RequestId);
            return null;
        }

        timingLog?.Write(new DetectorTimingEntry { Kind = "worker-ready", RequestId = timing.RequestId, Generation = workerGeneration, Outcome = "ready", Worker = ready.Timing });
        RecordDiagnostic("worker-ready", "ready", timing.RequestId, Stopwatch.GetElapsedTime(startupClock).TotalMilliseconds);
        return process;
    }

    private async Task<PaddleWorkerResponse?> ReadWorkerResponseAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(cancellationToken);
            if (line is null)
            {
                return null;
            }

            try
            {
                var response = JsonSerializer.Deserialize<PaddleWorkerResponse>(line, serializerOptions);
                if (!string.IsNullOrWhiteSpace(response?.Status))
                {
                    return response;
                }
            }
            catch (JsonException)
            {
            }
        }

    }

    private string CreateTemporaryBitmap(CapturedFrame frame)
    {
        var directory = Path.Combine(Path.GetTempPath(), "GameTranslator", "candidate-detector");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.bmp");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        var imageSize = checked(frame.Width * frame.Height * BytesPerPixel);
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(checked(54 + imageSize));
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(frame.Width);
        writer.Write(frame.Height);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(0);
        writer.Write(imageSize);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        var rowLength = checked(frame.Width * BytesPerPixel);
        var pixels = frame.PixelData.Span;
        for (var row = frame.Height - 1; row >= 0; row--)
        {
            writer.Write(pixels.Slice(checked(row * frame.Stride), rowLength));
        }

        return path;
    }

    private static TextCandidate? CreateCandidate(PaddleWorkerCandidate candidate)
    {
        if (candidate.Width <= 0 || candidate.Height <= 0 || !double.IsFinite(candidate.Confidence))
        {
            return null;
        }

        try
        {
            return new TextCandidate(
                new BoundingBox(candidate.X, candidate.Y, candidate.Width, candidate.Height),
                Math.Clamp(candidate.Confidence, 0d, 1d));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private void OnStandardError(object sender, DataReceivedEventArgs eventArgs)
    {
        if (diagnostics?.Current.Enabled == false || string.IsNullOrWhiteSpace(eventArgs.Data))
        {
            return;
        }

        lock (standardError)
        {
            if (standardError.Length < 2048)
            {
                standardError.Append(eventArgs.Data.AsSpan(0, Math.Min(eventArgs.Data.Length, 2048 - standardError.Length)));
            }
        }
    }

    private string GetWorkerFailureReason()
    {
        lock (standardError)
        {
            return standardError.Length == 0
                ? "PaddleOCR candidate detector is unavailable."
                : "PaddleOCR candidate detector is unavailable; inspect local runtime diagnostics.";
        }
    }

    private void TerminateWorker(string reason, long requestId = 0)
    {
        var process = worker;
        worker = null;
        if (process is null)
        {
            return;
        }

        ObserveProcess(process);
        RecordDiagnostic("worker-stop", reason, requestId);

        timingLog?.Write(new DetectorTimingEntry { Kind = "worker-stop", RequestId = requestId, Generation = workerGeneration, Outcome = reason });
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string SafeWorkerErrorCode(string? code) => code is
        "cuda-unavailable" or "initialization-failed" or "model-missing" or "dependency-missing"
        or "native-library-load-failed" or "gpu-out-of-memory" or "gpu-execution-failed"
        or "input-read-failed" or "permission-denied" or "memory-exhausted" or "inference-failed" ? code : "worker-error-unspecified";

    private string RuntimeUnavailableReason() => LastFailure is { } failure
        ? $"OCR is unavailable ({failure.Code}, stage {failure.Stage}, request {failure.RequestId}). Use Retry OCR; see OCR runtime diagnostics."
        : "OCR is unavailable. Check the OCR status and retry initialization.";

    private void ObserveProcess(Process? process)
    {
        if (process is null) return;
        try
        {
            lastProcessId = process.Id;
            exitedBeforeTermination = process.HasExited;
            observedExitCode = exitedBeforeTermination == true ? process.ExitCode : null;
        }
        catch (InvalidOperationException) { }
    }

    private void RecordDiagnostic(string kind, string code, long requestId = 0, double elapsedMs = 0,
        Exception? exception = null, bool failure = false, string? stage = null)
    {
        if (diagnostics?.Current.Enabled == false) return;
        ObserveProcess(worker);
        var entry = new DetectorRuntimeDiagnostic(DateTimeOffset.UtcNow, kind, code, stage ?? lastStartupStage,
            workerGeneration, requestId, lastProcessId, exitedBeforeTermination, observedExitCode, elapsedMs,
            options.RequestTimeout.TotalMilliseconds, exception?.GetType().FullName, exception?.HResult,
            (exception as System.ComponentModel.Win32Exception)?.NativeErrorCode);
        lock (diagnosticLock)
        {
            if (diagnosticHistory.Count == 64) diagnosticHistory.Dequeue();
            diagnosticHistory.Enqueue(entry);
            if (failure) lastFailure = entry;
        }
        // The ordinary rolling application log is available without detector-timing.enabled.
        // A failed sink must never change readiness, timeout or cancellation behavior.
        try
        {
            var message = "OCR runtime: " + JsonSerializer.Serialize(entry);
            if (failure) logger?.Warning(message); else logger?.Information(message);
        }
        catch (Exception) { Trace.TraceWarning("OCR diagnostic log sink failed."); }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void ValidateOptions(PaddleOcrTextCandidateDetectorOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PythonExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkerScriptPath);
        if (options.StartupTimeout != Timeout.InfiniteTimeSpan && options.StartupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Worker startup timeout must be positive.");
        }
        if (options.CancelledResponseDrainTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Cancelled response drain timeout must be positive.");
        if (options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Worker request timeout must be a positive finite timer duration.");
    }

    private sealed record PaddleWorkerRequest(
        string InputPath,
        double Threshold,
        double BoxThreshold,
        double UnclipRatio,
        long RequestId);

    private sealed class PaddleWorkerResponse
    {
        public string? Status { get; init; }

        public string? Stage { get; init; }

        public string? Error { get; init; }
        public string? ErrorCode { get; init; }

        public long? RequestId { get; init; }

        public DetectorWorkerTiming? Timing { get; init; }

        public IReadOnlyList<PaddleWorkerCandidate> Candidates { get; init; } = Array.Empty<PaddleWorkerCandidate>();
    }

    private sealed record PaddleWorkerCandidate(int X, int Y, int Width, int Height, double Confidence);
}

public sealed record PaddleOcrTextCandidateDetectorOptions
{
    public string PythonExecutablePath { get; init; } = Path.Combine(
        AppContext.BaseDirectory,
        "candidate-detector",
        "python.exe");

    public string WorkerScriptPath { get; init; } = Path.Combine(
        AppContext.BaseDirectory,
        "candidate-detector",
        "paddle_text_detector_worker.py");

    public bool TimingDiagnosticsEnabled { get; init; } = File.Exists(
        Path.Combine(AppContext.BaseDirectory, "detector-timing.enabled"));

    public string TimingDiagnosticsDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameTranslator", "Diagnostics", "Detector");

    // Explicit finite values are retained for diagnostics; production readiness is cancellable, not timed out.
    public TimeSpan StartupTimeout { get; init; } = Timeout.InfiniteTimeSpan;

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan CancelledResponseDrainTimeout { get; init; } = TimeSpan.FromSeconds(2);
}
