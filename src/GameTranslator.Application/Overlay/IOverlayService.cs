namespace GameTranslator.Application.Overlay;

/// <summary>
/// Controls the presentation overlay without exposing WPF or platform-specific window types.
/// </summary>
public interface IOverlayService
{
    bool IsVisible { get; }

    bool IsExcludedFromCapture { get; }

    OverlaySnapshot? CurrentSnapshot { get; }

    void Show(OverlaySnapshot snapshot);

    void Hide();
}
