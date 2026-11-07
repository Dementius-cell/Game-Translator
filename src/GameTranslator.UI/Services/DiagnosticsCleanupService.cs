using System.IO;
using GameTranslator.Application.Abstractions;
using GameTranslator.Application.Debug;

namespace GameTranslator.UI.Services;

public sealed class DiagnosticsCleanupService(IApplicationLogger logger, string diagnosticsRoot) : IDiagnosticsCleanupService
{
    public Task<DiagnosticsCleanupResult> ClearAsync(string liveReportsDirectory) => Task.Run(() =>
    {
        var deleted = 0;
        var skipped = 0;
        long bytes = 0;
        var root = Path.GetFullPath(diagnosticsRoot);
        foreach (var name in new[] { "Live", "Frames", "Detector" })
            CleanDirectory(Path.Combine(root, name), recursive: true);

        var customLive = Path.GetFullPath(liveReportsDirectory);
        if (!customLive.Equals(Path.Combine(root, "Live"), StringComparison.OrdinalIgnoreCase))
            CleanDirectory(customLive, recursive: false);
        return new DiagnosticsCleanupResult(deleted, skipped, bytes);

        bool HasLinkedAncestor(string path)
        {
            for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            return false;
        }

        void CleanDirectory(string path, bool recursive)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                if (HasLinkedAncestor(path)) { skipped++; return; }
                foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
                {
                    try
                    {
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                        if (entry is DirectoryInfo directory)
                        {
                            if (recursive) CleanDirectory(directory.FullName, recursive: true);
                            continue;
                        }
                        // A custom report path may contain unrelated user files.
                        if (!recursive && !(entry.Name.StartsWith("game-translator-live-", StringComparison.Ordinal)
                            && entry.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))) continue;
                        var length = ((FileInfo)entry).Length;
                        entry.Delete();
                        deleted++;
                        bytes += length;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        skipped++;
                        logger.Warning($"Diagnostic cleanup skipped an entry: {exception.GetType().Name}.");
                    }
                }
                // Keep the owned roots; remove only empty subdirectories without recursive deletion.
                if (recursive && !Path.GetDirectoryName(path)!.Equals(root, StringComparison.OrdinalIgnoreCase)
                    && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                skipped++;
                logger.Warning($"Diagnostic cleanup could not finish a directory: {exception.GetType().Name}.");
            }
        }
    });
}
