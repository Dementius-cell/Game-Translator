using System.Text.RegularExpressions;
using GameTranslator.Application.Ocr;

namespace GameTranslator.Application.Content;

/// <summary>Separates action captions around explicit OCR key glyphs without altering raw OCR.</summary>
public static partial class MenuControlTextProjection
{
    public static OcrResult? TryCreate(OcrResult raw)
    {
        // This is a single detector line, never a wrapped description or whole-page heuristic.
        if (raw.Request.DetectorLineBounds.Count != 1 || raw.TextBlocks.Count != 1
            || raw.Request.OrientationMode == Domain.Profiles.OcrOrientationMode.Vertical)
            return null;
        var words = raw.Words.OrderBy(w => w.Bounds.X).ToArray();
        if (words.Length < 2 || words.Length > 16
            || words.Select(w => w.RecognitionPassId).Distinct().Count() != 1
            || words.Any(w => w.Confidence is null or < 40)) return null;
        // Require complete word evidence; never guess missing words or assign invented bounds.
        static string Compact(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        if (Compact(raw.TextBlocks[0].Text) != Compact(string.Join(' ', words.Select(w => w.Text)))) return null;
        var height = words.Max(w => w.Bounds.Height);
        if (words.Max(w => w.Bounds.Y) - words.Min(w => w.Bounds.Y) > height * .5) return null;
        bool IsKey(OcrWord w) => Key().IsMatch(w.Text)
            && w.Bounds.Width <= w.Bounds.Height * 3;
        if (!words.Any(IsKey)) return null;
        var runs = new List<List<OcrWord>> { new() };
        foreach (var word in words)
        {
            if (IsKey(word)) { if (runs[^1].Count > 0) runs.Add(new()); }
            else runs[^1].Add(word);
        }
        var captions = runs.Where(r => r.Count > 0).ToArray();
        // Long prose with an inline key is not a row of actions.
        if (captions.Length == 0 || captions.Any(r => r.Count > 4 || !char.IsUpper(r[0].Text[0]))
            || (words.Count(IsKey) == 1 && !IsKey(words[0]))
            || words.Any(w => w.Text.EndsWith('.') || w.Text.EndsWith(',') || w.Text.EndsWith(';')))
            return null;
        var blocks = captions.Select(run =>
        {
            var x = run.Min(w => w.Bounds.X); var y = run.Min(w => w.Bounds.Y);
            return new OcrTextBlock(string.Join(' ', run.Select(w => w.Text)),
                new BoundingBox(x, y, run.Max(w => w.Bounds.Right) - x, run.Max(w => w.Bounds.Bottom) - y));
        }).ToArray();
        return new OcrResult(raw.Request, blocks, raw.RecognizedAt,
            blocks.Select(b => new OcrTextBlockSource(b.Bounds, new[] { b.Bounds }, raw.Request.OrientationMode)), raw.Words);
    }

    [GeneratedRegex(@"^(?:\[[A-Za-z0-9+ ]{1,12}\]|[('‘]?[ABXY8][)\]]|Esc|Enter|Shift|LT|RT|LB|RB)$", RegexOptions.CultureInvariant)]
    private static partial Regex Key();
}
