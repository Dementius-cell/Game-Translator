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
        var supportedJitter = 0;
        foreach (var pair in Order(anchorMembers).Zip(Order(currentMembers)))
        {
            if (RelativeMatch(pair.First, pair.Second)) continue;
            // One long line may jitter while its siblings and the enclosing paragraph
            // still match the fixed anchor. Never extend this to standalone labels,
            // a whole moving paragraph, splits, or more than one uncertain member.
            if (anchorMembers.Count < 2 || ++supportedJitter > 1
                || !SupportedLineJitter(pair.First, pair.Second)) return false;
        }
        return true;
    }

    private static bool SupportedLineJitter(BoundingBox anchor, BoundingBox current)
    {
        var smallerHeight = Math.Min(anchor.Height, current.Height);
        var largerHeight = Math.Max(anchor.Height, current.Height);
        var smallerWidth = Math.Min(anchor.Width, current.Width);
        var largerWidth = Math.Max(anchor.Width, current.Width);
        if (smallerWidth < 6L * largerHeight || smallerHeight < .9 * largerHeight
            || smallerWidth < .98 * largerWidth) return false;
        var tolerance = smallerHeight * .15;
        if (Math.Abs(anchor.X - current.X) > tolerance || Math.Abs(anchor.Y - current.Y) > tolerance
            || Math.Abs(anchor.Right - current.Right) > tolerance
            || Math.Abs(anchor.Bottom - current.Bottom) > tolerance) return false;
        var width = Math.Min(anchor.Right, current.Right) - Math.Max(anchor.X, current.X);
        var height = Math.Min(anchor.Bottom, current.Bottom) - Math.Max(anchor.Y, current.Y);
        if (width <= 0 || height <= 0) return false;
        var intersection = (long)width * height;
        var union = (long)anchor.Width * anchor.Height + (long)current.Width * current.Height - intersection;
        return intersection / (double)union >= .80;
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
        var ordinaryMatch = intersection / (double)union >= .85
            && Math.Abs(anchor.X - current.X) <= tolerance && Math.Abs(anchor.Y - current.Y) <= tolerance
            && Math.Abs(anchor.Right - current.Right) <= tolerance && Math.Abs(anchor.Bottom - current.Bottom) <= tolerance;
        if (ordinaryMatch) return true;

        // Low-contrast line edges can contract around the same glyph cores. This is
        // a nested resize, not an enlarged motion tolerance: centers and line width
        // must remain almost unchanged, and every member is checked independently.
        var outer = anchor.Height >= current.Height ? anchor : current;
        var inner = anchor.Height >= current.Height ? current : anchor;
        return outer.Width >= 3 * (long)outer.Height
            && inner.X >= outer.X && inner.Y >= outer.Y
            && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom
            && inner.Width >= .95 * outer.Width
            && intersection / (double)union >= .75
            && Math.Abs((long)anchor.X * 2 + anchor.Width - ((long)current.X * 2 + current.Width)) <= .1 * outer.Height
            && Math.Abs((long)anchor.Y * 2 + anchor.Height - ((long)current.Y * 2 + current.Height)) <= .1 * outer.Height;
    }
}
