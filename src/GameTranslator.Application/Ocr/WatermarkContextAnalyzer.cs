using GameTranslator.Application.Capture;

namespace GameTranslator.Application.Ocr;

/// <summary>Finds a bounded illustrated enclosure around OCR fragments, without named templates.
/// It only supplies evidence; neither candidate grouping nor OCR/mask bounds are changed.</summary>
public static class WatermarkContextAnalyzer
{
    private const int MaximumExtent = 384;
    private const int EdgeSamples = 24;

    public static WatermarkVisualEvidence? Analyze(CapturedFrame page, TextCandidateRegion region, IReadOnlyList<TextCandidateRegion>? neighbors = null)
    {
        var original = WatermarkFilter.Analyze(region);
        if (!WatermarkFilter.CanRead(page) || !region.Candidate.Bounds.IsWithin(page.Width, page.Height)) return original;
        var seed = region.Candidate.Bounds;
        if (seed.Width < 12 || seed.Height < 8
            || seed.Width > MaximumExtent || seed.Height > MaximumExtent) return original;
        if (original?.LogoStructure == true)
        {
            var measured = AnalyzeEnclosure(page, seed);
            return original with
            {
                LocalContrast = measured.LocalContrast, ContextGlobalContrast = measured.ContextGlobalContrast,
                ContextBounds = seed, ContextFingerprint = measured.ContextFingerprint,
            };
        }
        var extent = Math.Min(MaximumExtent, Math.Max(seed.Width, seed.Height) * 2);
        var tops = FindEdges(page, Math.Max(2, seed.Y - extent), seed.Y, seed.X, seed.Right, horizontal: true);
        var bottoms = FindEdges(page, seed.Bottom, Math.Min(page.Height - 3, seed.Bottom + extent), seed.X, seed.Right, horizontal: true);
        foreach (var top in tops)
        foreach (var bottom in bottoms)
        {
            var height = bottom.Position - top.Position;
            if (height < seed.Height || height > MaximumExtent || height < seed.Width * .35) continue;
            var lefts = FindEdges(page, Math.Max(2, seed.X - extent / 2), seed.X, top.Position + 2, bottom.Position - 2, horizontal: false);
            var rights = FindEdges(page, seed.Right, Math.Min(page.Width - 3, seed.Right + extent / 2), top.Position + 2, bottom.Position - 2, horizontal: false);
            foreach (var left in lefts)
            foreach (var right in rights)
            {
                var width = right.Position - left.Position;
                if (width < seed.Width || width > MaximumExtent || width / (double)height is < .5 or > 3.5) continue;
                // Recheck full edges: a short text underline must not borrow unrelated side edges.
                if (EdgeStrength(page, top.Position, left.Position + 2, right.Position - 2, true) < .65
                    || EdgeStrength(page, bottom.Position, left.Position + 2, right.Position - 2, true) < .65) continue;
                var box = new BoundingBox(left.Position, top.Position, width, height);
                var evidence = AnalyzeEnclosure(page, box);
                if (evidence.LogoStructure) return evidence.DetailedLogo ? evidence : AnalyzeNeighbors(page, region, neighbors) ?? evidence;
            }
        }
        return AnalyzeNeighbors(page, region, neighbors) ?? original;
    }

    private static WatermarkVisualEvidence? AnalyzeNeighbors(CapturedFrame page, TextCandidateRegion region,
        IReadOnlyList<TextCandidateRegion>? neighbors)
    {
        if (neighbors is null) return null;
        var seed = region.Candidate.Bounds;
        // This is evidence only: detector candidates, OCR requests and masks remain separate.
        // At most eight aligned source lines may supply one compact illustrated composition.
        var lines = neighbors.SelectMany(r => r.Candidate.SourceCandidateBounds)
            .Where(b => b.IsWithin(page.Width, page.Height)
                && b.Width >= seed.Width * .2 && b.Width <= seed.Width * 2
                && b.Height <= Math.Max(seed.Height * 3, 64)
                && Math.Min(seed.Right, b.Right) - Math.Max(seed.X, b.X) >= Math.Min(seed.Width, b.Width) * .6
                && Math.Max(0, Math.Max(seed.Y - b.Bottom, b.Y - seed.Bottom)) <= Math.Min(192, seed.Width))
            .Distinct()
            .OrderBy(b => Math.Abs((long)b.Y * 2 + b.Height - ((long)seed.Y * 2 + seed.Height)))
            .Take(8).ToArray();
        for (var count = 2; count <= lines.Length; count++)
        {
            var members = lines.Take(count).OrderBy(b => b.Y).ToArray();
            var x = Math.Min(seed.X, members.Min(b => b.X));
            var y = Math.Min(seed.Y, members.Min(b => b.Y));
            var right = Math.Max(seed.Right, members.Max(b => b.Right));
            var bottom = Math.Max(seed.Bottom, members.Max(b => b.Bottom));
            var box = new BoundingBox(x, y, right - x, bottom - y);
            if (box.Width > MaximumExtent || box.Height > MaximumExtent
                || box.Width / (double)box.Height is < .5 or > 3.5
                || members.Min(b => b.Width) >= members.Max(b => b.Width) * .72
                || !members.Zip(members.Skip(1), (a,b) => b.Y - a.Bottom).Any(gap => gap > box.Height * .12)) continue;
            // Dense detail must span the composition; plain subtitles and empty gaps do not vote.
            var evidence = AnalyzeEnclosure(page, box);
            if (evidence.LogoStructure && evidence.DetailedLogo) return evidence;
        }
        return null;
    }

    private static Edge[] FindEdges(CapturedFrame page, int from, int to, int spanStart, int spanEnd, bool horizontal)
    {
        var result = new List<Edge>();
        for (var position = from; position <= to; position++)
        {
            var strength = EdgeStrength(page, position, spanStart, spanEnd, horizontal);
            if (strength >= .65) result.Add(new(position, strength));
        }
        var selected = new List<Edge>();
        foreach (var edge in result.OrderByDescending(e => e.Strength).ThenBy(e => e.Position))
        {
            if (selected.All(e => Math.Abs(e.Position - edge.Position) > 5)) selected.Add(edge);
            if (selected.Count == 3) break;
        }
        return selected.ToArray();
    }

    private static double EdgeStrength(CapturedFrame page, int position, int from, int to, bool horizontal)
    {
        if (to <= from) return 0;
        var pixels = page.PixelData.Span;
        var strong = 0;
        for (var i = 0; i < EdgeSamples; i++)
        {
            var along = from + (2 * i + 1) * (to - from) / (2 * EdgeSamples);
            var a = horizontal ? (position - 2) * page.Stride + along * 4 : along * page.Stride + (position - 2) * 4;
            var b = horizontal ? (position + 2) * page.Stride + along * 4 : along * page.Stride + (position + 2) * 4;
            if (Difference(pixels, a, b) >= 18) strong++;
        }
        return strong / (double)EdgeSamples;
    }

    private static WatermarkVisualEvidence AnalyzeEnclosure(CapturedFrame page, BoundingBox box)
    {
        const int side = 48;
        var pixels = page.PixelData.Span;
        Span<int> histogram = stackalloc int[256]; histogram.Clear();
        Span<int> localHistograms = stackalloc int[9 * 256]; localHistograms.Clear();
        Span<int> tiles = stackalloc int[9]; tiles.Clear();
        Span<int> hues = stackalloc int[6]; hues.Clear();
        var quietRows = 0; var textured = 0; var colored = 0;
        var hash = 14695981039346656037UL;
        for (var y = 0; y < side; y++)
        {
            var edges = 0;
            for (var x = 0; x < side; x++)
            {
                var px = box.X + 2 + x * Math.Max(1, box.Width - 5) / side;
                var py = box.Y + 2 + y * Math.Max(1, box.Height - 5) / side;
                var offset = py * page.Stride + px * 4;
                var b = pixels[offset]; var g = pixels[offset + 1]; var r = pixels[offset + 2];
                var luminance = (r * 77 + g * 150 + b * 29) >> 8;
                histogram[luminance]++;
                localHistograms[((y / 16) * 3 + x / 16) * 256 + luminance]++;
                hash = unchecked((hash ^ (uint)(luminance / 16)) * 1099511628211UL);
                if (Math.Max(r, Math.Max(g,b)) - Math.Min(r, Math.Min(g,b)) >= 22)
                {
                    colored++;
                    hues[r >= g && r >= b ? (g >= b ? 0 : 5) : g >= r && g >= b ? (r >= b ? 1 : 2) : (g >= r ? 3 : 4)]++;
                }
                if (Difference(pixels, offset, offset + 2 * 4) >= 14
                    && Difference(pixels, offset, offset + 2 * page.Stride) >= 14)
                {
                    textured++; edges++; tiles[(y / 16) * 3 + x / 16]++;
                }
            }
            if (edges < 2) quietRows++;
        }
        var low = 0; var high = 255; var total = 0;
        for (var i = 0; i < 256; i++) { total += histogram[i]; if (total < side * side * .05) low = i; if (total < side * side * .95) high = i; }
        var occupied = 0; foreach (var tile in tiles) if (tile >= 8) occupied++;
        var hueCount = 0; foreach (var hue in hues) if (hue >= side * side * .015) hueCount++;
        var texture = textured / (double)(side * side);
        // Regular text cards have quiet rows/tiles; illustrated badges distribute detail in
        // both directions. Color helps first-page exclusion but is not mandatory structure.
        var logo = occupied >= 7 && quietRows <= 8 && texture >= .10;
        var detailed = logo && colored >= side * side * .16
            && (hueCount >= 3 || (hueCount >= 2 && occupied == 9 && texture >= .25 && quietRows <= 2));
        // Contrast inside textured tiles is independent of a panel/background brightness step.
        // Ignore empty tiles rather than letting whitespace lower the contrast of ordinary text.
        Span<double> contrasts = stackalloc double[9];
        var contrastCount = 0;
        for (var tile = 0; tile < 9; tile++)
        {
            if (tiles[tile] < 8) continue;
            var local = localHistograms.Slice(tile * 256, 256);
            var sum = 0; var lower = -1; var upper = 255;
            for (var value = 0; value < 256; value++)
            {
                sum += local[value];
                if (lower < 0 && sum >= 256 * .05) lower = value;
                if (sum >= 256 * .95) { upper = value; break; }
            }
            contrasts[contrastCount++] = (upper - lower) / 255d;
        }
        contrasts = contrasts[..contrastCount];
        contrasts.Sort();
        var globalContrast = (high - low) / 255d;
        var localContrast = contrastCount == 0 ? globalContrast : contrasts[contrastCount / 2];
        return new(localContrast, logo, detailed)
        {
            ContextBounds = box, ContextFingerprint = hash, ContextGlobalContrast = globalContrast,
            TextureCoverage = texture, QuietRowFraction = quietRows / (double)side,
        };
    }

    private static int Difference(ReadOnlySpan<byte> pixels, int a, int b) =>
        Math.Max(Math.Abs(pixels[a] - pixels[b]), Math.Max(Math.Abs(pixels[a + 1] - pixels[b + 1]), Math.Abs(pixels[a + 2] - pixels[b + 2])));
    private sealed record Edge(int Position, double Strength);
}
