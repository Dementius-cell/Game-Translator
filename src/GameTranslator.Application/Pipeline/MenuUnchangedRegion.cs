using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;

namespace GameTranslator.Application.Pipeline;

/// <summary>Exact, bounded comparison against the last accepted OCR input, never a rolling sample.</summary>
internal static class MenuUnchangedRegion
{
    internal static readonly TimeSpan AuditInterval = TimeSpan.FromSeconds(5);
    private const int MaximumPixels = 131072;

    internal static bool Matches(TextCandidateRegion accepted, TextCandidateRegion current)
    {
        if (accepted.Candidate.Bounds != current.Candidate.Bounds
            || accepted.Candidate.HasMenuValueRowEvidence != current.Candidate.HasMenuValueRowEvidence
            || !accepted.Candidate.SourceCandidateBounds.SequenceEqual(current.Candidate.SourceCandidateBounds)
            || !SamePixels(accepted.Frame, current.Frame)) return false;
        return accepted.RecognitionContextFrame is { } context
            ? current.RecognitionContextFrame is { } next && SamePixels(context, next)
            : current.RecognitionContextFrame is null;
    }

    private static bool SamePixels(CapturedFrame accepted, CapturedFrame current)
        => accepted.Origin is { } origin && current.Origin?.SourceId == origin.SourceId
            && current.Region == accepted.Region
            && current.Width == accepted.Width && current.Height == accepted.Height
            && current.Stride == accepted.Stride
            && current.PixelFormat == "Bgra32" && accepted.PixelFormat == "Bgra32"
            && (long)accepted.Width * accepted.Height <= MaximumPixels
            && accepted.Stride == (long)accepted.Width * 4
            && accepted.PixelData.Span.SequenceEqual(current.PixelData.Span);
}
