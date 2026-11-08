namespace GameTranslator.Application.Pipeline;

/// <summary>Local, pixel-free menu authority and scheduler evidence. Queue counters are session totals.</summary>
public sealed record MenuLiveDiagnostics(long Generation, long TextVersion, Guid? SourceId,
    long? SourceTimestampTicks, double? SourceAgeMilliseconds, bool CachedReuse,
    int ObservationCount, bool Confirmed, bool Periodic, int ActiveOcrCount, int PendingOcrCount,
    long ReplacedPendingCount, long ExpiredPendingCount, string Decision, double? WorkAgeMilliseconds)
{
    public int? CompatibleDetections { get; init; }
    public int? DetectedRegions { get; init; }
    public bool HadTranslation { get; init; }
    public bool HadPublishedTranslation { get; init; }
    public int TranslationItems { get; init; }
    public double? QueueWaitMilliseconds { get; init; }
    public string? QueueBlockReason { get; init; }
    public double? LastAcceptedSourceAgeMilliseconds { get; init; }
    public long? TranslatingVersion { get; init; }
    public bool TranslationInFlight { get; init; }
    public bool AwaitingGeometry { get; init; }
    public Guid? LastPublishedSnapshotId { get; init; }
}

/// <summary>Bounded publication membership; no text or pixels.</summary>
public sealed record OverlayCandidateDiagnostic(string CandidateId, long? Generation, long? TextVersion, int TextItems);
