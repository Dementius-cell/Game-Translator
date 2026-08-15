namespace GameTranslator.Application.Ocr;

/// <summary>
/// Reports a controlled degraded result when the default candidate runtime is unavailable.
/// </summary>
public sealed class UnavailableTextCandidateDetector : ITextCandidateDetector
{
    public const string DetectorId = "Unavailable";

    public Task<TextCandidateDetectionResult> DetectAsync(
        TextCandidateDetectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(TextCandidateDetectionResult.Unavailable(
            DetectorId,
            "No candidate detector runtime is registered."));
    }
}
