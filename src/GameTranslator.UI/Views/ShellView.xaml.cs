using System.Windows.Controls;
using GameTranslator.UI.ViewModels;

namespace GameTranslator.UI.Views;

public partial class ShellView : UserControl
{
    public ShellView(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
