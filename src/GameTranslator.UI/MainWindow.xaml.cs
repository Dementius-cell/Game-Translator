using System.Windows;
using GameTranslator.UI.Views;

namespace GameTranslator.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(ShellView shellView)
    {
        InitializeComponent();
        ShellHost.Content = shellView;
    }
}
