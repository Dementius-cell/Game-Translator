using System.Windows;
using System.Windows.Controls;
using GameTranslator.UI.ViewModels;

namespace GameTranslator.UI.Views;

public partial class ShellView : UserControl
{
    private readonly ShellViewModel viewModel;

    public ShellView(ShellViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await viewModel.InitializeAsync();
    }
}
