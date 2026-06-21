namespace GameTranslator.Application.Updates;

public interface IApplicationUpdateProvider
{
    Task<ApplicationUpdateResult> CheckForUpdatesAsync(
        ApplicationUpdateOptions options,
        ApplicationUpdateCheckMode checkMode,
        CancellationToken cancellationToken = default);
}
