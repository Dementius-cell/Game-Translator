using System.Windows;
using GameTranslator.UI.Services;
using GameTranslator.UI.Views;

namespace GameTranslator.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private LiveButtonRecorder? buttonRecorder;
    private readonly WpfGlobalHotkeyRegistrar hotkeyRegistrar;

    public MainWindow(ShellView shellView, WpfGlobalHotkeyRegistrar hotkeyRegistrar, ThemeService theme)
    {
        InitializeComponent();
        theme.Attach(this);
        this.hotkeyRegistrar = hotkeyRegistrar;
        ShellHost.Content = shellView;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        hotkeyRegistrar.Attach(this);
        if (ShellHost.Content is ShellView { DataContext: ViewModels.ShellViewModel { Navigation.CurrentViewModel: ViewModels.MainViewModel vm } })
            buttonRecorder = new LiveButtonRecorder(this, vm);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        buttonRecorder?.Dispose();
        hotkeyRegistrar.Dispose();
    }
}
