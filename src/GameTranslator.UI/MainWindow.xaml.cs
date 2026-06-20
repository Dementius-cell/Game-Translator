using System.Windows;
using GameTranslator.UI.Services;
using GameTranslator.UI.Views;

namespace GameTranslator.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly WpfGlobalHotkeyRegistrar hotkeyRegistrar;

    public MainWindow(ShellView shellView, WpfGlobalHotkeyRegistrar hotkeyRegistrar)
    {
        InitializeComponent();
        this.hotkeyRegistrar = hotkeyRegistrar;
        ShellHost.Content = shellView;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        hotkeyRegistrar.Attach(this);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        hotkeyRegistrar.Dispose();
    }
}