namespace GameTranslator.Application.Ocr;

/// <summary>
/// Conservative horizontal paragraph grouping for menu Auto. Short labels, rows of
/// values and visibly separated paragraphs remain independent detector candidates.
/// </summary>
public static class MenuDescriptionCandidateGrouping
{
    public static IReadOnlyList<TextCandidate> Group(IReadOnlyList<TextCandidate> candidates)
    {
        var remaining = candidates.OrderBy(c => c.Bounds.Y).ThenBy(c => c.Bounds.X).ToList();
        var output = new List<TextCandidate>();
        while (remaining.Count > 0)
        {
            var first = remaining[0];
            remaining.RemoveAt(0);
            var members = new List<TextCandidate> { first };
            var previous = first.Bounds;
            // Long prose-like lines are evidence; a tight list of short buttons is not.
            if (previous.Width >= previous.Height * 12)
            {
                while (members.Count < 32)
                {
                    var heights = members.Select(c => c.Bounds.Height).Order().ToArray();
                    var typicalHeight = (heights[(heights.Length - 1) / 2] + heights[heights.Length / 2]) / 2.0;
                    var next = remaining.Where(c => Continues(first.Bounds, previous, c.Bounds, typicalHeight, heights[0], heights[^1]))
                        .OrderBy(c => c.Bounds.Y).ThenBy(c => c.Bounds.X).FirstOrDefault();
                    if (next is null) break;
                    // Only reunite same-line fragments when a wrapped continuation
                    // already provides paragraph evidence (e.g. an inline key glyph).
                    var inline = remaining.Where(c => c != next
                        && Math.Abs(c.Bounds.Y - previous.Y) <= previous.Height * .25
                        && c.Bounds.X >= previous.Right
                        && c.Bounds.X - previous.Right <= previous.Height * 2
                        && c.Bounds.Right <= next.Bounds.Right + previous.Height
                        && c.Bounds.Width >= previous.Height * 2
                        && c.Bounds.Height <= previous.Height * 1.35).ToArray();
                    var envelope = Union(members.Select(c => c.Bounds).Concat(inline.Select(c => c.Bounds)).Append(next.Bounds));
                    // Never absorb a neighboring column or a foreign element inside the crop.
                    if (remaining.Any(c => c != next && !inline.Contains(c) && Intersects(envelope, c.Bounds))) break;
                    foreach (var fragment in inline) { members.Add(fragment); remaining.Remove(fragment); }
                    members.Add(next);
                    remaining.Remove(next);
                    previous = next.Bounds;
                }
            }
            output.Add(members.Count == 1 ? first : new TextCandidate(
                Union(members.Select(c => c.Bounds)), members.Min(c => c.Confidence))
            {
                SourceCandidateBounds = members.SelectMany(c => c.SourceCandidateBounds).ToArray(),
            });
        }
        return output;
    }

    private static bool Continues(BoundingBox first, BoundingBox previous, BoundingBox next,
        double typicalHeight, double minimumHeight, double maximumHeight)
    {
        var height = Math.Min(typicalHeight, next.Height);
        var gap = next.Y - previous.Bottom;
        return next.Height <= typicalHeight * 1.35 && typicalHeight <= next.Height * 1.35
            // Prevent a chain of gradually changing font sizes from bridging title and body.
            && Math.Max(maximumHeight, next.Height) <= Math.Min(minimumHeight, next.Height) * 2
            && next.Width >= next.Height * 2
            && gap >= 0 && gap <= height * 0.85
            && Math.Abs(next.X - first.X) <= height * 0.45
            && next.Width <= Math.Max(first.Width, previous.Width) * 1.65;
    }

    private static bool Intersects(BoundingBox a, BoundingBox b) =>
        a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    private static BoundingBox Union(IEnumerable<BoundingBox> bounds)
    {
        var boxes = bounds.ToArray();
        var x = boxes.Min(b => b.X);
        var y = boxes.Min(b => b.Y);
        return new BoundingBox(x, y, boxes.Max(b => b.Right) - x, boxes.Max(b => b.Bottom) - y);
    }
}
