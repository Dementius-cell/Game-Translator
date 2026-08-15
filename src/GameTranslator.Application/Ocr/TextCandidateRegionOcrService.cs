using System.Runtime.CompilerServices;
using GameTranslator.Application.Capture;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Ocr;

/// <summary>
/// Recognizes transient detector candidates with Tesseract without changing the public OCR engine contract.
/// </summary>
public sealed class TextCandidateRegionOcrService
{
    private const string SupportedPixelFormat = "Bgra32";
    private const int BytesPerPixel = 4;
    private readonly ITextCandidateDetector candidateDetector;
    private readonly OcrService ocrService;
    private readonly TextCandidateRegionOcrOptions options;
    private readonly BoundedTextCandidateGroupingService groupingService;

    public TextCandidateRegionOcrService(
        ITextCandidateDetector candidateDetector,
        OcrService ocrService,
        TextCandidateRegionOcrOptions? options = null,
        BoundedTextCandidateGroupingService? groupingService = null)
    {
        this.candidateDetector = candidateDetector ?? throw new ArgumentNullException(nameof(candidateDetector));
        this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        this.options = options ?? TextCandidateRegionOcrOptions.Default;
        this.groupingService = groupingService ?? new BoundedTextCandidateGroupingService();
        ValidateOptions(this.options);
    }

    public async IAsyncEnumerable<TextCandidateRegionOcrResult> RecognizeAsync(
        OcrRequest zoneRequest,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zoneRequest);
        cancellationToken.ThrowIfCancellationRequested();

        var detection = await DetectAsync(zoneRequest, cancellationToken);
        if (detection.Availability != TextCandidateDetectorAvailability.Available)
        {
            yield break;
        }

        var pending = detection.Regions
            .Select(region => new PendingCandidateRecognition(
                region,
                RecognizeCandidateAsync(zoneRequest, region, cancellationToken)))
            .ToList();

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completedTask = await Task.WhenAny(pending.Select(item => item.Task));
            var completedIndex = pending.FindIndex(item => ReferenceEquals(item.Task, completedTask));
            if (completedIndex < 0)
            {
                throw new InvalidOperationException("Completed candidate OCR task was not tracked.");
            }

            pending.RemoveAt(completedIndex);
            var result = await completedTask;
            if (result is not null)
            {
                yield return result;
            }
        }
    }

    /// <summary>
    /// Detects and bounds transient text candidates inside one manually saved capture zone.
    /// </summary>
    public async Task<TextCandidateRegionDetectionResult> DetectAsync(
        OcrRequest zoneRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zoneRequest);
        cancellationToken.ThrowIfCancellationRequested();

        var detection = await candidateDetector.DetectAsync(
            new TextCandidateDetectionRequest(
                zoneRequest.Frame,
                zoneRequest.Language,
                zoneRequest.OrientationMode,
                zoneRequest.LayoutMode),
            cancellationToken);
        if (detection.Availability != TextCandidateDetectorAvailability.Available)
        {
            return new TextCandidateRegionDetectionResult(
                detection.Availability,
                detection.DetectorId,
                detection.UnavailableReason,
                Array.Empty<TextCandidateRegion>());
        }

        var candidates = FilterCandidates(detection.Candidates, zoneRequest.Frame);
        var regions = SelectCandidates(
                groupingService.Group(candidates, zoneRequest.Frame.Height),
                zoneRequest.Frame)
            .Select(candidate => new TextCandidateRegion(candidate, CreateCroppedFrame(zoneRequest.Frame, candidate.Bounds)))
            .ToArray();
        return new TextCandidateRegionDetectionResult(
            detection.Availability,
            detection.DetectorId,
            detection.UnavailableReason,
            regions);
    }

    private async Task<TextCandidateRegionOcrResult?> RecognizeCandidateAsync(
        OcrRequest zoneRequest,
        TextCandidateRegion region,
        CancellationToken cancellationToken)
    {
        var candidateRequest = region.CreateOcrRequest(zoneRequest);
        var candidateResult = await ocrService.RecognizeAsync(candidateRequest, cancellationToken);
        var recognizedText = string.Join(
            Environment.NewLine,
            candidateResult.TextBlocks
                .Select(block => block.Text.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        if (string.IsNullOrWhiteSpace(recognizedText))
        {
            return null;
        }

        if (ShouldApplyCjkTargetPostFilter(zoneRequest.Language)
            && !IsCjkTargetCandidate(region.Candidate, zoneRequest.Frame.Height, recognizedText))
        {
            return null;
        }

        return new TextCandidateRegionOcrResult(
            region.Candidate,
            recognizedText,
            candidateResult.RecognizedAt,
            zoneRequest.OrientationMode);
    }

    private IReadOnlyList<TextCandidate> SelectCandidates(
        IEnumerable<TextCandidate> candidates,
        CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(frame);

        var selected = new List<TextCandidate>();
        foreach (var candidate in FilterCandidates(candidates, frame))
        {
            if (selected.Any(existing => Intersects(existing.Bounds, candidate.Bounds)))
            {
                continue;
            }

            selected.Add(candidate);
        }

        return selected
            .OrderBy(candidate => candidate.Bounds.Y)
            .ThenBy(candidate => candidate.Bounds.X)
            .ThenBy(candidate => candidate.Bounds.Width)
            .ThenBy(candidate => candidate.Bounds.Height)
            .ToArray();
    }

    private IReadOnlyList<TextCandidate> FilterCandidates(
        IEnumerable<TextCandidate> candidates,
        CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(frame);

        var selected = new List<TextCandidate>();
        foreach (var candidate in candidates
                     .Where(candidate => candidate.HasValidConfidence)
                     .Where(candidate => candidate.Confidence >= options.MinimumCandidateConfidence)
                     .Where(candidate => candidate.Bounds.IsWithin(frame.Width, frame.Height))
                     .OrderByDescending(candidate => candidate.Confidence)
                     .ThenBy(candidate => candidate.Bounds.Y)
                     .ThenBy(candidate => candidate.Bounds.X)
                     .ThenBy(candidate => candidate.Bounds.Width)
                     .ThenBy(candidate => candidate.Bounds.Height))
        {
            if (selected.Any(existing => HasSubstantialOverlap(existing.Bounds, candidate.Bounds)))
            {
                continue;
            }

            selected.Add(candidate);
        }

        return selected;
    }

    private static CapturedFrame CreateCroppedFrame(CapturedFrame frame, BoundingBox bounds)
    {
        if (!string.Equals(frame.PixelFormat, SupportedPixelFormat, StringComparison.OrdinalIgnoreCase))
        {
            throw new OcrEngineException(
                $"Tesseract candidate OCR requires {SupportedPixelFormat} captured frames, but received '{frame.PixelFormat}'.");
        }

        var targetStride = checked(bounds.Width * BytesPerPixel);
        var pixels = new byte[checked(targetStride * bounds.Height)];
        var source = frame.PixelData.Span;
        for (var row = 0; row < bounds.Height; row++)
        {
            var sourceOffset = checked((bounds.Y + row) * frame.Stride + bounds.X * BytesPerPixel);
            var targetOffset = checked(row * targetStride);
            source.Slice(sourceOffset, targetStride).CopyTo(pixels.AsSpan(targetOffset, targetStride));
        }

        return new CapturedFrame(
            new CaptureRegion(
                checked(frame.Region.X + bounds.X),
                checked(frame.Region.Y + bounds.Y),
                bounds.Width,
                bounds.Height),
            bounds.Width,
            bounds.Height,
            targetStride,
            frame.PixelFormat,
            pixels,
            frame.CapturedAt);
    }

    private static bool Intersects(BoundingBox left, BoundingBox right)
    {
        return left.X < right.Right
            && right.X < left.Right
            && left.Y < right.Bottom
            && right.Y < left.Bottom;
    }

    private static bool HasSubstantialOverlap(BoundingBox left, BoundingBox right)
    {
        var intersectionWidth = Math.Min(left.Right, right.Right) - Math.Max(left.X, right.X);
        var intersectionHeight = Math.Min(left.Bottom, right.Bottom) - Math.Max(left.Y, right.Y);
        if (intersectionWidth <= 0 || intersectionHeight <= 0)
        {
            return false;
        }

        var intersectionArea = checked((long)intersectionWidth * intersectionHeight);
        var smallerArea = Math.Min(
            checked((long)left.Width * left.Height),
            checked((long)right.Width * right.Height));
        return intersectionArea * 2 >= smallerArea;
    }

    private static void ValidateOptions(TextCandidateRegionOcrOptions options)
    {
        if (!double.IsFinite(options.MinimumCandidateConfidence)
            || options.MinimumCandidateConfidence is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Minimum candidate confidence must be between zero and one.");
        }

        if (!double.IsFinite(options.CjkTargetMinimumCandidateHeightZoneFraction)
            || options.CjkTargetMinimumCandidateHeightZoneFraction is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "CJK target minimum candidate height must be between zero and one.");
        }

        if (!double.IsFinite(options.CjkTargetMinimumHeightToWidthRatio)
            || options.CjkTargetMinimumHeightToWidthRatio < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "CJK target minimum height-to-width ratio must be finite and non-negative.");
        }

        if (options.CjkTargetMinimumCharacterCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "CJK target minimum character count must not be negative.");
        }
    }

    private bool ShouldApplyCjkTargetPostFilter(string language)
    {
        return options.EnableCjkTargetPostFilter
            && (language.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
                || language.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
    }

    private bool IsCjkTargetCandidate(
        TextCandidate candidate,
        int zoneHeight,
        string recognizedText)
    {
        var minimumHeight = (int)Math.Ceiling(
            zoneHeight * options.CjkTargetMinimumCandidateHeightZoneFraction);
        return candidate.Bounds.Height >= minimumHeight
            && candidate.Bounds.Height >= candidate.Bounds.Width * options.CjkTargetMinimumHeightToWidthRatio
            && CountCjkFamilyCharacters(recognizedText) >= options.CjkTargetMinimumCharacterCount;
    }

    private static int CountCjkFamilyCharacters(string text)
    {
        return text.Count(character => character is >= '\u3040' and <= '\u30ff'
            or >= '\u3400' and <= '\u4dbf'
            or >= '\u4e00' and <= '\u9fff'
            or >= '\uf900' and <= '\ufaff'
            or >= '\uff66' and <= '\uff9d');
    }

    private sealed record PendingCandidateRecognition(
        TextCandidateRegion Region,
        Task<TextCandidateRegionOcrResult?> Task);
}

/// <summary>
/// A detector candidate with its bounded, in-memory crop. It is never persisted as a profile zone.
/// </summary>
public sealed class TextCandidateRegion
{
    public TextCandidateRegion(TextCandidate candidate, CapturedFrame frame)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    public TextCandidate Candidate { get; }

    public CapturedFrame Frame { get; }

    public OcrRequest CreateOcrRequest(OcrRequest zoneRequest)
    {
        ArgumentNullException.ThrowIfNull(zoneRequest);
        return new OcrRequest(
            Frame,
            zoneRequest.Language,
            zoneRequest.ZoneId,
            zoneRequest.PreprocessingSettings,
            OcrSettings.TesseractEngineId,
            zoneRequest.OrientationMode,
            OcrLayoutMode.Dialog);
    }
}

public sealed class TextCandidateRegionDetectionResult
{
    public TextCandidateRegionDetectionResult(
        TextCandidateDetectorAvailability availability,
        string detectorId,
        string? unavailableReason,
        IReadOnlyList<TextCandidateRegion> regions)
    {
        if (!Enum.IsDefined(availability))
        {
            throw new ArgumentOutOfRangeException(nameof(availability));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(detectorId);
        ArgumentNullException.ThrowIfNull(regions);

        Availability = availability;
        DetectorId = detectorId.Trim();
        UnavailableReason = unavailableReason;
        Regions = regions.ToArray();
    }

    public TextCandidateDetectorAvailability Availability { get; }

    public string DetectorId { get; }

    public string? UnavailableReason { get; }

    public IReadOnlyList<TextCandidateRegion> Regions { get; }
}

public sealed record TextCandidateRegionOcrOptions
{
    public static TextCandidateRegionOcrOptions Default { get; } = new();

    public double MinimumCandidateConfidence { get; init; } = 0.5d;

    /// <summary>
    /// Enables the research-validated CJK target filter for an explicitly configured pilot service.
    /// </summary>
    public bool EnableCjkTargetPostFilter { get; init; }

    public double CjkTargetMinimumCandidateHeightZoneFraction { get; init; } = 0.04d;

    public double CjkTargetMinimumHeightToWidthRatio { get; init; } = 1d;

    public int CjkTargetMinimumCharacterCount { get; init; } = 2;
}

public sealed class TextCandidateRegionOcrResult
{
    public TextCandidateRegionOcrResult(
        TextCandidate candidate,
        string recognizedText,
        DateTimeOffset recognizedAt,
        OcrOrientationMode orientationMode)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        ArgumentException.ThrowIfNullOrWhiteSpace(recognizedText);

        if (!Enum.IsDefined(orientationMode))
        {
            throw new ArgumentOutOfRangeException(nameof(orientationMode));
        }

        RecognizedText = recognizedText.Trim();
        RecognizedAt = recognizedAt;
        OrientationMode = orientationMode;
    }

    public TextCandidate Candidate { get; }

    public string RecognizedText { get; }

    public DateTimeOffset RecognizedAt { get; }

    public OcrOrientationMode OrientationMode { get; }

    public OcrTextBlock CreateSourceTextBlock()
    {
        return new OcrTextBlock(RecognizedText, Candidate.Bounds);
    }

    public OcrTextBlockSource CreateSourceGeometry()
    {
        return new OcrTextBlockSource(
            Candidate.Bounds,
            new[] { Candidate.Bounds },
            OrientationMode);
    }
}
