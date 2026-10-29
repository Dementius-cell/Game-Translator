using System.IO;
using System.Text.Json;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Infrastructure.Ocr;

namespace GameTranslator.Tests.Infrastructure;

public sealed class DetectorTimingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detector_RequestDeadlineRetiresSilentWorkerAndAllowsRecovery(bool cancelCaller)
    {
        var directory = NewDirectory();
        using var detector = CreateDetector(directory, """
            [Console]::WriteLine('{"status":"ready"}')
            while ($line = [Console]::ReadLine()) {
                $request = $line | ConvertFrom-Json
                $signal = Join-Path $PSScriptRoot 'received'
                if (!(Test-Path $signal)) { [IO.File]::WriteAllText($signal, 'ready'); Start-Sleep -Seconds 20 }
                [Console]::WriteLine('{"status":"ok","requestId":' + $request.requestId + ',"candidates":[]}')
            }
            """, requestTimeout: TimeSpan.FromMilliseconds(300));
        using var cancellation = new CancellationTokenSource();
        var first = detector.DetectAsync(Request(), cancellation.Token);
        if (cancelCaller)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!File.Exists(Path.Combine(directory, "received"))) await Task.Delay(10, deadline.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        }
        else
        {
            var failed = await first.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal(TextCandidateDetectorAvailability.Unavailable, failed.Availability);
            Assert.Contains("response timed out", failed.UnavailableReason);
        }
        Assert.Equal(TextCandidateDetectorAvailability.Available,
            (await detector.DetectAsync(Request()).WaitAsync(TimeSpan.FromSeconds(8))).Availability);
        detector.Dispose();
        await detector.TimingDiagnosticsCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        var entries = Entries(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(directory, "logs")))));
        var request = Assert.Single(entries, e => e.Kind == "request" && e.RequestId == 1);
        Assert.Equal(cancelCaller ? "cancelled-drain-timeout" : "timeout-exchange", request.Outcome);
        Assert.InRange(request.ExchangeMs, 0, 1500); // Cancellation must not reset the deadline to the 2 s drain budget.
        Assert.Contains(entries, e => e.Kind == "request" && e.Outcome == "ok" && e.Generation == 2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4294967295d)]
    public void Detector_RejectsUnboundedOrInvalidRequestDeadline(double milliseconds)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new PaddleOcrTextCandidateDetector(
            new() { RequestTimeout = TimeSpan.FromMilliseconds(milliseconds), TimingDiagnosticsEnabled = false }));

    [Fact]
    public async Task Log_DrainsRecordsAndEnforcesByteLimit()
    {
        var directory = NewDirectory();
        var log = new DetectorTimingLog(directory, 1500);
        for (var id = 1; id <= 20; id++) log.Write(new() { RequestId = id, Outcome = "ok" });
        log.Dispose();
        await log.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var file = Assert.Single(Directory.GetFiles(directory));
        Assert.InRange(new FileInfo(file).Length, 1, 1500);
        Assert.All(File.ReadAllLines(file), line => Assert.Equal("ok", JsonDocument.Parse(line).RootElement.GetProperty("Outcome").GetString()));
    }

    [Fact]
    public async Task Log_ExistingFileLimitDoesNotCreateOrDeleteFiles()
    {
        var directory = NewDirectory();
        for (var i = 0; i < DetectorTimingLog.MaximumFiles; i++) File.WriteAllText(Path.Combine(directory, $"detector-{i}.jsonl"), "existing");
        var log = new DetectorTimingLog(directory);
        log.Write(new());
        log.Dispose();
        await log.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(DetectorTimingLog.MaximumFiles, Directory.GetFiles(directory).Length);
        Assert.All(Directory.GetFiles(directory), path => Assert.Equal("existing", File.ReadAllText(path)));
    }

    [Fact]
    public async Task Log_StorageFailureIsContained()
    {
        var file = Path.Combine(NewDirectory(), "file");
        File.WriteAllText(file, "not a directory");
        var log = new DetectorTimingLog(file);
        log.Write(new());
        log.Dispose();
        await log.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(log.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Detector_RecordsStagesAndReusesWorkerWithoutLoggingResponseText()
    {
        var directory = NewDirectory();
        using var detector = CreateDetector(directory, """
            [Console]::WriteLine('{"status":"ready","timing":{"importsMs":12,"totalMs":15}}')
            while ($line = [Console]::ReadLine()) {
                $request = $line | ConvertFrom-Json
                [Console]::WriteLine('{"status":"ok","requestId":' + $request.requestId + ',"timing":{"decodeMs":2,"totalMs":4},"candidates":[],"secret":"do-not-log"}')
            }
            """);
        Assert.Equal(TextCandidateDetectorAvailability.Available, (await detector.DetectAsync(Request())).Availability);
        Assert.Equal(TextCandidateDetectorAvailability.Available, (await detector.DetectAsync(Request())).Availability);
        detector.Dispose();
        await detector.TimingDiagnosticsCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        var text = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(directory, "logs"))));
        Assert.DoesNotContain("do-not-log", text);
        Assert.DoesNotContain("inputPath", text);
        var entries = Entries(text);
        Assert.Single(entries, e => e.Kind == "worker-start");
        Assert.Equal(12d, Assert.Single(entries, e => e.Kind == "worker-ready").Worker!.ImportsMs);
        var requests = entries.Where(e => e.Kind == "request").ToArray();
        Assert.Equal(2, requests.Length);
        Assert.All(requests, e => { Assert.Equal("ok", e.Outcome); Assert.Equal(e.RequestId, e.ResponseRequestId); Assert.Equal(1, e.Generation); Assert.Equal(2d, e.Worker!.DecodeMs); Assert.True(e.TotalMs >= e.ExchangeMs); });
        Assert.Contains(entries, e => e.Kind == "worker-stop" && e.Outcome == "disposed");
    }

    [Fact]
    public async Task Detector_StartupTimeoutIsDiagnosedAndStillReturnsUnavailable()
    {
        var directory = NewDirectory();
        using var detector = CreateDetector(directory, "Start-Sleep -Seconds 20", TimeSpan.FromMilliseconds(200));
        Assert.Equal(TextCandidateDetectorAvailability.Unavailable, (await detector.DetectAsync(Request())).Availability);
        detector.Dispose();
        await detector.TimingDiagnosticsCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        var entries = Entries(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(directory, "logs")))));
        Assert.Contains(entries, e => e.Kind == "request" && e.Outcome == "timeout-startup" && e.StartupMs > 0);
        Assert.Contains(entries, e => e.Kind == "worker-stop" && e.Outcome == "timeout-startup");
    }

    [Fact]
    public async Task Detector_ExplicitlyDisabledDoesNotCreateDiagnostics()
    {
        var directory = NewDirectory();
        using var detector = new PaddleOcrTextCandidateDetector(new() { TimingDiagnosticsEnabled = false, TimingDiagnosticsDirectory = Path.Combine(directory, "logs"), PythonExecutablePath = "missing-runtime", WorkerScriptPath = "missing-worker" });
        Assert.Equal(TextCandidateDetectorAvailability.Unavailable, (await detector.DetectAsync(Request())).Availability);
        detector.Dispose();
        await detector.TimingDiagnosticsCompletion;
        Assert.False(Directory.Exists(Path.Combine(directory, "logs")));
    }

    [Fact]
    public async Task Detector_CancellationWhileWaitingDoesNotKillActiveWorker()
    {
        var directory = NewDirectory();
        using var detector = CreateDetector(directory, """
            [Console]::WriteLine('{"status":"ready"}')
            while ($line = [Console]::ReadLine()) {
                $request = $line | ConvertFrom-Json
                [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'received'), 'ready')
                Start-Sleep -Milliseconds 500
                [Console]::WriteLine('{"status":"ok","requestId":' + $request.requestId + ',"candidates":[]}')
            }
            """);
        var active = detector.DetectAsync(Request());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!File.Exists(Path.Combine(directory, "received"))) await Task.Delay(10, deadline.Token);
        using var cancellation = new CancellationTokenSource();
        var waiting = detector.DetectAsync(Request(), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(TextCandidateDetectorAvailability.Available, (await active).Availability);
        detector.Dispose();
        await detector.TimingDiagnosticsCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        var entries = Entries(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(directory, "logs")))));
        Assert.Contains(entries, e => e.Kind == "request" && e.Outcome == "cancelled-lock-wait");
        Assert.DoesNotContain(entries, e => e.Kind == "worker-stop" && e.Outcome.StartsWith("cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Detector_ActiveCancellationRecordsStageAndRestartsOnNextRequest()
    {
        var directory = NewDirectory();
        using var detector = CreateDetector(directory, """
            [Console]::WriteLine('{"status":"ready"}')
            while ($line = [Console]::ReadLine()) {
                $request = $line | ConvertFrom-Json
                $signal = Join-Path $PSScriptRoot 'received'
                if (!(Test-Path $signal)) { [IO.File]::WriteAllText($signal, 'ready'); Start-Sleep -Seconds 20 }
                [Console]::WriteLine('{"status":"ok","requestId":' + $request.requestId + ',"candidates":[]}')
            }
            """);
        using var cancellation = new CancellationTokenSource();
        var active = detector.DetectAsync(Request(), cancellation.Token);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!File.Exists(Path.Combine(directory, "received"))) await Task.Delay(10, deadline.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        Assert.Equal(TextCandidateDetectorAvailability.Available, (await detector.DetectAsync(Request())).Availability);
        detector.Dispose();
        await detector.TimingDiagnosticsCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        var entries = Entries(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(directory, "logs")))));
        Assert.Contains(entries, e => e.Kind == "worker-stop" && e.Outcome == "cancelled-drain-timeout");
        Assert.Contains(entries, e => e.Kind == "request" && e.Outcome == "ok" && e.Generation == 2);
    }

    private static DetectorTimingEntry[] Entries(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonSerializer.Deserialize<DetectorTimingEntry>(line)!).ToArray();

    [Fact]
    public async Task Detector_CancelledResponseIsDrainedBeforeNextRequestAndBitmapSurvives()
    {
        var directory = NewDirectory();
        using var detector = CreateDetector(directory, """
            [Console]::WriteLine('{"status":"ready"}')
            while ($line = [Console]::ReadLine()) {
                $request = $line | ConvertFrom-Json
                if ($request.requestId -eq 1) {
                    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'received'), $request.inputPath)
                    while (!(Test-Path (Join-Path $PSScriptRoot 'release'))) { Start-Sleep -Milliseconds 10 }
                    if (!(Test-Path $request.inputPath)) { exit 1 }
                }
                [Console]::WriteLine('{"status":"ok","requestId":' + $request.requestId + ',"candidates":[{"x":' + $request.requestId + ',"y":0,"width":4,"height":4,"confidence":0.9}]}')
            }
            """);
        using var cancellation = new CancellationTokenSource();
        var first = detector.DetectAsync(Request(), cancellation.Token);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var signal = Path.Combine(directory, "received");
        while (!File.Exists(signal)) await Task.Delay(10, deadline.Token);
        var bitmap = File.ReadAllText(signal);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromMilliseconds(500)));
        Assert.True(File.Exists(bitmap));
        var second = detector.DetectAsync(Request());
        Assert.False(second.IsCompleted);
        File.WriteAllText(Path.Combine(directory, "release"), "ready");
        var result = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TextCandidateDetectorAvailability.Available, result.Availability);
        Assert.Equal(2, Assert.Single(result.Candidates).Bounds.X);
        Assert.False(File.Exists(bitmap));
        detector.Dispose();
        await detector.TimingDiagnosticsCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        var entries = Entries(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(directory, "logs")))));
        Assert.Single(entries, e => e.Kind == "worker-start");
        Assert.Contains(entries, e => e.RequestId == 1 && e.Outcome == "cancelled-drained");
        Assert.Contains(entries, e => e.RequestId == 2 && e.Outcome == "ok" && e.Generation == 1);
    }

    [Fact]
    public async Task Detector_WrongResponseIdIsNeverPublishedOrReused()
    {
        var directory = NewDirectory();
        using var detector = CreateDetector(directory, """
            [Console]::WriteLine('{"status":"ready"}')
            while ($line = [Console]::ReadLine()) {
                [Console]::WriteLine('{"status":"ok","requestId":999,"candidates":[{"x":1,"y":0,"width":4,"height":4,"confidence":0.9}]}')
            }
            """);
        var result = await detector.DetectAsync(Request());
        Assert.Equal(TextCandidateDetectorAvailability.Unavailable, result.Availability);
        Assert.Empty(result.Candidates);
        detector.Dispose();
        await detector.TimingDiagnosticsCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        var entries = Entries(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(directory, "logs")))));
        Assert.Contains(entries, e => e.Kind == "worker-stop" && e.Outcome == "response-id-mismatch");
    }

    [Fact]
    public async Task Detector_DisposeInterruptsDrainWithoutWaitingForItsDeadline()
    {
        var directory = NewDirectory();
        using var detector = CreateDetector(directory, """
            [Console]::WriteLine('{"status":"ready"}')
            while ($line = [Console]::ReadLine()) {
                [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'received'), 'ready')
                Start-Sleep -Seconds 20
            }
            """);
        using var cancellation = new CancellationTokenSource();
        var active = detector.DetectAsync(Request(), cancellation.Token);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!File.Exists(Path.Combine(directory, "received"))) await Task.Delay(10, deadline.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        detector.Dispose();
        await detector.TimingDiagnosticsCompletion.WaitAsync(TimeSpan.FromSeconds(1));
    }

    private static PaddleOcrTextCandidateDetector CreateDetector(string directory, string script, TimeSpan? timeout = null,
        TimeSpan? requestTimeout = null)
    {
        var scriptPath = Path.Combine(directory, "fake-worker.ps1");
        File.WriteAllText(scriptPath, script);
        return new(new()
        {
            PythonExecutablePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            WorkerScriptPath = scriptPath,
            StartupTimeout = timeout ?? TimeSpan.FromSeconds(5),
            RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5),
            TimingDiagnosticsEnabled = true,
            TimingDiagnosticsDirectory = Path.Combine(directory, "logs"),
        });
    }

    private static TextCandidateDetectionRequest Request() => new(new CapturedFrame(new(0, 0, 20, 10), 20, 10, 80, "Bgra32", new byte[800], DateTimeOffset.UtcNow), "eng", GameTranslator.Domain.Profiles.OcrOrientationMode.Horizontal, OcrLayoutMode.Menu);

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GameTranslator.Tests", "detector-timing", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
