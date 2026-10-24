using GameTranslator.Application.Capture;

namespace GameTranslator.Application.Ocr;

internal static class OcrFrameBorder
{
    internal const int Padding = 10;

    internal static OcrRequest Add(OcrRequest request, CancellationToken token)
    {
        var frame = request.Frame;
        var width = checked(frame.Width + Padding * 2);
        var height = checked(frame.Height + Padding * 2);
        var pixels = new byte[checked(width * height * 4)];
        Array.Fill(pixels, (byte)255);
        for (var y = 0; y < frame.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            frame.PixelData.Span.Slice(y * frame.Stride, frame.Width * 4)
                .CopyTo(pixels.AsSpan(((y + Padding) * width + Padding) * 4));
        }
        var padded = new CapturedFrame(frame.Region, width, height, width * 4, frame.PixelFormat, pixels, frame.CapturedAt, frame.Origin, frame.IsCachedReuse);
        return new OcrRequest(padded, request.Language, request.ZoneId, request.PreprocessingSettings,
            request.EngineId, request.OrientationMode, request.LayoutMode, request.ContentLayoutMode, request.CandidateGroupingSettings)
        {
            DetectorPreset = request.DetectorPreset,
            WatermarkFilter = request.WatermarkFilter,
            HasMenuValueRowEvidence = request.HasMenuValueRowEvidence,
            DetectorLineBounds = request.DetectorLineBounds.Select(b => new BoundingBox(b.X + Padding, b.Y + Padding, b.Width, b.Height)).ToArray(),
        };
    }

    internal static OcrResult Remove(OcrResult result, OcrRequest original)
    {
        BoundingBox? Map(BoundingBox b)
        {
            var left = Math.Max(0, b.X - Padding);
            var top = Math.Max(0, b.Y - Padding);
            var right = Math.Min(original.Frame.Width, b.Right - Padding);
            var bottom = Math.Min(original.Frame.Height, b.Bottom - Padding);
            return right > left && bottom > top ? new BoundingBox(left, top, right - left, bottom - top) : null;
        }
        var blocks = new List<OcrTextBlock>();
        var sources = new List<OcrTextBlockSource>();
        for (var index = 0; index < result.TextBlocks.Count; index++)
        {
            var block = result.TextBlocks[index];
            if (Map(block.Bounds) is not { } bounds) continue;
            blocks.Add(new OcrTextBlock(block.Text, bounds) { TranslationSourceLanguage = block.TranslationSourceLanguage });
            var source = result.TextBlockSources[index];
            var members = source.MemberBounds.Select(Map).OfType<BoundingBox>().ToArray();
            sources.Add(new OcrTextBlockSource(Map(source.SemanticBounds) ?? bounds,
                members.Length > 0 ? members : new[] { bounds }, source.OrientationMode)
            { CandidateRegionBounds = source.CandidateRegionBounds is { } region ? Map(region) : null });
        }
        var words = result.Words.Where(word => Map(word.Bounds) is not null)
            .Select(word => new OcrWord(word.Text, Map(word.Bounds)!.Value, word.Confidence, word.RecognitionPassId));
        return new OcrResult(original, blocks, result.RecognizedAt, sources, words)
        {
            LineRecognition = result.LineRecognition,
            WatermarkDecision = result.WatermarkDecision,
            WatermarkCandidates = result.WatermarkCandidates.Where(candidate => Map(candidate.Bounds) is not null)
                .Select(candidate => candidate with { Bounds = Map(candidate.Bounds)!.Value }).ToArray(),
        };
    }
}
