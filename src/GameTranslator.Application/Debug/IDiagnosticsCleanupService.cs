namespace GameTranslator.Application.Debug;

public sealed record DiagnosticsCleanupResult(int DeletedFiles, int SkippedEntries, long DeletedBytes);

public interface IDiagnosticsCleanupService
{
    Task<DiagnosticsCleanupResult> ClearAsync(string liveReportsDirectory);
}
