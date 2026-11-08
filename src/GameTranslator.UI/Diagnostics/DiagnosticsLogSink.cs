using GameTranslator.Application.Settings;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace GameTranslator.UI.Diagnostics;

/// <summary>Creates log files only on the first enabled event; opt-out closes the file immediately.</summary>
public sealed class DiagnosticsLogSink : ILogEventSink, IDisposable
{
    private readonly object gate = new();
    private readonly LiveDiagnosticsPreferences preferences;
    private readonly string path;
    private Logger? logger;
    private bool disposed;

    public DiagnosticsLogSink(LiveDiagnosticsPreferences preferences, string path)
    {
        this.preferences = preferences;
        this.path = path;
        preferences.CollectionChanged += OnCollectionChanged;
    }

    public void Emit(LogEvent logEvent)
    {
        lock (gate)
        {
            if (disposed || !preferences.Current.Enabled) return;
            logger ??= new LoggerConfiguration().MinimumLevel.Debug()
                .WriteTo.Debug()
                .WriteTo.File(path, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
                .CreateLogger();
            logger.Write(logEvent);
        }
    }

    private void OnCollectionChanged(bool enabled)
    {
        if (enabled) return;
        lock (gate)
        {
            logger?.Dispose();
            logger = null;
        }
    }

    public void Dispose()
    {
        preferences.CollectionChanged -= OnCollectionChanged;
        lock (gate)
        {
            disposed = true;
            logger?.Dispose();
            logger = null;
        }
    }
}
