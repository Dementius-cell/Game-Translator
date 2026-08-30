using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;

namespace GameTranslator.Infrastructure.Ocr;

/// <summary>
/// Runs the approved PaddleOCR detector worker and returns only transient text candidate bounds.
/// </summary>
public sealed class PaddleOcrTextCandidateDetector : ITextCandidateDetector, IDisposable
{
    public const string DetectorId = "PaddleOCR-TextDetection";
    private const string SupportedPixelFormat = "Bgra32";
    private const int BytesPerPixel = 4;
    private readonly PaddleOcrTextCandidateDetectorOptions options;
    private readonly SemaphoreSlim workerLock = new(1, 1);
    private readonly JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web);
    private readonly StringBuilder standardError = new();
    private Process? worker;
    private bool disposed;

    public PaddleOcrTextCandidateDetector(PaddleOcrTextCandidateDetectorOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        ValidateOptions(this.options);
    }

    public async Task<TextCandidateDetectionResult> DetectAsync(
        TextCandidateDetectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsConfigured(out var unavailableReason))
        {
            return TextCandidateDetectionResult.Unavailable(DetectorId, unavailableReason);
        }

        if (!string.Equals(request.Frame.PixelFormat, SupportedPixelFormat, StringComparison.OrdinalIgnoreCase))
        {
            return TextCandidateDetectionResult.Unavailable(
                DetectorId,
                $"PaddleOCR candidate detection requires {SupportedPixelFormat} captured frames.");
        }

        var preset = PaddleTextDetectionPresetResolver.Resolve(
            request.DetectorPreset,
            request.Language,
            request.OrientationMode);

        await workerLock.WaitAsync(cancellationToken);
        try
        {
            if (worker is { HasExited: true })
            {
                // Do not hide a persistent-worker loss by charging the next live frame for a
                // replacement startup. Report it for this frame; the next scheduled capture
                // can start a replacement worker and continue normal candidate processing.
                TerminateWorker();
                return TextCandidateDetectionResult.Unavailable(
                    DetectorId,
                    "PaddleOCR candidate detector worker exited; it will restart on the next capture.");
            }

            var process = await EnsureWorkerAsync(cancellationToken);
            if (process is null)
            {
                return TextCandidateDetectionResult.Unavailable(DetectorId, GetWorkerFailureReason());
            }

            var inputPath = CreateTemporaryBitmap(request.Frame);
            try
            {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(
                    new PaddleWorkerRequest(
                        inputPath,
                        preset.Threshold,
                        preset.BoxThreshold,
                        preset.UnclipRatio),
                    serializerOptions));
                await process.StandardInput.FlushAsync(cancellationToken);

                var response = await ReadWorkerResponseAsync(process, cancellationToken);
                if (response is null || !string.Equals(response.Status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    TerminateWorker();
                    return TextCandidateDetectionResult.Unavailable(
                        DetectorId,
                        string.IsNullOrWhiteSpace(response?.Error)
                            ? GetWorkerFailureReason()
                            : $"PaddleOCR candidate detector failed: {response.Error}");
                }

                var candidates = response.Candidates
                    .Select(CreateCandidate)
                    .Where(candidate => candidate is not null)
                    .Cast<TextCandidate>()
                    .ToArray();
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
            TerminateWorker();
            throw;
        }
        catch (OperationCanceledException)
        {
            TerminateWorker();
            return TextCandidateDetectionResult.Unavailable(DetectorId, GetWorkerFailureReason());
        }
        catch (Exception exception) when (exception is IOException
            or InvalidOperationException
            or JsonException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            TerminateWorker();
            return TextCandidateDetectionResult.Unavailable(DetectorId, GetWorkerFailureReason());
        }
        finally
        {
            workerLock.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        TerminateWorker();
        workerLock.Dispose();
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

    private async Task<Process?> EnsureWorkerAsync(CancellationToken cancellationToken)
    {
        if (worker is { HasExited: false })
        {
            return worker;
        }

        TerminateWorker();
        standardError.Clear();
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

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.ErrorDataReceived += OnStandardError;
        if (!process.Start())
        {
            process.Dispose();
            return null;
        }

        process.BeginErrorReadLine();
        worker = process;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.StartupTimeout);
        var ready = await ReadWorkerResponseAsync(process, timeout.Token);
        if (ready is null || !string.Equals(ready.Status, "ready", StringComparison.OrdinalIgnoreCase))
        {
            TerminateWorker();
            return null;
        }

        return process;
    }

    private async Task<PaddleWorkerResponse?> ReadWorkerResponseAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        while (!process.HasExited)
        {
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

        return null;
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
        if (string.IsNullOrWhiteSpace(eventArgs.Data))
        {
            return;
        }

        lock (standardError)
        {
            if (standardError.Length < 2048)
            {
                standardError.AppendLine(eventArgs.Data);
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

    private void TerminateWorker()
    {
        var process = worker;
        worker = null;
        if (process is null)
        {
            return;
        }

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
        if (options.StartupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Worker startup timeout must be positive.");
        }
    }

    private sealed record PaddleWorkerRequest(
        string InputPath,
        double Threshold,
        double BoxThreshold,
        double UnclipRatio);

    private sealed class PaddleWorkerResponse
    {
        public string? Status { get; init; }

        public string? Error { get; init; }

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

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
