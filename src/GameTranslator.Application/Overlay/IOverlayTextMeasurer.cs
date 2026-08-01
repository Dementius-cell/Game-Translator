namespace GameTranslator.Application.Overlay;

public interface IOverlayTextMeasurer
{
    OverlayTextMeasurement Measure(OverlayTextMeasurementRequest request);
}
