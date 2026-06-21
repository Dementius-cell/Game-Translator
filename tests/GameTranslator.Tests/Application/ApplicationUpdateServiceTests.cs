using GameTranslator.Application.Updates;

namespace GameTranslator.Tests.Application;

public sealed class ApplicationUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdatesAsync_WhenStartupCheckDisabled_ReturnsDisabledWithoutProviderCall()
    {
        var provider = new TestApplicationUpdateProvider();
        var service = new ApplicationUpdateService(
            provider,
            new ApplicationUpdateOptions("https://updates.test", checkOnStartup: false));

        var result = await service.CheckForUpdatesAsync(ApplicationUpdateCheckMode.Startup);

        Assert.Equal(ApplicationUpdateStatus.StartupCheckDisabled, result.Status);
        Assert.Empty(provider.CheckModes);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenUpdateSourceMissing_ReturnsNotConfiguredWithoutProviderCall()
    {
        var provider = new TestApplicationUpdateProvider();
        var service = new ApplicationUpdateService(
            provider,
            new ApplicationUpdateOptions(updateSource: string.Empty));

        var result = await service.CheckForUpdatesAsync(ApplicationUpdateCheckMode.Manual);

        Assert.Equal(ApplicationUpdateStatus.NotConfigured, result.Status);
        Assert.Empty(provider.CheckModes);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenConfigured_DelegatesToProvider()
    {
        var provider = new TestApplicationUpdateProvider
        {
            Result = ApplicationUpdateResult.CheckCompleted(),
        };
        var service = new ApplicationUpdateService(
            provider,
            new ApplicationUpdateOptions("https://updates.test"));

        var result = await service.CheckForUpdatesAsync(ApplicationUpdateCheckMode.Manual);

        Assert.Equal(ApplicationUpdateStatus.CheckCompleted, result.Status);
        Assert.Equal(new[] { ApplicationUpdateCheckMode.Manual }, provider.CheckModes);
    }

    private sealed class TestApplicationUpdateProvider : IApplicationUpdateProvider
    {
        public ApplicationUpdateResult Result { get; set; } = ApplicationUpdateResult.NotInstalled();

        public List<ApplicationUpdateCheckMode> CheckModes { get; } = new();

        public Task<ApplicationUpdateResult> CheckForUpdatesAsync(
            ApplicationUpdateOptions options,
            ApplicationUpdateCheckMode checkMode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckModes.Add(checkMode);

            return Task.FromResult(Result);
        }
    }
}
