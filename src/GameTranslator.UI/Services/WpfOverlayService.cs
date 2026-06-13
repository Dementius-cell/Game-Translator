using GameTranslator.Application.Overlay;
using GameTranslator.UI.Views;

namespace GameTranslator.UI.Services;

public sealed class WpfOverlayService : IOverlayService
{
    private readonly OverlayWindow overlayWindow;

    public WpfOverlayService(OverlayWindow overlayWindow)
    {
        this.overlayWindow = overlayWindow;
    }

    public bool IsVisible => overlayWindow.IsVisible;

    public OverlaySnapshot? CurrentSnapshot { get; private set; }

    public void Show(OverlaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        CurrentSnapshot = snapshot;
        overlayWindow.ShowSnapshot(snapshot);
    }

    public void Hide()
    {
        overlayWindow.Hide();
    }
}
