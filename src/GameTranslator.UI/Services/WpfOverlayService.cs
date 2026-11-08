using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using GameTranslator.Application.Abstractions;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Settings;
using GameTranslator.UI.Views;

namespace GameTranslator.UI.Services;

public sealed class WpfOverlayService : IOverlayService, IOverlayDiagnosticsSource, IDisposable
{
    private const int HistoryCapacity = 2048;
    private readonly OverlayWindow overlayWindow;
    private readonly LiveDiagnosticsPreferences? preferences;
    private readonly IApplicationLogger? logger;
    private readonly Queue<OverlayPresentationDiagnostic> history = new();
    private readonly object historyGate = new();
    private long sequence;
    private long dropped;
    private OverlaySnapshot? pending;
    private long pendingAt;
    private long pendingGeneration;
    private bool disposed;

    public WpfOverlayService(OverlayWindow overlayWindow) : this(overlayWindow, null, null) { }

    public WpfOverlayService(OverlayWindow overlayWindow, LiveDiagnosticsPreferences? preferences, IApplicationLogger? logger)
    {
        this.overlayWindow = overlayWindow;
        this.preferences = preferences;
        this.logger = logger;
        overlayWindow.SnapshotApplied += OnSnapshotApplied;
        overlayWindow.IsVisibleChanged += OnVisibilityChanged;
        overlayWindow.Closed += OnClosed;
        if (preferences is not null) preferences.CollectionChanged += OnCollectionChanged;
    }

    public bool IsVisible => overlayWindow.IsVisible;
    public bool IsExcludedFromCapture => overlayWindow.IsExcludedFromCapture;
    public OverlaySnapshot? CurrentSnapshot { get; private set; }
    public IReadOnlyList<OverlayPresentationDiagnostic> PresentationDiagnostics { get { lock (historyGate) return history.ToArray(); } }
    public long DroppedPresentationDiagnostics { get { lock (historyGate) return dropped; } }
    private bool Enabled => !disposed && preferences?.Current.Enabled != false;

    public void Show(OverlaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var started = Stopwatch.GetTimestamp();
        CurrentSnapshot = snapshot;
        Record("show-requested", snapshot);
        try
        {
            overlayWindow.ShowSnapshot(snapshot);
            Record("show-returned", snapshot, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (Exception error)
        {
            Record("show-failed", snapshot, Stopwatch.GetElapsedTime(started).TotalMilliseconds, error);
            throw;
        }
    }

    public void Hide()
    {
        Record("hide-requested", CurrentSnapshot);
        try { overlayWindow.Hide(); }
        catch (Exception error) { Record("hide-failed", CurrentSnapshot, error: error); throw; }
        finally { CancelPending("hidden-before-render-callback"); }
        Record("hide-returned", CurrentSnapshot);
    }

    private void OnSnapshotApplied(OverlaySnapshot snapshot)
    {
        CancelPending("superseded-before-render-callback");
        if (!Enabled) return;
        Record("view-model-applied", snapshot);
        pending = snapshot;
        pendingAt = Stopwatch.GetTimestamp();
        pendingGeneration = preferences?.CollectionGeneration ?? 0;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs args)
    {
        var snapshot = pending;
        var generation = pendingGeneration;
        var elapsed = Stopwatch.GetElapsedTime(pendingAt).TotalMilliseconds;
        CompositionTarget.Rendering -= OnRendering;
        pending = null;
        if (snapshot is not null && generation == (preferences?.CollectionGeneration ?? 0))
            Record("render-callback-before-composition", snapshot, elapsed);
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        Record(IsVisible ? "window-visible" : "window-hidden", CurrentSnapshot);
        if (!IsVisible) CancelPending("hidden-before-render-callback");
    }

    private void CancelPending(string reason)
    {
        CompositionTarget.Rendering -= OnRendering;
        if (pending is { } snapshot && pendingGeneration == (preferences?.CollectionGeneration ?? 0))
            Record(reason, snapshot, Stopwatch.GetElapsedTime(pendingAt).TotalMilliseconds);
        pending = null;
    }

    private void OnCollectionChanged(bool enabled)
    {
        if (enabled) return;
        if (overlayWindow.Dispatcher.CheckAccess()) CancelPending("collection-disabled");
        else if (!overlayWindow.Dispatcher.HasShutdownStarted)
            _ = overlayWindow.Dispatcher.InvokeAsync(() => CancelPending("collection-disabled"));
    }

    private void Record(string kind, OverlaySnapshot? snapshot, double elapsed = 0, Exception? error = null)
    {
        if (!Enabled) return;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(overlayWindow);
            OverlayPresentationDiagnostic entry;
            lock (historyGate)
            {
                entry = new(++sequence, DateTimeOffset.UtcNow, snapshot?.DiagnosticId, kind,
                    snapshot?.TextItems.Count ?? 0, snapshot?.MaskItems.Count ?? 0, IsVisible, IsExcludedFromCapture,
                    elapsed, overlayWindow.ActualWidth, overlayWindow.ActualHeight, dpi.DpiScaleX, dpi.DpiScaleY,
                    overlayWindow.IsMeasureValid && overlayWindow.IsArrangeValid, error?.GetType().FullName, error?.HResult);
                if (history.Count == HistoryCapacity) { history.Dequeue(); dropped++; }
                history.Enqueue(entry);
            }
            // Sink failure is diagnostic failure only; never alter presentation behavior.
            logger?.Information("Overlay presentation: " + JsonSerializer.Serialize(entry));
        }
        catch (Exception) { }
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        Record("window-closed", CurrentSnapshot);
        Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (preferences is not null) preferences.CollectionChanged -= OnCollectionChanged;
        overlayWindow.SnapshotApplied -= OnSnapshotApplied;
        overlayWindow.IsVisibleChanged -= OnVisibilityChanged;
        overlayWindow.Closed -= OnClosed;
        if (overlayWindow.Dispatcher.CheckAccess()) CancelPending("disposed");
        else if (!overlayWindow.Dispatcher.HasShutdownStarted)
            _ = overlayWindow.Dispatcher.InvokeAsync(() => CancelPending("disposed"));
    }
}
