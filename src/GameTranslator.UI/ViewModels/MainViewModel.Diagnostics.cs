using System.Text;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Settings;

namespace GameTranslator.UI.ViewModels;

public sealed partial class MainViewModel
{
    private readonly LiveDiagnosticsPreferences diagnostics;
    private readonly Queue<(DateTimeOffset At, string Button)> buttonLog = new();
    private int droppedButtons;
    public bool CanChangeOverlayRecording => !IsBusy && !IsLiveTranslationRunning && overlayService is IOverlayCaptureControl;
    public bool OverlayRecordingAllowed
    {
        get => overlayService is IOverlayCaptureControl control && control.IsRecordingAllowed;
        set
        {
            if (!CanChangeOverlayRecording || value == OverlayRecordingAllowed) return;
            try
            {
                ((IOverlayCaptureControl)overlayService).SetRecordingAllowed(value);
                StatusMessage = value ? "Overlay recording enabled until application exit." : "Overlay capture protection restored.";
            }
            catch (System.ComponentModel.Win32Exception)
            {
                StatusMessage = "Windows could not change overlay capture protection.";
            }
            OnPropertyChanged(nameof(OverlayRecordingAllowed));
        }
    }
    internal bool ButtonCaptureActive => IsLiveTranslationRunning && DiagnosticsEnabled && DiagnosticsInput && DiagnosticsButtons;
    internal bool ButtonDeviceAvailable { get; set; }
    internal string GamepadCaptureStatus { get; set; } = "not started";
    public bool DiagnosticsEnabled { get => diagnostics.Current.Enabled; set => SaveDiagnostics(diagnostics.Current with { Enabled = value }); }
    public bool DiagnosticsInput { get => diagnostics.Current.Input; set => SaveDiagnostics(diagnostics.Current with { Input = value }); }
    public bool DiagnosticsButtons { get => diagnostics.Current.Buttons; set => SaveDiagnostics(diagnostics.Current with { Buttons = value }); }
    public bool DiagnosticsFrames { get => diagnostics.Current.Frames; set => SaveDiagnostics(diagnostics.Current with { Frames = value }); }
    public int DiagnosticsReportMiB { get => diagnostics.Current.ReportMiB; set => SaveDiagnostics(diagnostics.Current with { ReportMiB = value }); }
    public int DiagnosticsFramesMiB { get => diagnostics.Current.FramesMiB; set => SaveDiagnostics(diagnostics.Current with { FramesMiB = value }); }
    public int[] ReportSizes { get; } = [1, 4, 16, 32, 64, 94];
    public int[] FrameBudgets { get; } = [1, 16, 64, 128, 256, 512, 1024, 2048];

    private void SaveDiagnostics(LiveDiagnosticsOptions next)
    {
        if (next == diagnostics.Current) return;
        diagnostics.Save(next);
        if (!ButtonCaptureActive) ResetButtonLog();
        foreach (var property in new[] { nameof(DiagnosticsEnabled), nameof(DiagnosticsInput), nameof(DiagnosticsButtons),
            nameof(DiagnosticsFrames), nameof(DiagnosticsReportMiB), nameof(DiagnosticsFramesMiB) })
            OnPropertyChanged(property);
    }

    internal void RecordButton(string button)
    {
        if (!ButtonCaptureActive) return;
        lock (buttonLog)
        {
            if (buttonLog.Count >= 4096) { buttonLog.Dequeue(); droppedButtons++; }
            buttonLog.Enqueue((DateTimeOffset.UtcNow, button));
        }
    }

    private void ResetButtonLog()
    {
        lock (buttonLog) { buttonLog.Clear(); droppedButtons = 0; }
    }

    private void AppendButtonLog(StringBuilder builder)
    {
        if (!DiagnosticsEnabled || !DiagnosticsInput || !DiagnosticsButtons) return;
        lock (buttonLog)
        {
            builder.AppendLine($"Session input buttons (UTC), device registered: {ButtonDeviceAvailable}, dropped oldest: {droppedButtons}");
            builder.AppendLine($"Gamepad input: {GamepadCaptureStatus}. Gamepad1–4 use XInput slot numbers; Connected/Disconnected are device events. Presses only, no held repeats; times are observations, not hardware timestamps.");
            foreach (var item in buttonLog) builder.AppendLine($"  At={item.At:O} Button={item.Button}");
        }
    }
}
