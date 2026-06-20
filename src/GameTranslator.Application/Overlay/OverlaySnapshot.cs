using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Overlay;

public sealed class OverlaySnapshot
{
    public OverlaySnapshot(
        IEnumerable<OverlayTextItem> textItems,
        DateTimeOffset shownAt,
        OverlaySettings? overlaySettings = null,
        IEnumerable<OverlayMaskItem>? maskItems = null,
        IEnumerable<OverlayDebugItem>? debugItems = null,
        IEnumerable<string>? debugMetricLines = null)
    {
        ArgumentNullException.ThrowIfNull(textItems);

        TextItems = textItems.ToArray();
        ShownAt = shownAt;
        OverlaySettings = overlaySettings ?? OverlaySettings.Default;
        MaskItems = maskItems?.ToArray() ?? Array.Empty<OverlayMaskItem>();
        DebugItems = debugItems?.ToArray() ?? Array.Empty<OverlayDebugItem>();
        DebugMetricLines = debugMetricLines?
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToArray() ?? Array.Empty<string>();
    }

    public IReadOnlyList<OverlayTextItem> TextItems { get; }

    public IReadOnlyList<OverlayMaskItem> MaskItems { get; }

    public IReadOnlyList<OverlayDebugItem> DebugItems { get; }

    public IReadOnlyList<string> DebugMetricLines { get; }

    public DateTimeOffset ShownAt { get; }

    public OverlaySettings OverlaySettings { get; }
}