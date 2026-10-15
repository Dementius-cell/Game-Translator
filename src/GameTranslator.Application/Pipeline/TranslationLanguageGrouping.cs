using System.Text;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Pipeline;

public static partial class TranslationTextGroupingService
{
    private enum ScriptFamily { Neutral, Latin, Han, Japanese, Korean, Mixed, Other }

    private static ScriptFamily GetScriptFamily(string text)
    {
        bool latin = false, han = false, kana = false, hangul = false, other = false;
        foreach (var rune in text.EnumerateRunes())
        {
            var c = rune.Value;
            if (c is >= 0x3040 and <= 0x30ff or >= 0xff66 and <= 0xff9f) kana = true;
            else if (c is >= 0x1100 and <= 0x11ff or >= 0x3130 and <= 0x318f or >= 0xac00 and <= 0xd7af) hangul = true;
            else if (c is >= 0x3400 and <= 0x9fff or >= 0xf900 and <= 0xfaff or >= 0x20000 and <= 0x323af) han = true;
            else if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') latin = true;
            else if (Rune.IsLetter(rune)) other = true;
        }
        if (other) return ScriptFamily.Other;
        if (kana && !hangul) return ScriptFamily.Japanese; // Preserve embedded names/HP in a Japanese sentence.
        if (hangul && !kana && !latin) return ScriptFamily.Korean;
        if (kana || hangul || (han && latin)) return ScriptFamily.Mixed;
        return han ? ScriptFamily.Han : latin ? ScriptFamily.Latin : ScriptFamily.Neutral;
    }

    private static OcrResult CreateLanguageAwareResult(OcrResult original, OcrZone zone)
    {
        var expandedBlocks = new List<OcrTextBlock>();
        var expandedSources = new List<OcrTextBlockSource>();
        foreach (var block in original.TextBlocks)
        {
            var pieces = SplitVerifiedWords(original, block);
            foreach (var piece in pieces)
            {
                expandedBlocks.Add(piece);
                expandedSources.Add(ReferenceEquals(piece, block) ? GetSourceForBlock(original, block)
                    : new OcrTextBlockSource(piece.Bounds, new[] { piece.Bounds }, GetSourceForBlock(original, block).OrientationMode)
                    { CandidateRegionBounds = GetSourceForBlock(original, block).CandidateRegionBounds });
            }
        }
        var source = new OcrResult(original.Request, expandedBlocks, original.RecognizedAt, expandedSources, original.Words)
        { LineRecognition = original.LineRecognition };
        var ordered = OrderBlocksByReadingPosition(source, source.TextBlocks)
            .GroupBy(block => GetSourceForBlock(source, block).CandidateRegionBounds)
            .SelectMany(region => region).ToArray();
        var groups = new List<List<OcrTextBlock>>();
        var groupScripts = new List<ScriptFamily>();
        foreach (var block in ordered)
        {
            var script = GetScriptFamily(block.Text);
            var previous = groups.Count > 0 ? groupScripts[^1] : ScriptFamily.Other;
            var compatible = script == previous && script is not (ScriptFamily.Mixed or ScriptFamily.Other or ScriptFamily.Neutral);
            var close = groups.Count > 0 && CalculateDistance(groups[^1][^1].Bounds, block.Bounds)
                <= CalculateAdaptiveMergeThreshold(new[] { groups[^1][^1], block });
            var sameRegion = groups.Count > 0 && GetSourceForBlock(source, groups[^1][^1]).CandidateRegionBounds
                == GetSourceForBlock(source, block).CandidateRegionBounds;
            var continuation = groups.Count > 0 && IsGeometricContinuation(source, groups[^1][^1], block);
            if (zone.TranslationGroupingMode != TranslationGroupingMode.BlockByBlock && sameRegion && close
                && (compatible || continuation))
            {
                groups[^1].Add(block);
            }
            else
            {
                groups.Add(new List<OcrTextBlock> { block });
                groupScripts.Add(script);
            }
        }
        var output = new List<OcrTextBlock>();
        var sources = new List<OcrTextBlockSource>();
        foreach (var group in groups)
        {
            var merged = CreateTextBlockFromGroup(source, group);
            if (merged is null) continue;
            var text = merged.TextBlock.Text;
            var script = GetScriptFamily(text);
            var distinctiveLetters = text.EnumerateRunes().Count(r => script == ScriptFamily.Japanese
                ? r.Value is >= 0x3040 and <= 0x30ff : r.Value is >= 0xac00 and <= 0xd7af);
            // Script is not language: Han and Latin remain auto, including names and OCR noise.
            var latinLetters = text.Count(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
            var consistentScript = group.Select(block => GetScriptFamily(block.Text))
                .Where(family => family != ScriptFamily.Neutral).Distinct().Count() <= 1;
            var language = consistentScript && distinctiveLetters >= 2 && distinctiveLetters >= latinLetters ? script switch
            { ScriptFamily.Japanese => "ja", ScriptFamily.Korean => "ko", _ => "auto" } : "auto";
            output.Add(new OcrTextBlock(text, merged.TextBlock.Bounds) { TranslationSourceLanguage = language });
            sources.Add(merged.Source);
        }
        return new OcrResult(original.Request, output, original.RecognizedAt, sources, original.Words)
        { LineRecognition = original.LineRecognition };
    }

    // A change of script may be an OCR error or an embedded name. Compact aligned
    // lines keep their visual context; independent side-by-side regions do not.
    private static bool IsGeometricContinuation(OcrResult source, OcrTextBlock first, OcrTextBlock second)
    {
        var vertical = GetSourceForBlock(source, first).OrientationMode == OcrOrientationMode.Vertical
            || source.Request.OrientationMode == OcrOrientationMode.Vertical;
        var a = first.Bounds;
        var b = second.Bounds;
        var overlap = vertical ? Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y)
            : Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        var span = vertical ? Math.Min(a.Height, b.Height) : Math.Min(a.Width, b.Width);
        var gap = vertical ? Math.Max(0, Math.Max(a.X - b.Right, b.X - a.Right))
            : Math.Max(0, Math.Max(a.Y - b.Bottom, b.Y - a.Bottom));
        var thickness = vertical ? Math.Min(a.Width, b.Width) : Math.Min(a.Height, b.Height);
        return overlap >= span * 0.5 && gap <= Math.Max(2, thickness * 0.6);
    }

    private static IReadOnlyList<OcrTextBlock> SplitVerifiedWords(OcrResult source, OcrTextBlock block)
    {
        if (GetScriptFamily(block.Text) != ScriptFamily.Mixed) return new[] { block };
        var words = source.Words.Where(w => w.Bounds.X >= block.Bounds.X && w.Bounds.Y >= block.Bounds.Y
            && w.Bounds.Right <= block.Bounds.Right && w.Bounds.Bottom <= block.Bounds.Bottom).ToArray();
        if (words.Length < 2 || words.Any(w => w.Confidence is null or < 50)
            || words.Select(w => w.RecognitionPassId).Distinct().Count() != 1) return new[] { block };
        var vertical = GetSourceForBlock(source, block).OrientationMode == OcrOrientationMode.Vertical;
        words = (vertical ? words.OrderBy(w => w.Bounds.Y) : words.OrderBy(w => w.Bounds.X)).ToArray();
        static string Compact(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
        if (Compact(string.Concat(words.Select(w => w.Text))) != Compact(block.Text)) return new[] { block };
        var runs = new List<List<OcrWord>> { new() { words[0] } };
        for (var i = 1; i < words.Length; i++)
        {
            var previous = words[i - 1];
            var current = words[i];
            var a = GetScriptFamily(previous.Text);
            var b = GetScriptFamily(current.Text);
            var gap = vertical ? current.Bounds.Y - previous.Bounds.Bottom : current.Bounds.X - previous.Bounds.Right;
            var size = vertical ? Math.Min(current.Bounds.Width, previous.Bounds.Width) : Math.Min(current.Bounds.Height, previous.Bounds.Height);
            if (gap < 0) return new[] { block }; // Ambiguous/overlapping word geometry: keep original.
            var boundary = a != b && a is ScriptFamily.Latin or ScriptFamily.Han or ScriptFamily.Korean
                && b is ScriptFamily.Latin or ScriptFamily.Han or ScriptFamily.Korean;
            if (boundary && gap >= Math.Max(2, size * 0.6)) runs.Add(new List<OcrWord>());
            runs[^1].Add(current);
        }
        if (runs.Count < 2 || runs.Any(run => GetScriptFamily(string.Join(' ', run.Select(w => w.Text))) is ScriptFamily.Mixed
            or ScriptFamily.Neutral or ScriptFamily.Other
            || run.Sum(w => w.Text.Count(char.IsLetter)) < (GetScriptFamily(run[0].Text) == ScriptFamily.Latin ? 4 : 2))) return new[] { block };
        return runs.Select(run => new OcrTextBlock(string.Join(' ', run.Select(w => w.Text)),
            CreateCombinedBounds(run.Select(w => w.Bounds).ToArray()))).ToArray();
    }
}
