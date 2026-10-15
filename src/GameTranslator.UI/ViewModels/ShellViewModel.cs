using GameTranslator.Application.Abstractions;
using GameTranslator.Application.Debug;
using GameTranslator.UI.Commands;
using GameTranslator.UI.Localization;
using System.Windows.Input;

namespace GameTranslator.UI.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private bool isInitialized;
    private readonly IBugReportService reports;
    private string reportNotice = string.Empty;
    private string? reportPath;
    private bool creatingReport;

    public ShellViewModel(
        INavigationService navigation,
        IDialogService dialog,
        ISettingsService settings,
        IApplicationLogger logger,
        IBugReportService reports)
    {
        Navigation = navigation;
        Dialog = dialog;
        Settings = settings;
        Logger = logger;
        this.reports = reports;
        CreateBugReportCommand = new AsyncRelayCommand(CreateBugReportAsync, () => !creatingReport);
        OpenBugReportCommand = new RelayCommand(OpenBugReport, () => reportPath is not null);
        DismissReportNoticeCommand = new RelayCommand(() => ReportNotice = string.Empty);

        Navigation.NavigateTo<MainViewModel>();
        Logger.Information("Shell view model initialized.");
    }

    public string ApplicationName => "Game Translator";

    public string ReportNotice
    {
        get => reportNotice;
        private set { SetProperty(ref reportNotice, value); OnPropertyChanged(nameof(HasReportNotice)); }
    }
    public bool HasReportNotice => !string.IsNullOrWhiteSpace(ReportNotice);
    public ICommand CreateBugReportCommand { get; }
    public ICommand OpenBugReportCommand { get; }
    public ICommand DismissReportNoticeCommand { get; }

    public async Task CreateBugReportAsync()
    {
        if (creatingReport) return;
        creatingReport = true;
        ((AsyncRelayCommand)CreateBugReportCommand).RaiseCanExecuteChanged();
        ReportNotice = "Создание баг-репорта…";
        reportPath = null;
        ((RelayCommand)OpenBugReportCommand).RaiseCanExecuteChanged();
        try
        {
            var main = Navigation.CurrentViewModel as MainViewModel;
            var snapshot = new BugReportSnapshot(main?.SelectedWorkspaceTabIndex ?? -1, main?.OcrZones.Count ?? 0,
                main?.IsLiveTranslationRunning ?? false, main?.HasValidationErrors ?? false);
            var result = await reports.CreateBugReportAsync(snapshot);
            reportPath = result.FilePath;
            ReportNotice = result.Succeeded
                ? $"Баг-репорт сохранён: {result.FilePath}. Отправка не выполнялась. {UiText.Translate(reports.StorageWarning)}"
                : result.Error ?? "Не удалось создать баг-репорт.";
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Could not create bug report.");
            ReportNotice = "Не удалось создать баг-репорт. Проверьте доступ к папке отчётов и свободное место.";
        }
        finally
        {
            creatingReport = false;
            ((AsyncRelayCommand)CreateBugReportCommand).RaiseCanExecuteChanged();
            ((RelayCommand)OpenBugReportCommand).RaiseCanExecuteChanged();
        }
    }

    private void OpenBugReport()
    {
        try { if (reportPath is not null) reports.OpenReportFolder(reportPath); }
        catch (Exception exception)
        {
            Logger.Error(exception, "Could not reveal bug report.");
            ReportNotice = $"Не удалось открыть отчёт в Проводнике. Путь: {reportPath}";
        }
    }

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
        reportPath = reports.RecoveredReports.LastOrDefault();
        ReportNotice = reportPath is not null
            ? $"Предыдущий запуск завершился неожиданно. Найдено отчётов: {reports.RecoveredReports.Count}. Отчёт: {reportPath}. {UiText.Translate(reports.StorageWarning)}"
            : reports.StorageWarning ?? string.Empty;
        ((RelayCommand)OpenBugReportCommand).RaiseCanExecuteChanged();

        if (Navigation.CurrentViewModel is MainViewModel mainViewModel)
        {
            await mainViewModel.LoadAsync();
        }
    }
}
