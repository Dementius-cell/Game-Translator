using GameTranslator.Application.Capture;

namespace GameTranslator.Application.Pipeline;

/// <summary>
/// Scheduling only: a lease is not permission to translate or publish its result.
/// The live-session authority must serialize calls, supply a common monotonic clock,
/// and complete every lease only after its OCR has actually stopped (including faults).
/// </summary>
internal sealed class MenuOcrScheduler<T> : IDisposable
{
    internal static readonly TimeSpan MinimumStartInterval = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan VisualWaitBudget = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan MaximumSourceAge = TimeSpan.FromSeconds(2);
    private readonly int concurrency;
    private readonly TimeSpan visualQuietInterval;
    private readonly Dictionary<string, Track> tracks = new(StringComparer.Ordinal);
    private readonly Dictionary<long, Lease> active = new();
    private long generation;
    private long queueSequence;
    private TimeSpan? lastNow;
    private bool disposed;

    internal MenuOcrScheduler(int concurrency = 3, TimeSpan? visualQuietInterval = null)
    {
        if (concurrency < 1) throw new ArgumentOutOfRangeException(nameof(concurrency));
        this.concurrency = concurrency;
        this.visualQuietInterval = visualQuietInterval ?? TimeSpan.FromMilliseconds(200);
        if (this.visualQuietInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visualQuietInterval));
    }

    internal int ActiveCount => active.Count;
    internal bool HasActive(string trackId) => tracks.TryGetValue(trackId, out var track) && active.ContainsKey(track.Generation);
    internal int PendingCount => tracks.Values.Count(t => t.Pending is not null);
    internal long ReplacedPendingCount { get; private set; }
    internal long ExpiredPendingCount { get; private set; }

    internal long Register(string trackId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        var track = new Track(checked(++generation));
        tracks.Add(trackId, track);
        return track.Generation;
    }

    internal OfferOutcome Offer(string trackId, T work, CaptureFrameOrigin? origin,
        bool pixelsChanged, TimeSpan now)
    {
        CheckNow(now);
        var track = tracks[trackId];
        if (origin is null) return OfferOutcome.UnknownOrigin;
        if (track.LastOrigin is { } previous && origin.SourceId != previous.SourceId)
        {
            Remove(trackId);
            return OfferOutcome.ContextChanged;
        }
        if (!IsFresh(origin, now)) return OfferOutcome.ExpiredOrFuture;
        if (track.LastOrigin is { } last && origin.SourceTimestamp <= last.SourceTimestamp)
            return OfferOutcome.DuplicateOrOutOfOrder;

        var first = track.FirstObservedAt is null;
        track.FirstObservedAt ??= now;
        if (first || pixelsChanged) track.LastChangedAt = now;
        if (!first && pixelsChanged) track.ChangedFrames = Math.Min(3, track.ChangedFrames + 1);
        track.LastOrigin = origin;
        UpdatePeriodic(track, now);
        if (track.Pending is null) track.QueueSequence = checked(++queueSequence);
        else ReplacedPendingCount++;
        track.Pending = new Pending(work, origin);
        return OfferOutcome.Queued;
    }

    internal bool IsPeriodic(string trackId) => tracks[trackId].Periodic;

    internal Lease? TryStartNext(TimeSpan now, Func<T, bool>? canStart = null)
    {
        CheckNow(now);
        Track? selected = null;
        string? selectedId = null;
        foreach (var (id, track) in tracks)
        {
            UpdatePeriodic(track, now);
            if (track.Pending is not { } pending) continue;
            if (!IsFresh(pending.Origin, now))
            {
                track.Pending = null;
                ExpiredPendingCount++;
                continue;
            }
            if (active.ContainsKey(track.Generation)
                || (canStart is not null && !canStart(pending.Work))
                || (track.LastStartedAt is { } started && now - started < MinimumStartInterval)
                || (!track.Periodic && now - track.LastChangedAt < visualQuietInterval)) continue;
            if (selected is null || track.QueueSequence < selected.QueueSequence)
            {
                selected = track;
                selectedId = id;
            }
        }
        if (selected is null || active.Count >= concurrency) return null;
        var item = selected.Pending!;
        var lease = new Lease(selectedId!, selected.Generation, item.Work, item.Origin, now);
        selected.Pending = null;
        selected.LastStartedAt = now;
        active.Add(selected.Generation, lease);
        return lease;
    }

    /// <returns>Whether the original track generation still owns this result.
    /// Freshness and text authority must additionally be checked by the consumer.</returns>
    internal bool Complete(Lease lease)
    {
        if (!active.TryGetValue(lease.Generation, out var current) || !ReferenceEquals(current, lease))
            return false;
        active.Remove(lease.Generation);
        lease.Release();
        return !disposed && tracks.TryGetValue(lease.TrackId, out var track)
            && track.Generation == lease.Generation;
    }

    internal void Remove(string trackId)
    {
        if (!tracks.Remove(trackId, out var track)) return;
        // Cancellation is a request, not completion. Keep the global slot until Complete.
        if (active.TryGetValue(track.Generation, out var lease)) lease.Cancel();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        tracks.Clear();
        foreach (var lease in active.Values) lease.Cancel();
    }

    private void CheckNow(TimeSpan now)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (lastNow is { } last && now < last)
            throw new ArgumentOutOfRangeException(nameof(now), "The scheduling clock must be monotonic.");
        lastNow = now;
    }

    private static bool IsFresh(CaptureFrameOrigin origin, TimeSpan now)
        => now >= origin.FreshnessTimestamp && now - origin.FreshnessTimestamp <= MaximumSourceAge;

    private static void UpdatePeriodic(Track track, TimeSpan now)
    {
        if (track.ChangedFrames >= 3 && track.FirstObservedAt is { } first && now - first >= VisualWaitBudget)
            track.Periodic = true;
    }

    internal enum OfferOutcome { Queued, UnknownOrigin, ExpiredOrFuture, DuplicateOrOutOfOrder, ContextChanged }

    internal sealed class Lease
    {
        private readonly CancellationTokenSource cancellation = new();
        internal Lease(string trackId, long generation, T work, CaptureFrameOrigin origin, TimeSpan startedAt)
        {
            TrackId = trackId;
            Generation = generation;
            Work = work;
            Origin = origin;
            StartedAt = startedAt;
            CancellationToken = cancellation.Token;
        }
        internal string TrackId { get; }
        internal long Generation { get; }
        internal T Work { get; }
        internal CaptureFrameOrigin Origin { get; }
        internal TimeSpan StartedAt { get; }
        internal CancellationToken CancellationToken { get; }
        internal void Cancel() => cancellation.Cancel();
        internal void Release() => cancellation.Dispose();
    }

    private sealed record Pending(T Work, CaptureFrameOrigin Origin);
    private sealed class Track(long generation)
    {
        internal long Generation { get; } = generation;
        internal CaptureFrameOrigin? LastOrigin { get; set; }
        internal TimeSpan? FirstObservedAt { get; set; }
        internal TimeSpan LastChangedAt { get; set; }
        internal TimeSpan? LastStartedAt { get; set; }
        internal int ChangedFrames { get; set; }
        internal bool Periodic { get; set; }
        internal Pending? Pending { get; set; }
        internal long QueueSequence { get; set; }
    }
}
