using System.Diagnostics;
using System.Reflection;
using GameTranslator.Application.Updates;

namespace GameTranslator.Infrastructure.Updates;

public sealed class SquirrelApplicationUpdateProvider : IApplicationUpdateProvider
{
    public async Task<ApplicationUpdateResult> CheckForUpdatesAsync(
        ApplicationUpdateOptions options,
        ApplicationUpdateCheckMode checkMode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var updateExecutablePath = ResolveUpdateExecutablePath();
        if (updateExecutablePath is null)
        {
            return ApplicationUpdateResult.NotInstalled();
        }

        var startInfo = new ProcessStartInfo(updateExecutablePath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(updateExecutablePath) ?? string.Empty,
        };
        startInfo.ArgumentList.Add("--update");
        startInfo.ArgumentList.Add(options.UpdateSource);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Squirrel.Windows Update.exe could not be started.");
        cancellationToken.ThrowIfCancellationRequested();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Squirrel.Windows Update.exe exited with code {process.ExitCode}.");
        }

        return ApplicationUpdateResult.CheckCompleted();
    }

    private static string? ResolveUpdateExecutablePath()
    {
        var assembly = Assembly.GetEntryAssembly();
        var assemblyLocation = assembly?.Location;
        if (string.IsNullOrWhiteSpace(assemblyLocation))
        {
            return null;
        }

        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            return null;
        }

        var updateExecutablePath = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "Update.exe"));

        return File.Exists(updateExecutablePath)
            ? updateExecutablePath
            : null;
    }
}
