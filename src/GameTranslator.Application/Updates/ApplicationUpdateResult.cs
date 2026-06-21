namespace GameTranslator.Application.Updates;

public sealed class ApplicationUpdateResult
{
    private ApplicationUpdateResult(
        ApplicationUpdateStatus status,
        string message,
        bool restartRecommended)
    {
        Status = status;
        Message = string.IsNullOrWhiteSpace(message)
            ? throw new ArgumentException("Update result message cannot be empty.", nameof(message))
            : message.Trim();
        RestartRecommended = restartRecommended;
    }

    public ApplicationUpdateStatus Status { get; }

    public string Message { get; }

    public bool RestartRecommended { get; }

    public static ApplicationUpdateResult StartupCheckDisabled()
    {
        return new ApplicationUpdateResult(
            ApplicationUpdateStatus.StartupCheckDisabled,
            "Automatic update checks at startup are disabled.",
            restartRecommended: false);
    }

    public static ApplicationUpdateResult NotConfigured()
    {
        return new ApplicationUpdateResult(
            ApplicationUpdateStatus.NotConfigured,
            "Application update source is not configured.",
            restartRecommended: false);
    }

    public static ApplicationUpdateResult ProviderUnavailable()
    {
        return new ApplicationUpdateResult(
            ApplicationUpdateStatus.ProviderUnavailable,
            "Application update provider is not available.",
            restartRecommended: false);
    }

    public static ApplicationUpdateResult NotInstalled()
    {
        return new ApplicationUpdateResult(
            ApplicationUpdateStatus.NotInstalled,
            "Squirrel.Windows installation was not detected; update check skipped.",
            restartRecommended: false);
    }

    public static ApplicationUpdateResult CheckCompleted()
    {
        return new ApplicationUpdateResult(
            ApplicationUpdateStatus.CheckCompleted,
            "Squirrel.Windows update check completed. Restart the app if an update was installed.",
            restartRecommended: false);
    }
}
