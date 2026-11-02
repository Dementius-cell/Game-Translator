using System.IO;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;
using GameTranslator.Infrastructure.Ocr;

namespace GameTranslator.Tests.Infrastructure;

public sealed class DetectorRuntimeTests
{
    [Fact]
    public async Task BackgroundInitialization_IsSingleFlightAndFramesDoNotCancelOrRestartIt()
    {
        using var fixture = new WorkerFixture();
        using var detector = fixture.Create();
        var startup = detector.InitializeAsync();
        Assert.Same(startup, detector.InitializeAsync());
        await fixture.WaitForEntryAsync();
        using (var stageDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            while (detector.Status.Stage != DetectorRuntimeStage.LoadingLibraries)
                await Task.Delay(10, stageDeadline.Token);
        // The fake stays blocked past its explicit diagnostic startup timeout (20 ms).
        await Task.Delay(80);
        Assert.Equal(DetectorRuntimeState.Initializing, detector.Status.State);
        Assert.True(detector.Status.Elapsed > TimeSpan.Zero);
        for (var i = 0; i < 5; i++)
            Assert.Equal(TextCandidateDetectorAvailability.Unavailable, (await detector.DetectAsync(Request())).Availability);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => detector.DetectAsync(Request(), cancelled.Token));
        Assert.False(startup.IsCompleted);
        File.WriteAllText(fixture.Release, "ready");
        await startup.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(DetectorRuntimeState.Ready, detector.Status.State);
        var elapsed = detector.Status.Elapsed;
        Assert.True(elapsed > TimeSpan.Zero);
        await Task.Delay(30);
        Assert.Equal(elapsed, detector.Status.Elapsed);
        Assert.Equal(TextCandidateDetectorAvailability.Available, (await detector.DetectAsync(Request())).Availability);
        Assert.Single(File.ReadAllLines(fixture.Entries));
    }

    [Theory]
    [InlineData("cuda-unavailable", DetectorRuntimeFailure.CudaUnavailable)]
    [InlineData("initialization-failed", DetectorRuntimeFailure.InitializationFailed)]
    [InlineData("crash", DetectorRuntimeFailure.InitializationFailed)]
    public async Task StartupFailure_IsLatchedUntilExplicitRetry(string code, DetectorRuntimeFailure expected)
    {
        using var fixture = new WorkerFixture();
        File.WriteAllText(fixture.Failure, code);
        using var detector = fixture.Create();
        await detector.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(DetectorRuntimeState.Unavailable, detector.Status.State);
        Assert.Equal(expected, detector.Status.Failure);
        for (var i = 0; i < 3; i++)
        {
            await detector.InitializeAsync();
            Assert.Equal(TextCandidateDetectorAvailability.Unavailable, (await detector.DetectAsync(Request())).Availability);
        }
        Assert.Single(File.ReadAllLines(fixture.Entries));
        File.Delete(fixture.Failure);
        File.WriteAllText(fixture.Release, "ready");
        await detector.InitializeAsync(retry: true).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(DetectorRuntimeState.Ready, detector.Status.State);
        Assert.Equal(2, File.ReadAllLines(fixture.Entries).Length);
    }

    [Fact]
    public async Task Dispose_CancelsBlockedInitializationAndPreventsLateReady()
    {
        using var fixture = new WorkerFixture();
        var detector = fixture.Create();
        var startup = detector.InitializeAsync();
        await fixture.WaitForEntryAsync();
        await detector.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await startup.WaitAsync(TimeSpan.FromSeconds(5));
        await detector.TimingDiagnosticsCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DetectorRuntimeState.Stopped, detector.Status.State);
        await detector.InitializeAsync(retry: true);
        Assert.Single(File.ReadAllLines(fixture.Entries));
    }

    [Fact]
    public async Task MissingRuntime_IsRecoverableStatus()
    {
        using var detector = new PaddleOcrTextCandidateDetector(new() { PythonExecutablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".exe") });
        await detector.InitializeAsync();
        Assert.Equal(DetectorRuntimeState.Unavailable, detector.Status.State);
        Assert.Equal(DetectorRuntimeFailure.RuntimeMissing, detector.Status.Failure);
    }

    [Fact]
    public async Task ReadyWorkerRequestTimeout_IsFiniteAndRequiresExplicitRecovery()
    {
        using var fixture = new WorkerFixture();
        File.WriteAllText(fixture.Release, "ready");
        var silence = Path.Combine(Path.GetDirectoryName(fixture.Release)!, "silence");
        File.WriteAllText(silence, "silent request");
        using var detector = fixture.Create(TimeSpan.FromMilliseconds(100));
        await detector.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var result = await detector.DetectAsync(Request()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TextCandidateDetectorAvailability.Unavailable, result.Availability);
        Assert.Contains("Retry OCR", result.UnavailableReason);
        Assert.Equal(DetectorRuntimeState.Unavailable, detector.Status.State);
        Assert.Equal(DetectorRuntimeFailure.WorkerStopped, detector.Status.Failure);
        await detector.DetectAsync(Request());
        Assert.Single(File.ReadAllLines(fixture.Entries));
        File.Delete(silence);
        await detector.InitializeAsync(retry: true).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(DetectorRuntimeState.Ready, detector.Status.State);
        Assert.Equal(2, File.ReadAllLines(fixture.Entries).Length);
    }

    private static TextCandidateDetectionRequest Request() => new(new CapturedFrame(new(0,0,10,10),10,10,40,"Bgra32",new byte[400],DateTimeOffset.UtcNow),"eng",OcrOrientationMode.Horizontal,OcrLayoutMode.Menu);

    private sealed class WorkerFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "GameTranslator.Tests", "runtime", Guid.NewGuid().ToString("N"));
        public string Entries => Path.Combine(directory, "entries");
        public string Release => Path.Combine(directory, "release");
        public string Failure => Path.Combine(directory, "failure");
        public WorkerFixture()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"worker.ps1"), """
                Add-Content (Join-Path $PSScriptRoot 'entries') 'start'
                '{"status":"startup-progress","stage":"import-paddle"}'
                $failure = Join-Path $PSScriptRoot 'failure'
                if (Test-Path $failure) {
                    $code = (Get-Content $failure -Raw).Trim()
                    if ($code -eq 'crash') { exit 3 }
                    @{status='startup-error';errorCode=$code} | ConvertTo-Json -Compress
                    exit
                }
                while (-not (Test-Path (Join-Path $PSScriptRoot 'release'))) { Start-Sleep -Milliseconds 20 }
                '{"status":"ready"}'
                while ($null -ne ($line = [Console]::ReadLine())) {
                    if (Test-Path (Join-Path $PSScriptRoot 'silence')) { Start-Sleep -Seconds 1; continue }
                    $request = $line | ConvertFrom-Json
                    @{status='ok';requestId=$request.requestId;candidates=@()} | ConvertTo-Json -Compress
                }
                """);
        }
        public PaddleOcrTextCandidateDetector Create(TimeSpan? requestTimeout = null) => new(new()
        {
            PythonExecutablePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),
            WorkerScriptPath=Path.Combine(directory,"worker.ps1"), StartupTimeout=TimeSpan.FromMilliseconds(20),
            RequestTimeout=requestTimeout ?? TimeSpan.FromSeconds(5),
        });
        public async Task WaitForEntryAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(Entries)) await Task.Delay(20, deadline.Token);
        }
        public void Dispose() { try { Directory.Delete(directory,true); } catch (IOException) { } }
    }
}
