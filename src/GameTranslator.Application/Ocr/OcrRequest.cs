using GameTranslator.Application.Capture;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Ocr;

/// <summary>
/// Describes one OCR request for a captured frame and its profile zone context.
/// </summary>
public sealed class OcrRequest
{
    public OcrRequest(
        CapturedFrame frame,
        string language,
        string? zoneId = null,
        OcrPreprocessingSettings? preprocessingSettings = null)
    {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        if (zoneId is not null && string.IsNullOrWhiteSpace(zoneId))
        {
            throw new ArgumentException("Zone id must not be empty when provided.", nameof(zoneId));
        }

        Language = language;
        ZoneId = zoneId;
        PreprocessingSettings = preprocessingSettings ?? OcrPreprocessingSettings.Default;
    }

    public CapturedFrame Frame { get; }

    public CaptureRegion Region => Frame.Region;

    public string Language { get; }

    public string? ZoneId { get; }

    public OcrPreprocessingSettings PreprocessingSettings { get; }
}