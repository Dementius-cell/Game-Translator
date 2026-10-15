namespace GameTranslator.Application.Debug;

public sealed record BugReportSnapshot(int WorkspaceTab, int ZoneCount, bool IsLiveRunning, bool HasValidationErrors);

public sealed record BugReportResult(string? FilePath, string? Error)
{
    public bool Succeeded => FilePath is not null;
}

public interface IBugReportService
{
    string ReportsDirectory { get; }
    string? StorageWarning { get; }
    IReadOnlyList<string> RecoveredReports { get; }
    Task<BugReportResult> CreateBugReportAsync(BugReportSnapshot snapshot);
    void OpenReportFolder(string reportPath);
}
