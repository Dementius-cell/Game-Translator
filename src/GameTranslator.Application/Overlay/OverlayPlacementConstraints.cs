using GameTranslator.Application.Capture;

namespace GameTranslator.Application.Overlay;

/// <summary>
/// Supplies a bounded screen-space placement area for a transient overlay item.
/// This is used by the opt-in candidate path only; it does not persist any profile setting.
/// </summary>
public sealed class OverlayPlacementConstraints
{
    public OverlayPlacementConstraints(
        CaptureRegion placementRegion,
        IEnumerable<CaptureRegion>? occupiedRegions = null)
    {
        PlacementRegion = placementRegion;
        OccupiedRegions = (occupiedRegions ?? Array.Empty<CaptureRegion>())
            .ToArray();
    }

    /// <summary>
    /// Gets the source capture-zone rectangle within which the translated item may be placed.
    /// </summary>
    public CaptureRegion PlacementRegion { get; }

    /// <summary>
    /// Gets neighboring detector regions that a relocated translated item must not cover.
    /// </summary>
    public IReadOnlyList<CaptureRegion> OccupiedRegions { get; }
}
