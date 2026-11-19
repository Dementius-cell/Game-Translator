using GameTranslator.Application.Abstractions;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Settings;
using GameTranslator.Infrastructure.Capture;

namespace GameTranslator.Infrastructure.Ocr;

// Records the engine-entry request, including already applied context/preprocessing.
// Internal engine transformations are reproduced by replaying the same engine build.
internal sealed class OcrInputDiagnosticRecorder
{
    private readonly object gate = new();
    private readonly LiveDiagnosticsPreferences preferences;
    private readonly IApplicationLogger? logger;
    private readonly CaptureFrameDiagnosticRecorder sink;
    private Task completion = Task.CompletedTask;
    private long pendingBytes;
    private int pending;
    private long dropped;

    public OcrInputDiagnosticRecorder(LiveDiagnosticsPreferences preferences, IApplicationLogger? logger = null)
        : this(preferences, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameTranslator", "Diagnostics", "OcrInputs", $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid():N}"), logger) { }

    internal OcrInputDiagnosticRecorder(LiveDiagnosticsPreferences preferences, string directory, IApplicationLogger? logger = null)
    {
        this.preferences = preferences;
        this.logger = logger;
        sink = new(directory, maximumFrames: 20000, maximumBytes: 256L * 1024 * 1024,
            preferences: preferences, logger: logger, minimumInterval: TimeSpan.Zero);
    }

    internal Task Completion { get { lock (gate) return completion; } }
    private bool Enabled => preferences.Live && preferences.Current is { Enabled: true, Frames: true };

    internal void Record(OcrRequest request, OcrResult result, long generation)
    {
        lock (gate)
        {
            if (!Enabled || preferences.CollectionGeneration != generation) return;
            var bytes = request.Frame.PixelData.Length;
            if (pending >= 32 || bytes > 16L * 1024 * 1024 - pendingBytes)
            {
                Drop("queue-limit");
                return;
            }
            pending++;
            pendingBytes += bytes;
            var previous = completion;
            completion = Task.Run(async () =>
            {
                try
                {
                    await previous.ConfigureAwait(false);
                    if (!Enabled || preferences.CollectionGeneration != generation) return;
                    var metadata = new
                    {
                        kind = "ocr-engine-input", request.EngineId, request.Language, request.ZoneId,
                        request.OrientationMode, request.LayoutMode, request.ContentLayoutMode,
                        request.PreprocessingSettings, request.DetectorLineBounds, request.DetectorPreset,
                        request.CandidateGroupingSettings, request.WatermarkFilter, request.HasMenuValueRowEvidence,
                        blocks = result.TextBlocks.Select(b => new { b.Text, b.Bounds }).ToArray(),
                    };
                    if (sink.TryRecord(request.Frame, inputMetadata: metadata))
                        await sink.Completion.ConfigureAwait(false);
                    else Drop("sink-limit-or-disabled");
                }
                catch (Exception error)
                {
                    // Optional evidence must never fail recognition or leak exception messages.
                    Log("failure-type=" + error.GetType().Name);
                }
                finally { lock (gate) { pending--; pendingBytes -= bytes; } }
            });
        }
    }

    private void Drop(string reason)
    {
        var count = Interlocked.Increment(ref dropped);
        // Bounded telemetry: first loss and periodic aggregate updates.
        if (count == 1 || count % 100 == 0) Log($"dropped={count}; reason={reason}");
    }

    private void Log(string message)
    {
        if (!preferences.Current.Enabled) return;
        try { logger?.Information("OCR input diagnostics: " + message); }
        catch (Exception) { /* Logging remains optional. */ }
    }
}

internal sealed class DiagnosticOcrEngine(IOcrEngine inner, OcrInputDiagnosticRecorder recorder,
    LiveDiagnosticsPreferences preferences) : IOcrEngine
{
    public string EngineId => inner.EngineId;

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
    {
        var generation = preferences.CollectionGeneration;
        var enabled = preferences.Live && preferences.Current is { Enabled: true, Frames: true };
        var result = await inner.RecognizeAsync(request, cancellationToken).ConfigureAwait(false);
        if (enabled) recorder.Record(request, result, generation);
        return result;
    }
}
