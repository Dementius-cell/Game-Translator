using GameTranslator.UI.Views;

namespace GameTranslator.UI.Services;

public sealed class WpfScreenRegionPickerService : IScreenRegionPickerService
{
    public ScreenRegionSelectionResult? PickRegion()
    {
        var picker = new ScreenRegionPickerWindow();
        var owner = System.Windows.Application.Current?.MainWindow;
        if (owner is not null && owner.IsVisible)
        {
            picker.Owner = owner;
        }

        return picker.ShowDialog() == true
            ? picker.SelectedRegion
            : null;
    }
}
