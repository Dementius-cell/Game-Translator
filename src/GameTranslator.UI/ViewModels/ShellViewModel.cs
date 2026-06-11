using GameTranslator.Application.Abstractions;

namespace GameTranslator.UI.ViewModels;

public sealed class ShellViewModel
{
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

    public string CurrentStage => "Sprint 1";

    public INavigationService Navigation { get; }

    public IDialogService Dialog { get; }

    public ISettingsService Settings { get; }

    public IApplicationLogger Logger { get; }
}
