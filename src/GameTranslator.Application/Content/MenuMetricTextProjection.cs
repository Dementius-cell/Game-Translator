using System.Text.RegularExpressions;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Content;

/// <summary>Projects labels from a spatially separated row of repeated numeric metrics.</summary>
public static partial class MenuMetricTextProjection
{
    public static OcrResult? TryCreate(OcrResult raw)
    {
        if (raw.Request.DetectorLineBounds.Count != 1 || raw.TextBlocks.Count != 1
            || raw.Request.OrientationMode == OcrOrientationMode.Vertical) return null;
        var partialRow = raw.Request.HasMenuValueRowEvidence
            && raw.Request.ContentLayoutMode == ContentLayoutMode.StaticMenu;
        var words = raw.Words.OrderBy(w => w.Bounds.X).ToArray();
        if ((words.Length < (partialRow ? 2 : 4) || words.Length > 20) || words.Any(w => w.Confidence is null or < 60)
            || words.Select(w => w.RecognitionPassId).Distinct().Count() != 1) return null;
        static string Compact(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        if (Compact(raw.TextBlocks[0].Text) != Compact(string.Join(' ', words.Select(w => w.Text)))) return null;
        var heights = words.Select(w => w.Bounds.Height).Order().ToArray();
        var height = (heights[(heights.Length - 1) / 2] + heights[heights.Length / 2]) / 2.0;
        if (words.Max(w => w.Bounds.Y) - words.Min(w => w.Bounds.Y) > height * .35
            || heights[^1] > heights[0] * 1.5) return null;
        bool IsValue(OcrWord word) => Value().IsMatch(word.Text);
        if (words.Count(IsValue) < (partialRow ? 1 : 2)) return null;

        var captions = new List<List<OcrWord>>();
        var current = new List<OcrWord>();
        var wideGap = false;
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            if (i > 0)
            {
                var gap = word.Bounds.X - words[i - 1].Bounds.Right;
                if (gap < 0) return null;
                if (IsValue(word) || IsValue(words[i - 1]))
                {
                    if (IsValue(word) == IsValue(words[i - 1]) || gap < height * (partialRow ? .6 : .4)) return null;
                    wideGap |= gap >= height * .8;
                }
            }
            if (IsValue(word))
            {
                if (current.Count > 0) { captions.Add(current); current = new(); }
            }
            else
            {
                if (!LabelWord().IsMatch(word.Text)) return null;
                current.Add(word);
            }
        }
        if (current.Count > 0) captions.Add(current);
        if ((!partialRow && !wideGap) || captions.Count < (partialRow ? 1 : 2)
            || captions.Any(c => c.Count > 3 || !char.IsUpper(c[0].Text[0]))) return null;
        var blocks = captions.Select(c =>
        {
            var x = c.Min(w => w.Bounds.X); var y = c.Min(w => w.Bounds.Y);
            return new OcrTextBlock(string.Join(' ', c.Select(w => w.Text)),
                new BoundingBox(x, y, c.Max(w => w.Bounds.Right) - x, c.Max(w => w.Bounds.Bottom) - y));
        }).ToArray();
        return new OcrResult(raw.Request, blocks, raw.RecognizedAt,
            blocks.Select(b => new OcrTextBlockSource(b.Bounds, new[] { b.Bounds }, raw.Request.OrientationMode)), raw.Words);
    }

    [GeneratedRegex(@"^[+\-−]?\d+(?:[.,]\d+)?%?$", RegexOptions.CultureInvariant)]
    private static partial Regex Value();

    [GeneratedRegex(@"^\p{L}+(?:[-']\p{L}+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex LabelWord();
}
