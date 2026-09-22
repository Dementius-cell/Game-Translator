using System.Text;
using System.Text.RegularExpressions;
using GameTranslator.Application.Capture;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Ocr;

/// <summary>Numeric evidence from original capture pixels; contrast is not opacity.</summary>
public sealed record WatermarkVisualEvidence(
    double LocalContrast, bool LogoStructure, bool DetailedLogo, int DistinctPages = 1)
{
    public double ContextGlobalContrast { get; init; }
    public BoundingBox? ContextBounds { get; init; }
    public ulong ContextFingerprint { get; init; }
    public double TextureCoverage { get; init; }
    public double QuietRowFraction { get; init; }
}

public readonly record struct WatermarkEvidenceKey(bool LowContrast, bool Logo, bool Detailed, bool Repeated, ulong ContextFingerprint);

public sealed record WatermarkDecision(
    WatermarkFilterMode Mode, bool Excluded, bool LowContrast, bool LogoStructure,
    bool RepeatedPattern, bool WebsiteAddress, int DistinctPages, double LocalContrast = 0, bool DetailedLogo = false)
{
    public double ContextGlobalContrast { get; init; }
    public WatermarkEvidenceKey EvidenceKey { get; init; }
    public string Reasons => $"contrast={LowContrast};logo={LogoStructure};repeat={RepeatedPattern};address={WebsiteAddress};pages={DistinctPages};detail={DetailedLogo};context={EvidenceKey.ContextFingerprint != 0};contextContrast={ContextGlobalContrast.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)};localContrast={LocalContrast.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}";
}

public sealed record WatermarkCandidateDecision(BoundingBox Bounds, WatermarkDecision Decision);

/// <summary>Conservative candidate selection, independent of recognizer, scripts and screen corners.</summary>
public static class WatermarkFilter
{
    private static readonly Regex Address = new(
        @"(?:https?://|www\.)[^\s]{3,}|[\p{L}\p{N}][\p{L}\p{N}-]{1,62}\.(?:com|net|org|io|co|ru|tv|me|info|xyz|site|online)(?:[/?:#][^\s]*)?(?:$|[^\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static WatermarkDecision Evaluate(WatermarkFilterMode mode, WatermarkVisualEvidence? visual, string text)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == WatermarkFilterMode.Off || visual is null)
            return new(mode, false, false, false, false, false, 0);
        var low = visual.LocalContrast is > 0.02 and < 0.62;
        var repeated = visual.DistinctPages >= (mode == WatermarkFilterMode.Cautious ? 3 : 2);
        var address = HasWebsiteAddress(text);
        // An irregular, densely illustrated logo plus reduced contrast can work on the first page
        // in Strong. Cautious also requires independent address or cross-page evidence.
        var excluded = visual.LogoStructure && (mode == WatermarkFilterMode.Cautious
            ? (address && (low || repeated)) || (repeated && low && visual.DetailedLogo)
            : (address || repeated || (low && visual.DetailedLogo)));
        return new(mode, excluded, low, visual.LogoStructure, repeated, address, visual.DistinctPages, visual.LocalContrast, visual.DetailedLogo)
        { EvidenceKey = CreateEvidenceKey(mode, visual), ContextGlobalContrast = visual.ContextGlobalContrast };
    }

    public static WatermarkEvidenceKey CreateEvidenceKey(WatermarkFilterMode mode, WatermarkVisualEvidence? visual) =>
        mode == WatermarkFilterMode.Off || visual is null ? default : new(
            visual.LocalContrast is > .02 and < .62, visual.LogoStructure, visual.DetailedLogo,
            visual.DistinctPages >= (mode == WatermarkFilterMode.Cautious ? 3 : 2), visual.ContextFingerprint);

    public static bool HasWebsiteAddress(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2048) return false;
        // Bounded OCR tolerance: full-width forms, whitespace immediately around dots,
        // and two common suffix confusions. Do not turn arbitrary digits into domains.
        var normalized = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"\s*[.·]\s*", ".", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        normalized = normalized.Replace(".c0m", ".com", StringComparison.Ordinal)
            .Replace(".corn", ".com", StringComparison.Ordinal);
        return Address.IsMatch(normalized);
    }

    public static WatermarkVisualEvidence? Analyze(TextCandidateRegion region)
    {
        var frame = region.Frame;
        if (!CanRead(frame) || frame.Width < 12 || frame.Height < 12) return null;
        var pixels = frame.PixelData.Span;
        Span<int> histogram = stackalloc int[256];
        Span<int> hues = stackalloc int[6];
        Span<int> occupied = stackalloc int[9];
        histogram.Clear(); hues.Clear(); occupied.Clear();
        var count = 0;
        var colored = 0;
        var step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)frame.Width * frame.Height / 4096)));
        for (var y = 0; y < frame.Height; y += step)
        for (var x = 0; x < frame.Width; x += step)
        {
            var offset = y * frame.Stride + x * 4;
            var b = pixels[offset]; var g = pixels[offset + 1]; var r = pixels[offset + 2];
            histogram[(r * 77 + g * 150 + b * 29) >> 8]++;
            var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b));
            if (max - min >= 22)
            {
                colored++;
                var hue = r >= g && r >= b ? (g >= b ? 0 : 5)
                    : g >= r && g >= b ? (r >= b ? 1 : 2) : (g >= r ? 3 : 4);
                hues[hue]++;
                occupied[Math.Min(2, y * 3 / frame.Height) * 3 + Math.Min(2, x * 3 / frame.Width)]++;
            }
            count++;
        }
        var low = Percentile(histogram, count, 0.05);
        var high = Percentile(histogram, count, 0.95);
        var hueCount = 0; var tileCount = 0;
        foreach (var value in hues) if (value >= count * 0.015) hueCount++;
        foreach (var value in occupied) if (value >= count * 0.008) tileCount++;
        var bounds = region.Candidate.SourceCandidateBounds;
        var compact = frame.Width / (double)frame.Height is >= 0.5 and <= 3.5;
        var colorful = colored / (double)count >= 0.16 && hueCount >= 3 && tileCount >= 6;
        // Unequal line widths and a large internal gap distinguish a stacked emblem from
        // ordinary regular subtitle/dialogue rows. Neither compactness nor color alone votes.
        var rows = bounds.OrderBy(b => b.Y).ToArray();
        var irregular = rows.Length is >= 2 and <= 8
            && rows.Min(b => b.Width) < rows.Max(b => b.Width) * 0.72
            && rows.Zip(rows.Skip(1), (a,b) => b.Y - a.Bottom).Any(gap => gap > frame.Height * 0.12);
        var logo = compact && colorful && irregular;
        return new((high - low) / 255d, logo, logo && tileCount >= 8 && colored / (double)count >= 0.25);
    }

    internal static bool CanRead(CapturedFrame frame) =>
        string.Equals(frame.PixelFormat, "Bgra32", StringComparison.OrdinalIgnoreCase)
        && frame.Stride >= (long)frame.Width * 4;

    private static int Percentile(ReadOnlySpan<int> histogram, int count, double fraction)
    {
        var sum = 0;
        for (var i = 0; i < histogram.Length; i++)
        {
            sum += histogram[i];
            if (sum >= count * fraction) return i;
        }
        return 255;
    }
}
