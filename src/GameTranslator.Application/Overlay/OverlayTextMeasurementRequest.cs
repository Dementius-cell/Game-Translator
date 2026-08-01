using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Overlay;

public sealed class OverlayTextMeasurementRequest
{
    public OverlayTextMeasurementRequest(
        string text,
        OcrZoneTextStyle textStyle,
        int maxWidth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(textStyle);

        if (maxWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth), "Overlay text measurement width must be positive.");
        }

        Text = text.Trim();
        TextStyle = textStyle;
        MaxWidth = maxWidth;
    }

    public string Text { get; }

    public OcrZoneTextStyle TextStyle { get; }

    public int MaxWidth { get; }
}
