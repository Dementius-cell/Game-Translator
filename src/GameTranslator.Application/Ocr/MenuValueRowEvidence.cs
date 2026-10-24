using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Ocr;

/// <summary>Finds compact value-like neighbors without reading or joining neighboring OCR text.</summary>
public static class MenuValueRowEvidence
{
    public static IReadOnlyList<TextCandidate> Annotate(IEnumerable<TextCandidate> candidates, OcrRequest request)
    {
        var items = candidates.ToArray();
        if (request.ContentLayoutMode != ContentLayoutMode.StaticMenu
            || request.OrientationMode == OcrOrientationMode.Vertical) return items;
        var eligible = items.Where(c => c.SourceCandidateCount == 1).OrderBy(c => c.Bounds.X).ToArray();
        var marked = new HashSet<TextCandidate>();
        foreach (var anchor in eligible)
        {
            var a = anchor.Bounds;
            var neighbors = eligible.Where(c =>
            {
                var b = c.Bounds;
                var h = Math.Min(a.Height, b.Height);
                return Math.Max(a.Height, b.Height) <= h * 1.35
                    && Math.Abs(a.Y + a.Height / 2.0 - b.Y - b.Height / 2.0) <= h * .25;
            }).ToArray();
            // Use only bounded consecutive triples; never bridge distant columns or a long chain.
            for (var i = 0; i + 2 < neighbors.Length; i++)
            {
                var triple = neighbors.Skip(i).Take(3).ToArray();
                if (!triple.Contains(anchor)) continue;
                var h = triple.Min(c => c.Bounds.Height);
                if (triple.Max(c => c.Bounds.Height) > h * 1.35) continue;
                if (Enumerable.Range(0, 2).Any(j =>
                    triple[j + 1].Bounds.X - triple[j].Bounds.Right > h * 2
                    || triple[j + 1].Bounds.X - triple[j].Bounds.Right < -h * .4)) continue;
                if (triple.Count(c => c.Bounds.Width >= c.Bounds.Height * 2.5) < 2
                    || !triple.Any(c => c.Bounds.Width >= c.Bounds.Height * .25
                        && c.Bounds.Width <= c.Bounds.Height * 1.3)) continue;
                foreach (var candidate in triple) marked.Add(candidate);
            }
        }
        return items.Select(c => c with { HasMenuValueRowEvidence = marked.Contains(c) }).ToArray();
    }
}
