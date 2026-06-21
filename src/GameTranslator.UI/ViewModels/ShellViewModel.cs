using GameTranslator.Application.Abstractions;

namespace GameTranslator.UI.ViewModels;

public sealed class ShellViewModel
{
    private bool isInitialized;

    public ShellViewModel(
        INavigationService navigation,
        IDialogService dialog,
        ISettingsService settings,
        IApplicationLogger logger)
    {
        Navigation = navigation;
        Dialog = dialog;
        Settings = settings;
        Logger = logger;

        Navigation.NavigateTo<MainViewModel>();
        Logger.Information("Shell view model initialized.");
    }

    public string ApplicationName => "Game Translator";

    public string CurrentStage => "Sprint 24";

    public INavigationService Navigation { get; }

    public IDialogService Dialog { get; }

    public ISettingsService Settings { get; }

    public IApplicationLogger Logger { get; }

    public async Task InitializeAsync()
    {
        if (isInitialized)
        {
            return;
        }

        isInitialized = true;

        if (Navigation.CurrentViewModel is MainViewModel mainViewModel)
        {
            await mainViewModel.LoadAsync();
        }
    }
}
