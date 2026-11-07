using GameTranslator.Application.Abstractions;

namespace GameTranslator.Application.Settings;

public sealed record LiveDiagnosticsOptions(bool Enabled = true, bool Input = true, bool Buttons = false,
    bool Frames = false, int ReportMiB = 64, int FramesMiB = 512);

public sealed class LiveDiagnosticsPreferences
{
    private const string Key = "diagnostics.live.options";
    private readonly ISettingsService settings;
    private volatile LiveDiagnosticsOptions current;
    public LiveDiagnosticsOptions Current => current;
    public volatile bool Live;

    public LiveDiagnosticsPreferences(ISettingsService settings)
    {
        this.settings = settings;
        current = Normalize(settings.GetValue<LiveDiagnosticsOptions>(Key) ??
            new LiveDiagnosticsOptions(Frames: System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "capture-frames.enabled"))));
    }

    public void Save(LiveDiagnosticsOptions options)
    {
        var normalized = Normalize(options);
        settings.SetValue(Key, normalized);
        current = normalized;
    }

    private static LiveDiagnosticsOptions Normalize(LiveDiagnosticsOptions options) => options with
    {
        ReportMiB = Math.Clamp(options.ReportMiB, 1, 94),
        FramesMiB = Math.Clamp(options.FramesMiB, 1, 2048)
    };
}
