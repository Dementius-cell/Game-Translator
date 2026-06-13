namespace GameTranslator.Application.Overlay;

public sealed class OverlaySnapshot
{
    public OverlaySnapshot(IEnumerable<OverlayTextItem> textItems, DateTimeOffset shownAt)
    {
        ArgumentNullException.ThrowIfNull(textItems);

        TextItems = textItems.ToArray();
        ShownAt = shownAt;
    }

    public IReadOnlyList<OverlayTextItem> TextItems { get; }

    public DateTimeOffset ShownAt { get; }
}
