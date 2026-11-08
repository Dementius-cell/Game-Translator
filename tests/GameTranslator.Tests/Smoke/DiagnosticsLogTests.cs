using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using GameTranslator.Application.Settings;
using GameTranslator.Infrastructure.Settings;

namespace GameTranslator.Tests.Smoke;

public sealed class DiagnosticsLogTests
{
    [Fact]
    public void MasterSwitch_PersistedOffCreatesNoFileAndLiveToggleFiltersAllLevels()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GameTranslator.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var preferences = new LiveDiagnosticsPreferences(new JsonSettingsService(settingsPath));
            preferences.Save(preferences.Current with { Enabled = false });
            preferences = new LiveDiagnosticsPreferences(new JsonSettingsService(settingsPath));
            var uiDirectory = Path.Combine(RepositoryRoot.Find(), "src", "GameTranslator.UI", "bin",
                AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug",
                "net9.0-windows10.0.19041.0");
            var serilog = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(uiDirectory, "Serilog.dll"));
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(uiDirectory, "Serilog.Sinks.File.dll"));
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(uiDirectory, "Serilog.Sinks.Debug.dll"));
            var ui = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(uiDirectory, "GameTranslator.UI.dll"));
            var sinkType = ui.GetType("GameTranslator.UI.Diagnostics.DiagnosticsLogSink", true)!;
            var logs = Path.Combine(directory, "logs");
            using var sink = (IDisposable)Activator.CreateInstance(sinkType, preferences, Path.Combine(logs, "app-.txt"))!;
            var emit = sinkType.GetMethod("Emit")!;
            void Write(string text, string level)
            {
                var parserType = serilog.GetType("Serilog.Parsing.MessageTemplateParser", true)!;
                var template = parserType.GetMethod("Parse")!.Invoke(Activator.CreateInstance(parserType), [text]);
                var entry = Activator.CreateInstance(serilog.GetType("Serilog.Events.LogEvent", true)!,
                    DateTimeOffset.UtcNow, Enum.Parse(serilog.GetType("Serilog.Events.LogEventLevel", true)!, level),
                    null, template, Array.CreateInstance(serilog.GetType("Serilog.Events.LogEventProperty", true)!, 0));
                emit.Invoke(sink, [entry]);
            }
            Write("disabled-startup", "Fatal");
            Assert.False(Directory.Exists(logs));
            preferences.Save(preferences.Current with { Enabled = true });
            Write("enabled-debug", "Debug");
            preferences.Save(preferences.Current with { Enabled = false });
            Write("disabled-error", "Error");
            var path = Assert.Single(Directory.GetFiles(logs));
            var before = File.ReadAllText(path);
            Assert.Contains("enabled-debug", before);
            Assert.DoesNotContain("disabled", before);
            // Opt-out releases the file handle too, so cleanup can access it exclusively.
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            preferences.Save(preferences.Current with { Enabled = true });
            Write("enabled-again", "Warning");
            preferences.Save(preferences.Current with { Enabled = false });
            var after = File.ReadAllText(path);
            Assert.Contains("enabled-again", after);
            Assert.StartsWith(before, after);
            Assert.DoesNotContain("disabled", after);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
