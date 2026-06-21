namespace GameTranslator.Application.Updates;

public sealed class NoOpApplicationUpdateProvider : IApplicationUpdateProvider
{
    public Task<ApplicationUpdateResult> CheckForUpdatesAsync(
        ApplicationUpdateOptions options,
        ApplicationUpdateCheckMode checkMode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ApplicationUpdateResult.ProviderUnavailable());
    }
}
