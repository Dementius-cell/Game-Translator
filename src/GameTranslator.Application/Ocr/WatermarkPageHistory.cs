using GameTranslator.Application.Capture;

namespace GameTranslator.Application.Ocr;

/// <summary>Bounded, session/zone-owned evidence. Static refreshes and revisits do not add pages.</summary>
public sealed class WatermarkPageHistory
{
    private readonly List<Page> pages = new();
    private readonly List<Pattern> patterns = new();
    private short[]? pendingPage;
    private CaptureRegion? captureRegion;
    private int nextPageId;

    public void Observe(CapturedFrame frame, IReadOnlyList<TextCandidateRegion> regions)
    {
        if (!WatermarkFilter.CanRead(frame)) return;
        if (captureRegion != frame.Region)
        {
            pages.Clear(); patterns.Clear(); pendingPage = null; nextPageId = 0;
            captureRegion = frame.Region;
        }
        var sample = Sample(frame, 32, center: true);
        var page = pages.FirstOrDefault(p => Similar(p.Sample, sample, 28, 0.24));
        if (page is null && pages.Count < 16 && pendingPage is not null && Similar(pendingPage, sample, 12, 0.08))
        {
            page = new Page(++nextPageId, sample);
            // Once the bounded catalog fills, stop collecting new evidence rather than
            // count an evicted page again. A new live session starts a fresh catalog.
            pages.Add(page);
        }
        pendingPage = sample;
        foreach (var region in regions)
        {
            var visual = region.WatermarkEvidence ?? WatermarkFilter.Analyze(region);
            if (visual is null) continue;
            if (region.Frame.Width < 12 || region.Frame.Height < 8)
            {
                region.WatermarkEvidence = visual;
                continue;
            }
            var signature = Sample(region.Frame, 16, center: true);
            var aspect = region.Frame.Width / (double)region.Frame.Height;
            var pattern = patterns.FirstOrDefault(p => Math.Abs(p.Aspect - aspect) <= 0.1
                && Similar(p.Sample, signature, 16, 0.08));
            if (pattern is null && patterns.Count < 128)
            {
                pattern = new Pattern(aspect, signature);
                patterns.Add(pattern);
            }
            if (page is not null) pattern?.PageIds.Add(page.Id);
            region.WatermarkEvidence = visual with { DistinctPages = pattern?.PageIds.Count ?? 1 };
        }
    }

    private static short[] Sample(CapturedFrame frame, int side, bool center)
    {
        var values = new short[side * side * 3];
        var pixels = frame.PixelData.Span;
        for (var y = 0; y < side; y++)
        for (var x = 0; x < side; x++)
        {
            var offset = Math.Min(frame.Height - 1, (2 * y + 1) * frame.Height / (2 * side)) * frame.Stride
                + Math.Min(frame.Width - 1, (2 * x + 1) * frame.Width / (2 * side)) * 4;
            for (var c = 0; c < 3; c++) values[(y * side + x) * 3 + c] = pixels[offset + c];
        }
        if (center)
        for (var c = 0; c < 3; c++)
        {
            var sum = 0;
            for (var i = c; i < values.Length; i += 3) sum += values[i];
            var mean = sum / (side * side);
            for (var i = c; i < values.Length; i += 3) values[i] -= (short)mean;
        }
        return values;
    }

    private static bool Similar(short[] a, short[] b, int tolerance, double maximumChanged)
    {
        var changed = 0;
        for (var i = 0; i < a.Length; i += 3)
            if (Math.Abs(a[i] - b[i]) > tolerance || Math.Abs(a[i + 1] - b[i + 1]) > tolerance
                || Math.Abs(a[i + 2] - b[i + 2]) > tolerance) changed++;
        return changed <= a.Length / 3 * maximumChanged;
    }

    private sealed record Page(int Id, short[] Sample);
    private sealed record Pattern(double Aspect, short[] Sample)
    {
        public HashSet<int> PageIds { get; } = new();
    }
}
