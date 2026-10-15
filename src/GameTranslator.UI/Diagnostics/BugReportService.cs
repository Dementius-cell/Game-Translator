using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GameTranslator.Application.Debug;

namespace GameTranslator.UI.Diagnostics;

public sealed class BugReportService : IBugReportService, IDisposable
{
    private readonly string applicationDirectory;
    private readonly string fallbackDirectory;
    private readonly string sessionId = Guid.NewGuid().ToString("N");
    private readonly List<string> recoveredReports = new();
    private FileStream? sessionLease;
    private string? sessionPath;
    private int fatalObserved;
    private bool started;

    public BugReportService(string applicationDirectory, string fallbackDirectory)
    {
        this.applicationDirectory = applicationDirectory;
        this.fallbackDirectory = fallbackDirectory;
        ReportsDirectory = Path.Combine(applicationDirectory, "Reports");
    }

    public string ReportsDirectory { get; private set; }
    public string? StorageWarning { get; private set; }
    public IReadOnlyList<string> RecoveredReports => recoveredReports;

    public void StartSession()
    {
        if (started) return;
        started = true;
        if (!TryPrepareDirectory(ReportsDirectory))
        {
            ReportsDirectory = fallbackDirectory;
            if (!TryPrepareDirectory(ReportsDirectory))
            {
                StorageWarning = "Не удалось создать папку отчётов. Проверьте доступ к папке приложения и свободное место.";
                return;
            }
            StorageWarning = "Папка приложения недоступна для записи. Отчёты сохраняются в: " + ReportsDirectory;
        }

        // Recover both locations: installation permissions may have changed since the last run.
        foreach (var directory in new[] { Path.Combine(applicationDirectory, "Reports"), fallbackDirectory }
            .Distinct(StringComparer.OrdinalIgnoreCase))
            RecoverSessions(directory);
        try
        {
            sessionPath = Path.Combine(ReportsDirectory, $"session-{sessionId}.json");
            sessionLease = new FileStream(sessionPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new SessionInfo(Version, DateTimeOffset.UtcNow));
            sessionLease.Write(bytes);
            sessionLease.Flush(true);
        }
        catch (Exception exception)
        {
            TraceFailure(exception);
            StorageWarning = "Не удалось включить обнаружение аварийного завершения. Проверьте доступ и свободное место: " + ReportsDirectory;
            sessionLease?.Dispose();
            sessionLease = null;
            try { if (sessionPath is not null) File.Delete(sessionPath); }
            catch (Exception cleanupException) { TraceFailure(cleanupException); }
        }
    }

    public Task<BugReportResult> CreateBugReportAsync(BugReportSnapshot snapshot) => Task.Run(() =>
    {
        var text = Header("Manual bug report");
        text.AppendLine($"Workspace tab: {snapshot.WorkspaceTab}");
        text.AppendLine($"OCR zone count: {snapshot.ZoneCount}");
        text.AppendLine($"Live running: {snapshot.IsLiveRunning}");
        text.AppendLine($"Profile validation errors: {snapshot.HasValidationErrors}");
        return SaveReport($"bug-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.txt", text.ToString());
    });

    public void CaptureCrash(Exception exception)
    {
        if (Interlocked.Exchange(ref fatalObserved, 1) != 0) return;
        try
        {
            var text = Header("Unhandled exception");
            var current = exception;
            for (var depth = 0; current is not null && depth < 8; depth++, current = current.InnerException)
            {
                text.AppendLine($"Exception {depth}: {current.GetType().FullName}; HResult: 0x{current.HResult:X8}");
                // Messages/Data/source paths may contain credentials or user content. Keep method names only.
                foreach (var frame in new StackTrace(current, false).GetFrames().Take(64))
                {
                    var method = frame.GetMethod();
                    text.AppendLine($"  at {method?.DeclaringType?.FullName}.{method?.Name}");
                }
            }
            var result = SaveReport($"crash-{sessionId}.txt", text.ToString());
            if (!result.Succeeded) Trace.TraceError("Could not persist crash report.");
        }
        catch (Exception failure)
        {
            TraceFailure(failure);
        }
    }

    public void CompleteSession()
    {
        sessionLease?.Dispose();
        sessionLease = null;
        if (Volatile.Read(ref fatalObserved) == 0 && sessionPath is not null)
        {
            try { File.Delete(sessionPath); }
            catch (Exception exception) { TraceFailure(exception); }
        }
    }

    // Disposing without CompleteSession deliberately leaves evidence of an interrupted run.
    public void Dispose()
    {
        sessionLease?.Dispose();
        sessionLease = null;
    }

    public void OpenReportFolder(string reportPath)
    {
        var fullPath = Path.GetFullPath(reportPath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Report no longer exists.");
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"") { UseShellExecute = true });
    }

    private void RecoverSessions(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (var marker in Directory.EnumerateFiles(directory, "session-*.json"))
            {
                var id = Path.GetFileNameWithoutExtension(marker)["session-".Length..];
                if (!Guid.TryParseExact(id, "N", out _)) continue;
                try
                {
                    // A running instance keeps this lease locked. PID reuse cannot trigger a false crash.
                    using (var lease = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        var crashPath = Path.Combine(directory, $"crash-{id}.txt");
                        if (!File.Exists(crashPath))
                        {
                            var text = Header("Unexpected previous shutdown (cause unknown)");
                            text.AppendLine("The previous run did not finish normally. Power loss, forced termination and native crashes cannot be distinguished.");
                            try
                            {
                                var previous = lease.Length <= 4096 ? JsonSerializer.Deserialize<SessionInfo>(lease) : null;
                                var version = previous?.Version;
                                text.AppendLine($"Previous version: {(version is { Length: <= 128 } && version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '+' or '-') ? version : "Unknown")}");
                                text.AppendLine($"Previous start UTC: {previous?.StartedAt:O}");
                            }
                            catch (JsonException exception) { TraceFailure(exception); }
                            var result = SaveReport($"crash-{id}.txt", text.ToString());
                            if (!result.Succeeded) continue;
                            crashPath = result.FilePath!;
                        }
                        recoveredReports.Add(crashPath);
                    }
                    File.Delete(marker);
                }
                catch (IOException exception) { TraceFailure(exception); }
                catch (UnauthorizedAccessException exception) { TraceFailure(exception); }
            }
        }
        catch (Exception exception) { TraceFailure(exception); }
    }

    private BugReportResult SaveReport(string name, string text)
    {
        var path = Path.Combine(ReportsDirectory, name);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(ReportsDirectory);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporary, path);
            return new BugReportResult(path, null);
        }
        catch (Exception exception)
        {
            TraceFailure(exception);
            return new BugReportResult(null, "Не удалось сохранить отчёт. Проверьте доступ и свободное место: " + ReportsDirectory);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) { TraceFailure(exception); }
        }
    }

    private static string Version => typeof(BugReportService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown";

    private static StringBuilder Header(string kind) => new StringBuilder()
        .AppendLine("Game Translator diagnostic report — format 1")
        .AppendLine($"Kind: {kind}")
        .AppendLine($"Created UTC: {DateTimeOffset.UtcNow:O}")
        .AppendLine($"Version: {Version}")
        .AppendLine($"OS: {Environment.OSVersion.VersionString}; architecture: {RuntimeInformation.ProcessArchitecture}")
        .AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}")
        .AppendLine($"Managed memory bytes: {GC.GetTotalMemory(false)}")
        .AppendLine("Privacy: no screenshots, OCR/translation text, credentials, raw logs, exception messages or memory dumps are included.");

    private static bool TryPrepareDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            using var probe = new FileStream(Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception exception) { TraceFailure(exception); return false; }
    }

    private static void TraceFailure(Exception exception) => Trace.TraceWarning($"Bug report storage: {exception.GetType().Name} (0x{exception.HResult:X8}).");
    private sealed record SessionInfo(string Version, DateTimeOffset StartedAt);
}
