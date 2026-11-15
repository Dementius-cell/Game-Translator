using System.Security.Cryptography;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;

namespace GameTranslator.Application.Pipeline;

/// <summary>Exact tile evidence for detector scheduling, never proof of OCR text.</summary>
internal sealed class MenuRegionChangeMap
{
    internal const int TileSize = 64;
    private byte[]? hashes;
    private CaptureRegion region;
    private CaptureFrameOrigin? origin;
    private TimeSpan lastFull;
    internal string BaselineState { get; private set; } = "not-initialized";

    internal MenuRegionPlan Plan(CapturedFrame frame, IReadOnlyList<BoundingBox> candidates, TimeSpan now,
        CancellationToken cancellationToken = default)
    {
        var whole = new BoundingBox(0, 0, frame.Width, frame.Height);
        var cols = (frame.Width + TileSize - 1) / TileSize;
        var rows = (frame.Height + TileSize - 1) / TileSize;
        MenuRegionPlan Full(string reason, byte[]? next = null, int changed = 0, string? detail = null) =>
            new(reason, true, [whole], changed, cols * rows, next) { Detail = detail };
        if (frame.PixelFormat != "Bgra32") return Full("unsupported-frame", detail: "pixel-format");
        if (frame.Stride != (long)frame.Width * 4) return Full("unsupported-frame", detail: "stride");
        if ((long)frame.Width * frame.Height > 16777216) return Full("unsupported-frame", detail: "pixel-limit");
        if ((long)cols * rows > 8192) return Full("unsupported-frame", detail: "tile-limit");
        if (frame.Origin is not { } current) return Full("untrusted-source", detail: "missing-origin");
        if (now < current.FreshnessTimestamp) return Full("untrusted-source", detail: "future-source");
        if (now - current.FreshnessTimestamp > TimeSpan.FromSeconds(2))
            return Full("untrusted-source", detail: "expired-source");
        if (frame.IsCachedReuse)
        {
            // A full detector pass can establish geometry for a recent cached delivery.
            // Hashes are only committed after success; cached pixels never become OCR evidence.
            MenuRegionPlan VerifyFull(string detail) => Full("untrusted-source",
                HashTiles(frame, cols, rows, cancellationToken), detail: detail);
            // Exact replays may reuse the prior result. A newer monotonic source proceeds
            // through the shared exact tile-diff path below; neither path renews text evidence.
            if (hashes is null) return VerifyFull("cached-no-baseline");
            if (origin?.SourceId == current.SourceId
                && (current.SourceTimestamp < origin.SourceTimestamp || current.FreshnessTimestamp < origin.FreshnessTimestamp))
                return Full("untrusted-source", detail: "cached-source-regressed");
            if (region != frame.Region) return VerifyFull("cached-region-mismatch");
            if (origin?.SourceId != current.SourceId) return VerifyFull("cached-source-mismatch");
            if (current.SourceTimestamp == origin.SourceTimestamp)
            {
                if (origin.FreshnessTimestamp != current.FreshnessTimestamp)
                    return Full("untrusted-source", detail: "cached-freshness-mismatch");
                var repeated = HashTiles(frame, cols, rows, cancellationToken);
                if (!repeated.AsSpan().SequenceEqual(hashes))
                    return Full("untrusted-source", detail: "cached-pixels-mismatch");
                if (now - lastFull >= TimeSpan.FromSeconds(2)) return Full("periodic-audit", repeated);
                return new("cached-source-reused", false, [], 0, cols * rows, hashes);
            }
            if (current.SourceTimestamp < origin.SourceTimestamp)
                return Full("untrusted-source", detail: "cached-source-regressed");
        }
        var next = HashTiles(frame, cols, rows, cancellationToken);
        if (hashes is null) return Full("initial-or-context", next, detail: "no-baseline");
        if (region != frame.Region) return Full("initial-or-context", next, detail: "region-mismatch");
        if (hashes.Length != next.Length) return Full("initial-or-context", next, detail: "tile-layout-mismatch");
        if (origin?.SourceId != current.SourceId) return Full("initial-or-context", next, detail: "source-mismatch");
        if (current.SourceTimestamp <= origin.SourceTimestamp) return Full("source-gap", next, detail: "non-increasing-source");
        if (current.FreshnessTimestamp < origin.FreshnessTimestamp) return Full("source-gap", next, detail: "freshness-regressed");
        if (current.FreshnessTimestamp - origin.FreshnessTimestamp > TimeSpan.FromSeconds(2))
            return Full("source-gap", next, detail: "source-gap-exceeded");
        if (now - lastFull >= TimeSpan.FromSeconds(2)) return Full("periodic-audit", next);

        var dirty = new bool[cols * rows];
        var count = 0;
        var broad = false;
        for (var y = 0; y < rows; y++)
        {
            var rowCount = 0;
            for (var x = 0; x < cols; x++)
            {
                var i = y * cols + x;
                if (next.AsSpan(i * 32, 32).SequenceEqual(hashes.AsSpan(i * 32, 32))) continue;
                dirty[i] = true;
                rowCount++;
                count++;
            }
            broad |= rowCount >= Math.Max(4, Math.Ceiling(cols * .75));
        }
        // A horizontal band may cross real letters. Classify it; never erase its evidence.
        if (broad) return Full("broad-change", next, count);
        if (count > dirty.Length * .45) return Full("large-change", next, count);
        var areas = new List<BoundingBox>();
        var queue = new Queue<int>();
        for (var i = 0; i < dirty.Length; i++)
        {
            if (!dirty[i]) continue;
            dirty[i] = false;
            queue.Enqueue(i);
            var left = i % cols; var right = left; var top = i / cols; var bottom = top;
            while (queue.TryDequeue(out var cell))
            {
                var x = cell % cols; var y = cell / cols;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                    if (nx >= 0 && nx < cols && ny >= 0 && ny < rows && dirty[ny * cols + nx])
                    { dirty[ny * cols + nx] = false; queue.Enqueue(ny * cols + nx); }
            }
            areas.Add(Expand(new(left * TileSize, top * TileSize,
                Math.Min(frame.Width, (right + 1) * TileSize) - left * TileSize,
                Math.Min(frame.Height, (bottom + 1) * TileSize) - top * TileSize), 16, frame));
        }
        // Close over complete existing text blocks, then merge overlaps to avoid duplicate OCR.
        var modified = true;
        while (modified)
        {
            modified = false;
            for (var i = 0; i < areas.Count; i++)
                foreach (var candidate in candidates)
                    if (Intersects(areas[i], candidate))
                    {
                        var enlarged = Union(areas[i], Expand(candidate, 16, frame));
                        if (enlarged != areas[i]) { areas[i] = enlarged; modified = true; }
                    }
            for (var i = 0; i < areas.Count; i++)
                for (var j = areas.Count - 1; j > i; j--)
                    if (Intersects(areas[i], areas[j]))
                    { areas[i] = Union(areas[i], areas[j]); areas.RemoveAt(j); modified = true; }
        }
        if (areas.Count > 4 || areas.Sum(a => (long)a.Width * a.Height) > (long)frame.Width * frame.Height / 2)
            return Full("expanded-change", next, count);
        return new(count == 0 ? "unchanged" : "local-change", false, areas.ToArray(), count, cols * rows, next);
    }

    internal void Commit(CapturedFrame frame, MenuRegionPlan plan, bool fullScan)
    {
        if (plan.Reason == "cached-source-reused") return;
        hashes = plan.Hashes;
        BaselineState = hashes is null ? "discarded-after-full-scan" : "available";
        region = frame.Region;
        origin = frame.Origin;
        if (fullScan && frame.Origin is { } current) lastFull = current.FreshnessTimestamp;
    }
    internal void Reset(string reason = "explicit-reset") { hashes = null; origin = null; BaselineState = reason; }

    private static byte[] HashTiles(CapturedFrame frame, int cols, int rows, CancellationToken token)
    {
        var result = new byte[cols * rows * 32];
        var buffer = new byte[TileSize * TileSize * 4];
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < cols; x++)
            {
                token.ThrowIfCancellationRequested();
                var width = Math.Min(TileSize, frame.Width - x * TileSize);
                var height = Math.Min(TileSize, frame.Height - y * TileSize);
                for (var row = 0; row < height; row++)
                    frame.PixelData.Span.Slice((y * TileSize + row) * frame.Stride + x * TileSize * 4, width * 4)
                        .CopyTo(buffer.AsSpan(row * width * 4));
                SHA256.HashData(buffer.AsSpan(0, width * height * 4), result.AsSpan((y * cols + x) * 32, 32));
            }
        return result;
    }
    internal static bool Intersects(BoundingBox a, BoundingBox b) =>
        a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
    private static BoundingBox Union(BoundingBox a, BoundingBox b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Max(a.Right, b.Right) - Math.Min(a.X, b.X), Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Y, b.Y));
    private static BoundingBox Expand(BoundingBox b, int pad, CapturedFrame f)
    {
        var x = Math.Max(0, b.X - pad); var y = Math.Max(0, b.Y - pad);
        return new(x, y, Math.Min(f.Width, b.Right + pad) - x, Math.Min(f.Height, b.Bottom + pad) - y);
    }
}

internal sealed record MenuRegionPlan(string Reason, bool FullScan, BoundingBox[] Areas,
    int ChangedTiles, int TotalTiles, byte[]? Hashes)
{
    public string? Detail { get; init; }
}
