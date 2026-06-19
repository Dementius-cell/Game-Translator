using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Overlay;

public sealed class OverlaySnapshot
{
    public OverlaySnapshot(
        IEnumerable<OverlayTextItem> textItems,
        DateTimeOffset shownAt,
        OverlaySettings? overlaySettings = null)
    {
        ArgumentNullException.ThrowIfNull(textItems);

        TextItems = textItems.ToArray();
        ShownAt = shownAt;
        OverlaySettings = overlaySettings ?? OverlaySettings.Default;
    }

    public IReadOnlyList<OverlayTextItem> TextItems { get; }

    public DateTimeOffset ShownAt { get; }

    public OverlaySettings OverlaySettings { get; }
}
