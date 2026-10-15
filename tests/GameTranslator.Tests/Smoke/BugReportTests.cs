using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using GameTranslator.Application.Debug;
using GameTranslator.Application.Abstractions;
using System.Windows.Input;

namespace GameTranslator.Tests.Smoke;

public sealed class BugReportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameTranslator.Tests", Guid.NewGuid().ToString("N"));

    private static Assembly Ui => AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(
        RepositoryRoot.Find(), "src", "GameTranslator.UI", "bin",
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug",
        "net9.0-windows10.0.19041.0", "GameTranslator.UI.dll"));

    private IBugReportService Create()
    {
        var instance = (IBugReportService)Activator.CreateInstance(Ui.GetType("GameTranslator.UI.Diagnostics.BugReportService", true)!,
            Path.Combine(root, "app"), Path.Combine(root, "fallback"))!;
        Call(instance, "StartSession");
        return instance;
    }

    private static void Call(object service, string method, params object[] args) => service.GetType().GetMethod(method)!.Invoke(service, args);
    private static void Close(IBugReportService service) => Call(service, "CompleteSession");

    private static object Shell(IBugReportService service) => Activator.CreateInstance(
        Ui.GetType("GameTranslator.UI.ViewModels.ShellViewModel", true)!, new NavigationStub(),
        Activator.CreateInstance(Ui.GetType("GameTranslator.UI.Services.DialogService", true)!),
        Activator.CreateInstance(Ui.GetType("GameTranslator.UI.Services.InMemorySettingsService", true)!),
        new LoggerStub(), service)!;

    private static object? Property(object target, string name) => target.GetType().GetProperty(name)!.GetValue(target);
    private static Task Run(object target, string name) => (Task)target.GetType().GetMethod(name)!.Invoke(target, null)!;

    [Fact]
    public async Task Shell_ShowsRecoveredReportAndManualReportWithDismissibleNotice()
    {
        var previous = Create();
        ((IDisposable)previous).Dispose();
        var service = Create();
        try
        {
            var shell = Shell(service);
            await Run(shell, "InitializeAsync");
            Assert.Equal(true, Property(shell, "HasReportNotice"));
            Assert.Contains("неожиданно", (string)Property(shell, "ReportNotice")!);
            Assert.True(((ICommand)Property(shell, "OpenBugReportCommand")!).CanExecute(null));
            ((ICommand)Property(shell, "DismissReportNoticeCommand")!).Execute(null);
            Assert.Equal(false, Property(shell, "HasReportNotice"));
            await Run(shell, "CreateBugReportAsync");
            Assert.Contains("Баг-репорт сохранён:", (string)Property(shell, "ReportNotice")!);
            Assert.Equal(true, Property(shell, "HasReportNotice"));
        }
        finally { Close(service); }
    }

    [Fact]
    public async Task Shell_ReportsWriteFailureAndAllowsRetry()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "app"), "blocked");
        File.WriteAllText(Path.Combine(root, "fallback"), "blocked");
        var service = Create();
        try
        {
            var shell = Shell(service);
            await Run(shell, "CreateBugReportAsync");
            Assert.Contains("Не удалось", (string)Property(shell, "ReportNotice")!);
            Assert.True(((ICommand)Property(shell, "CreateBugReportCommand")!).CanExecute(null));
            Assert.False(((ICommand)Property(shell, "OpenBugReportCommand")!).CanExecute(null));
        }
        finally { Close(service); }
    }

    private sealed class NavigationStub : INavigationService
    {
        public object? CurrentViewModel => null;
        public void NavigateTo<TViewModel>() where TViewModel : class { }
    }

    private sealed class LoggerStub : IApplicationLogger
    {
        public void Information(string message) { }
        public void Warning(string message) { }
        public void Error(Exception exception, string message) { }
    }

    [Fact]
    public async Task ManualReport_IsUniqueAndSavedBesideApplication()
    {
        var service = Create();
        try
        {
            var snapshot = new BugReportSnapshot(3, 2, true, false);
            var first = await service.CreateBugReportAsync(snapshot);
            var second = await service.CreateBugReportAsync(snapshot);
            Assert.True(first.Succeeded);
            Assert.NotEqual(first.FilePath, second.FilePath);
            Assert.Equal(Path.Combine(root, "app", "Reports"), Path.GetDirectoryName(first.FilePath));
            var text = File.ReadAllText(first.FilePath!);
            Assert.Contains("OCR zone count: 2", text);
            Assert.Contains("Live running: True", text);
            Assert.Contains("Version:", text);
            Assert.Empty(Directory.GetFiles(service.ReportsDirectory, "*.tmp"));
        }
        finally { Close(service); }
    }

    [Fact]
    public void CleanShutdown_DoesNotProduceCrashNotice()
    {
        var first = Create();
        Close(first);
        var second = Create();
        try { Assert.Empty(second.RecoveredReports); }
        finally { Close(second); }
    }

    [Fact]
    public void Crash_IsDeduplicatedAndDoesNotExportExceptionSecrets()
    {
        var first = Create();
        try { throw new InvalidOperationException("api_key=PRIVATE-SECRET https://example.com?token=HIDDEN"); }
        catch (Exception exception)
        {
            exception.Data["secret"] = "PRIVATE-DATA";
            Call(first, "CaptureCrash", exception);
            Call(first, "CaptureCrash", exception);
        }
        Close(first);
        var next = Create();
        try
        {
            var text = File.ReadAllText(Assert.Single(next.RecoveredReports));
            Assert.Contains("InvalidOperationException", text);
            Assert.Contains(nameof(Crash_IsDeduplicatedAndDoesNotExportExceptionSecrets), text);
            Assert.DoesNotContain("PRIVATE", text);
            Assert.DoesNotContain("HIDDEN", text);
            Assert.DoesNotContain("example.com", text);
            Assert.Single(Directory.GetFiles(next.ReportsDirectory, "crash-*.txt"));
        }
        finally { Close(next); }
        var third = Create();
        try { Assert.Empty(third.RecoveredReports); }
        finally { Close(third); }
    }

    [Fact]
    public void AbruptTermination_IsDetectedWithoutInventingExceptionDetails()
    {
        var first = Create();
        ((IDisposable)first).Dispose();
        var next = Create();
        try
        {
            var text = File.ReadAllText(Assert.Single(next.RecoveredReports));
            Assert.Contains("cause unknown", text);
            Assert.Contains("Previous version:", text);
            Assert.Contains("Previous start UTC:", text);
        }
        finally { Close(next); }
    }

    [Fact]
    public void AnotherRunningInstance_IsNotTreatedAsCrashed()
    {
        var first = Create();
        var second = Create();
        try { Assert.Empty(second.RecoveredReports); }
        finally { Close(second); Close(first); }
    }

    [Fact]
    public async Task UnwritableApplicationDirectory_UsesVisibleFallbackAndRecoversIt()
    {
        Directory.CreateDirectory(Path.Combine(root, "app"));
        var blocker = Path.Combine(root, "app", "Reports");
        File.WriteAllText(blocker, "blocked");
        var service = Create();
        Assert.NotNull(service.StorageWarning);
        Assert.Equal(Path.Combine(root, "fallback"), service.ReportsDirectory);
        var report = await service.CreateBugReportAsync(new BugReportSnapshot(0, 0, false, false));
        Assert.True(report.Succeeded);
        ((IDisposable)service).Dispose();
        File.Delete(blocker);
        var recovered = Create();
        try { Assert.Single(recovered.RecoveredReports); }
        finally { Close(recovered); }
    }

    [Fact]
    public async Task BothLocationsBlocked_ReturnsRecoverableFailure()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "app"), "blocked");
        File.WriteAllText(Path.Combine(root, "fallback"), "blocked");
        var service = Create();
        try
        {
            Assert.NotNull(service.StorageWarning);
            var result = await service.CreateBugReportAsync(new BugReportSnapshot(0, 0, false, false));
            Assert.False(result.Succeeded);
            Assert.NotNull(result.Error);
            Call(service, "CaptureCrash", new InvalidOperationException("secret"));
        }
        finally { Close(service); }
    }

    [Fact]
    public void CorruptSessionMarker_StillProducesUnexpectedShutdownReport()
    {
        var directory = Path.Combine(root, "app", "Reports");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"session-{Guid.NewGuid():N}.json"), "broken JSON");
        var service = Create();
        try { Assert.Contains("cause unknown", File.ReadAllText(Assert.Single(service.RecoveredReports))); }
        finally { Close(service); }
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
