namespace GameTranslator.Application.Capture;

/// <summary>
/// Identifies an actual source frame independently of delivery, cropping and OCR preparation.
/// SourceTimestamp is the compositor's monotonic timestamp, not a polling timestamp.
/// </summary>
public sealed record CaptureFrameOrigin(Guid SourceId, TimeSpan SourceTimestamp, DateTimeOffset CapturedAt)
{
    /// <summary>Actual monotonic arrival time, assigned once by the producer, never on cached delivery.</summary>
    public TimeSpan? ObservedAtTimestamp { get; init; }

    // A compositor timestamp can slightly lead arrival. Never extend freshness into the future.
    public TimeSpan FreshnessTimestamp => ObservedAtTimestamp is { } observed && observed < SourceTimestamp
        ? observed : SourceTimestamp;
}
