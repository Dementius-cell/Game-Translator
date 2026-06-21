namespace GameTranslator.Application.Updates;

public sealed class ApplicationUpdateService
{
    private readonly IApplicationUpdateProvider provider;
    private readonly ApplicationUpdateOptions options;

    public ApplicationUpdateService(
        IApplicationUpdateProvider provider,
        ApplicationUpdateOptions options)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<ApplicationUpdateResult> CheckForUpdatesAsync(
        ApplicationUpdateCheckMode checkMode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (checkMode == ApplicationUpdateCheckMode.Startup && !options.CheckOnStartup)
        {
            return Task.FromResult(ApplicationUpdateResult.StartupCheckDisabled());
        }

        if (string.IsNullOrWhiteSpace(options.UpdateSource))
        {
            return Task.FromResult(ApplicationUpdateResult.NotConfigured());
        }

        return provider.CheckForUpdatesAsync(options, checkMode, cancellationToken);
    }
}
