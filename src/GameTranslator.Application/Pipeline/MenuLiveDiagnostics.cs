namespace GameTranslator.Application.Pipeline;

/// <summary>Local, pixel-free menu authority and scheduler evidence. Queue counters are session totals.</summary>
public sealed record MenuLiveDiagnostics(long Generation, long TextVersion, Guid? SourceId,
    long? SourceTimestampTicks, double? SourceAgeMilliseconds, bool CachedReuse,
    int ObservationCount, bool Confirmed, bool Periodic, int ActiveOcrCount, int PendingOcrCount,
    long ReplacedPendingCount, long ExpiredPendingCount, string Decision, double? WorkAgeMilliseconds);
