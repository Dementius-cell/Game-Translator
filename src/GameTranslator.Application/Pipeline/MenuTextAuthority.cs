using System.Text;
using GameTranslator.Application.Capture;

namespace GameTranslator.Application.Pipeline;

/// <summary>Text authority for one menu track generation; called by the session authority only.</summary>
internal sealed class MenuTextAuthority(int requiredObservations, TimeSpan requiredDuration)
{
    private readonly int requiredCount = Math.Max(2, requiredObservations);
    private readonly TimeSpan minimumDuration = requiredDuration > TimeSpan.FromMilliseconds(200)
        ? requiredDuration : TimeSpan.FromMilliseconds(200);
    private CaptureFrameOrigin? lastOrigin;
    private string? signature;
    private TimeSpan firstObservedAt;
    private int count;
    internal long Version { get; private set; }
    internal bool Confirmed { get; private set; }
    internal int ObservationCount => count;

    internal bool Observe(CaptureFrameOrigin origin, IEnumerable<string> groups, TimeSpan now)
    {
        Expire(now);
        if (!Fresh(origin, now) || (lastOrigin is { } previous
            && (origin.SourceId != previous.SourceId || origin.SourceTimestamp <= previous.SourceTimestamp))) return false;
        var next = CreateSignature(groups);
        if (next != signature || next is null)
        {
            Version++;
            signature = next;
            count = 0;
            Confirmed = false;
            firstObservedAt = origin.FreshnessTimestamp;
        }
        lastOrigin = origin;
        if (next is not null)
        {
            if (count == 0) firstObservedAt = origin.FreshnessTimestamp;
            count = Math.Min(requiredCount, count + 1);
            Confirmed |= count >= requiredCount && origin.FreshnessTimestamp - firstObservedAt >= minimumDuration;
        }
        return true;
    }

    internal bool Expire(TimeSpan now)
    {
        if (lastOrigin is null || Fresh(lastOrigin, now)) return false;
        var changed = signature is not null || Confirmed;
        if (changed) Version++;
        signature = null;
        count = 0;
        Confirmed = false;
        // Preserve last identity: expiry cannot make a repeated source independent again.
        return changed;
    }

    internal bool CanPublish(long version, TimeSpan now)
        => version == Version && Confirmed && lastOrigin is { } origin && Fresh(origin, now);

    // Unvalidated geometry may revoke contradicted text, but cannot confirm new text.
    internal bool RevokeIfDifferent(CaptureFrameOrigin origin, IEnumerable<string> groups, TimeSpan now)
    {
        if (signature is null || !Fresh(origin, now) || lastOrigin is not { } previous
            || origin.SourceId != previous.SourceId || origin.SourceTimestamp <= previous.SourceTimestamp
            || CreateSignature(groups) == signature) return false;
        Version++;
        signature = null;
        count = 0;
        Confirmed = false;
        return true;
    }

    private static string? CreateSignature(IEnumerable<string> groups)
    {
        var normalized = groups.Select(Normalize).ToArray();
        return normalized.Length == 0 || normalized.All(string.IsNullOrEmpty) ? null
            : string.Concat(normalized.Select(text => text.Length + ":" + text));
    }

    private static bool Fresh(CaptureFrameOrigin origin, TimeSpan now)
        => now >= origin.FreshnessTimestamp && now - origin.FreshnessTimestamp <= TimeSpan.FromSeconds(2);

    private static string Normalize(string text)
        => string.Join(" ", text.Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
