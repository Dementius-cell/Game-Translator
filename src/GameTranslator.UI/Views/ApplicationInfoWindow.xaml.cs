using System.Diagnostics;
using System.Windows;
using GameTranslator.Application.Abstractions;
using GameTranslator.UI.ViewModels;

namespace GameTranslator.UI.Views;

public partial class ApplicationInfoWindow : Window
{
    private readonly IApplicationLogger logger;
    public ApplicationInfoWindow(ApplicationInfoViewModel viewModel, IApplicationLogger logger)
    {
        InitializeComponent();
        this.logger = logger;
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }

    private void OnProjectLinkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ApplicationInfoViewModel.ProjectUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Could not open the project website.");
            MessageBox.Show(this, $"Не удалось открыть браузер. Адрес проекта: {ApplicationInfoViewModel.ProjectUrl}", "GitHub");
        }
    }
}
