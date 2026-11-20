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
    private CaptureFrameOrigin? pendingOrigin;
    internal TimeSpan? ContradictionDeadline { get; private set; }
    internal bool HasPendingContradiction => pendingOrigin is not null;
    internal long Version { get; private set; }
    internal bool Confirmed { get; private set; }
    internal int ObservationCount => count;
    internal CaptureFrameOrigin? LastEvidenceOrigin => lastOrigin;

    // The caller must prove exact pixels and detector geometry against accepted OCR.
    // This renews an already confirmed version; it never counts as another OCR observation.
    internal bool RenewUnchanged(CaptureFrameOrigin origin, TimeSpan now)
    {
        if (HasPendingContradiction || !Confirmed || lastOrigin is not { } previous || !Fresh(origin, now)
            || origin.SourceId != previous.SourceId || origin.SourceTimestamp <= previous.SourceTimestamp
            || origin.FreshnessTimestamp < previous.FreshnessTimestamp
            || !Fresh(previous, origin.FreshnessTimestamp)) return false;
        lastOrigin = origin;
        return true;
    }

    internal bool Observe(CaptureFrameOrigin origin, IEnumerable<string> groups, TimeSpan now, bool allowDeferral = false)
    {
        if (!Fresh(origin, now) || (lastOrigin is { } previous
            && (origin.SourceId != previous.SourceId || origin.SourceTimestamp <= previous.SourceTimestamp)))
        {
            Expire(now);
            return false;
        }
        // A late agreeing result must not rescue a timed-out retained snapshot.
        if (HasPendingContradiction) Expire(now);
        var next = CreateSignature(groups);
        if (next is not null && pendingOrigin is { } pending && origin.SourceTimestamp <= pending.SourceTimestamp)
            return false;
        if (next != signature && TryDefer(origin, next, now, allowDeferral)) return false;
        ClearPending();
        // A ready, validated observation can bridge collection delay, never a source gap.
        // Publication still requires this new origin to be fresh at the current time.
        // An authority already expired by an earlier collection remains cleared.
        Expire(origin.FreshnessTimestamp);
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
        if (lastOrigin is null || (Fresh(lastOrigin, now)
            && (ContradictionDeadline is not { } deadline || now < deadline))) return false;
        ClearPending();
        var changed = signature is not null || Confirmed;
        if (changed) Version++;
        signature = null;
        count = 0;
        Confirmed = false;
        // Preserve last identity: expiry cannot make a repeated source independent again.
        return changed;
    }

    internal bool CanPublish(long version, TimeSpan now)
        => version == Version && Confirmed && lastOrigin is { } origin && Fresh(origin, now)
            && (ContradictionDeadline is not { } deadline || now < deadline);

    // Unvalidated geometry may revoke contradicted text, but cannot confirm new text.
    internal bool RevokeIfDifferent(CaptureFrameOrigin origin, IEnumerable<string> groups, TimeSpan now, bool allowDeferral = false)
    {
        if (signature is null || !Fresh(origin, now) || lastOrigin is not { } previous
            || origin.SourceId != previous.SourceId || origin.SourceTimestamp <= previous.SourceTimestamp
            || CreateSignature(groups) == signature) return false;
        if (HasPendingContradiction && Expire(now)) return true;
        if (TryDefer(origin, CreateSignature(groups), now, allowDeferral)) return false;
        ClearPending();
        Version++;
        signature = null;
        count = 0;
        Confirmed = false;
        return true;
    }

    private bool TryDefer(CaptureFrameOrigin origin, string? next, TimeSpan now, bool allowed)
    {
        if (!allowed || next is null) return false;
        if (pendingOrigin is { } pending)
            return origin.SourceTimestamp <= pending.SourceTimestamp;
        if (!Confirmed || lastOrigin is not { } supporting || !Fresh(supporting, now)) return false;
        pendingOrigin = origin;
        var holdDeadline = now + TimeSpan.FromMilliseconds(750);
        var sourceDeadline = supporting.FreshnessTimestamp + TimeSpan.FromSeconds(2);
        ContradictionDeadline = holdDeadline < sourceDeadline ? holdDeadline : sourceDeadline;
        return true;
    }

    private void ClearPending()
    {
        pendingOrigin = null;
        ContradictionDeadline = null;
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
