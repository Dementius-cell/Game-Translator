using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using GameTranslator.Application.Abstractions;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Settings;
using GameTranslator.Infrastructure.Settings;

namespace GameTranslator.Tests.Smoke;

[Collection(WpfResourceCollection.Name)]
public sealed class OverlayPresentationDiagnosticsTests
{
    [Fact]
    public void PresentationEvidence_CorrelatesApplyRenderHideAndHonorsMasterSwitchEvenWithFailedLogger()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "gt-presentation-" + Guid.NewGuid());
            Window? window = null;
            try
            {
                var configuration = AppContext.BaseDirectory.Contains("Release") ? "Release" : "Debug";
                var ui = Assembly.LoadFrom(Path.Combine(RepositoryRoot.Find(), "src", "GameTranslator.UI", "bin",
                    configuration, "net9.0-windows10.0.19041.0", "GameTranslator.UI.dll"));
                window = (Window)Activator.CreateInstance(ui.GetType("GameTranslator.UI.Views.OverlayWindow", true)!)!;
                // Real WPF lifecycle without visible UI or focus activation.
                window.Opacity = 0;
                window.ShowActivated = false;
                var preferences = new LiveDiagnosticsPreferences(new JsonSettingsService(Path.Combine(root, "settings.json")));
                var service = (IOverlayService)Activator.CreateInstance(ui.GetType("GameTranslator.UI.Services.WpfOverlayService", true)!,
                    window, preferences, new FailedLogger())!;
                using var lifetime = (IDisposable)service;
                var source = (IOverlayDiagnosticsSource)service;
                var first = new OverlaySnapshot([new OverlayTextItem("synthetic", 10, 10, 100, 30)], DateTimeOffset.UtcNow);
                service.Show(first);
                var frame = new DispatcherFrame();
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Assert.Contains(source.PresentationDiagnostics, e => e.Event == "view-model-applied" && e.SnapshotId == first.DiagnosticId);
                Assert.Contains(source.PresentationDiagnostics, e => e.Event == "render-callback-before-composition" && e.SnapshotId == first.DiagnosticId);
                service.Hide();
                Assert.Contains(source.PresentationDiagnostics, e => e.Event == "hide-returned" && !e.Visible);
                preferences.Save(preferences.Current with { Enabled = false });
                var count = source.PresentationDiagnostics.Count;
                service.Show(new OverlaySnapshot([], DateTimeOffset.UtcNow));
                service.Hide();
                Assert.Equal(count, source.PresentationDiagnostics.Count);
                preferences.Save(preferences.Current with { Enabled = true });
                service.Show(first);
                var second = new OverlaySnapshot([], DateTimeOffset.UtcNow);
                service.Show(second);
                Assert.Contains(source.PresentationDiagnostics, e => e.Event == "superseded-before-render-callback" && e.SnapshotId == first.DiagnosticId);
                service.Hide();
                Assert.Contains(source.PresentationDiagnostics, e => e.Event == "hidden-before-render-callback" && e.SnapshotId == second.DiagnosticId);
                Assert.DoesNotContain(source.PresentationDiagnostics, e => e.ExceptionType is not null);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                window?.Close();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "WPF diagnostic test did not finish.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class FailedLogger : IApplicationLogger
    {
        public void Information(string message) => throw new IOException("synthetic sink failure");
        public void Warning(string message) => throw new IOException();
        public void Error(Exception error, string message) => throw new IOException();
    }
}
