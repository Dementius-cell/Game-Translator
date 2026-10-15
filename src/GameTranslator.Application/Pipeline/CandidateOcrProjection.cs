using GameTranslator.Application.Ocr;

namespace GameTranslator.Application.Pipeline;

internal static class CandidateOcrProjection
{
    public static OcrResult PreserveLines(OcrRequest request, IReadOnlyList<TextCandidateRegionOcrResult> candidates, DateTimeOffset at)
    {
        var blocks = new List<OcrTextBlock>();
        var sources = new List<OcrTextBlockSource>();
        var words = new List<OcrWord>();
        var watermarks = new List<WatermarkCandidateDecision>();
        foreach (var candidate in candidates)
        {
            if (candidate.SourceResult is not { } raw)
            {
                blocks.Add(candidate.CreateSourceTextBlock());
                var geometry = candidate.CreateSourceGeometry();
                sources.Add(new OcrTextBlockSource(geometry.SemanticBounds, geometry.MemberBounds, geometry.OrientationMode)
                { CandidateRegionBounds = candidate.Candidate.Bounds });
                if (candidate.WatermarkDecision is { } decision)
                    watermarks.Add(new WatermarkCandidateDecision(candidate.Candidate.Bounds, decision));
                continue;
            }
            var crop = candidate.Candidate.Bounds;
            BoundingBox Map(BoundingBox bounds)
            {
                var x = Math.Clamp((int)Math.Floor(bounds.X * (double)crop.Width / raw.InputWidth), 0, crop.Width - 1);
                var y = Math.Clamp((int)Math.Floor(bounds.Y * (double)crop.Height / raw.InputHeight), 0, crop.Height - 1);
                var right = Math.Clamp((int)Math.Ceiling(bounds.Right * (double)crop.Width / raw.InputWidth), x + 1, crop.Width);
                var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom * (double)crop.Height / raw.InputHeight), y + 1, crop.Height);
                return new BoundingBox(crop.X + x, crop.Y + y, right - x, bottom - y);
            }
            for (var i = 0; i < raw.TextBlocks.Count; i++)
            {
                var block = raw.TextBlocks[i];
                var bounds = Map(block.Bounds);
                blocks.Add(new OcrTextBlock(block.Text, bounds));
                sources.Add(new OcrTextBlockSource(Map(raw.TextBlockSources[i].SemanticBounds),
                    raw.TextBlockSources[i].MemberBounds.Select(Map), raw.TextBlockSources[i].OrientationMode)
                { CandidateRegionBounds = crop });
                if (candidate.WatermarkDecision is { } decision)
                    watermarks.Add(new WatermarkCandidateDecision(bounds, decision));
            }
            words.AddRange(raw.Words.Select(w => new OcrWord(w.Text, Map(w.Bounds), w.Confidence,
                $"{crop.X}:{crop.Y}:{w.RecognitionPassId}")));
        }
        return new OcrResult(request, blocks, at, sources, words) { WatermarkCandidates = watermarks };
    }
}
