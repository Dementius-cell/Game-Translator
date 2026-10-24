using GameTranslator.Application.Ocr;

namespace GameTranslator.Application.Pipeline;

internal static class MenuGeometryMatcher
{
    // Return target -> source only when neither endpoint has another compatible partner.
    internal static IReadOnlyDictionary<int, int> MatchUnique<TSource, TTarget>(
        IReadOnlyList<TSource> sources, IReadOnlyList<TTarget> targets, Func<TSource, TTarget, bool> compatible)
    {
        var sourceCounts = new int[sources.Count];
        var targetCounts = new int[targets.Count];
        var lastSources = new int[targets.Count];
        for (var target = 0; target < targets.Count; target++)
        for (var source = 0; source < sources.Count; source++)
        {
            if (!compatible(sources[source], targets[target])) continue;
            sourceCounts[source]++;
            targetCounts[target]++;
            lastSources[target] = source;
        }
        var result = new Dictionary<int, int>();
        for (var target = 0; target < targets.Count; target++)
            if (targetCounts[target] == 1 && sourceCounts[lastSources[target]] == 1)
                result.Add(target, lastSources[target]);
        return result;
    }

    internal static bool Matches(BoundingBox anchor, IReadOnlyList<BoundingBox> anchorMembers,
        BoundingBox current, IReadOnlyList<BoundingBox> currentMembers)
    {
        if (!RelativeMatch(anchor, current) || anchorMembers.Count != currentMembers.Count) return false;
        static IOrderedEnumerable<BoundingBox> Order(IEnumerable<BoundingBox> items)
            => items.OrderBy(b => b.Y).ThenBy(b => b.X).ThenBy(b => b.Width).ThenBy(b => b.Height);
        return Order(anchorMembers).Zip(Order(currentMembers)).All(p => RelativeMatch(p.First, p.Second));
    }

    private static bool RelativeMatch(BoundingBox anchor, BoundingBox current)
    {
        var width = Math.Min(anchor.Right, current.Right) - Math.Max(anchor.X, current.X);
        var height = Math.Min(anchor.Bottom, current.Bottom) - Math.Max(anchor.Y, current.Y);
        if (width <= 0 || height <= 0) return false;
        var intersection = (long)width * height;
        var union = (long)anchor.Width * anchor.Height + (long)current.Width * current.Height - intersection;
        var tolerance = Math.Min(anchor.Height, current.Height) * .15;
        // Detector edge quantization can change a short line's height by roughly 10%.
        // Keep that jitter within its fixed anchor; edge limits and reciprocal uniqueness
        // still prevent a rolling track from acquiring a neighboring line.
        return intersection / (double)union >= .85
            && Math.Abs(anchor.X - current.X) <= tolerance && Math.Abs(anchor.Y - current.Y) <= tolerance
            && Math.Abs(anchor.Right - current.Right) <= tolerance && Math.Abs(anchor.Bottom - current.Bottom) <= tolerance;
    }
}
