namespace GameTranslator.Application.Ocr;

/// <summary>
/// Scores comic OCR semantic source geometry against owner/reference bounds.
/// </summary>
public sealed class ComicGeometryQualityGate
{
    private readonly ComicGeometryQualityGateOptions options;

    public ComicGeometryQualityGate()
        : this(ComicGeometryQualityGateOptions.Default)
    {
    }

    public ComicGeometryQualityGate(ComicGeometryQualityGateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        this.options = options;
    }

    public ComicGeometryQualityGateResult Evaluate(
        OcrResult result,
        IEnumerable<BoundingBox> expectedSemanticBounds)
    {
        ArgumentNullException.ThrowIfNull(result);

        return Evaluate(
            result.TextBlockSources.Select(source => source.SemanticBounds),
            expectedSemanticBounds);
    }

    /// <summary>
    /// Scores detector-proposed semantic geometry against owner/reference bounds.
    /// </summary>
    public ComicGeometryQualityGateResult Evaluate(
        IEnumerable<BoundingBox> detectedSemanticBounds,
        IEnumerable<BoundingBox> expectedSemanticBounds)
    {
        ArgumentNullException.ThrowIfNull(detectedSemanticBounds);
        ArgumentNullException.ThrowIfNull(expectedSemanticBounds);

        var expected = expectedSemanticBounds.ToArray();
        if (expected.Length == 0)
        {
            throw new ArgumentException("Comic geometry reference must include at least one expected source bound.", nameof(expectedSemanticBounds));
        }

        var detected = detectedSemanticBounds.ToArray();
        var candidates = CreateMatchCandidates(expected, detected)
            .Where(candidate => IsAcceptableMatch(candidate))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.ExpectedIndex)
            .ThenBy(candidate => candidate.DetectedIndex)
            .ToArray();
        var usedExpectedIndexes = new HashSet<int>();
        var usedDetectedIndexes = new HashSet<int>();
        var acceptedMatches = new List<ComicGeometryQualityMatch>();

        foreach (var candidate in candidates)
        {
            if (!usedExpectedIndexes.Add(candidate.ExpectedIndex)
                || !usedDetectedIndexes.Add(candidate.DetectedIndex))
            {
                continue;
            }

            acceptedMatches.Add(candidate.ToMatch(isMatched: true));
        }

        var allMatches = expected
            .Select((bounds, index) => acceptedMatches.SingleOrDefault(match => match.ExpectedIndex == index)
                ?? ComicGeometryQualityMatch.CreateMiss(index, bounds))
            .OrderBy(match => match.ExpectedIndex)
            .ToArray();
        var extraDetections = detected
            .Select((bounds, index) => (bounds, index))
            .Where(item => !usedDetectedIndexes.Contains(item.index))
            .Select(item => new ComicGeometryExtraDetection(item.index, item.bounds))
            .ToArray();
        var readingOrderViolationCount = CountReadingOrderViolations(allMatches);
        var requiredMatchCount = (int)Math.Ceiling(expected.Length * options.MinimumReferenceRecall);
        var passed = acceptedMatches.Count >= requiredMatchCount
            && extraDetections.Length <= options.MaximumExtraDetections
            && (!options.RequireReadingOrder || readingOrderViolationCount == 0);

        return new ComicGeometryQualityGateResult(
            passed,
            expected.Length,
            detected.Length,
            acceptedMatches.Count,
            expected.Length - acceptedMatches.Count,
            extraDetections.Length,
            readingOrderViolationCount,
            allMatches,
            extraDetections);
    }

    private static int CountReadingOrderViolations(IReadOnlyList<ComicGeometryQualityMatch> matches)
    {
        var matched = matches
            .Where(match => match.IsMatched)
            .OrderBy(match => match.ExpectedIndex)
            .ToArray();
        var violations = 0;
        var maxDetectedIndex = -1;

        foreach (var match in matched)
        {
            var detectedIndex = match.DetectedIndex!.Value;
            if (detectedIndex < maxDetectedIndex)
            {
                violations++;
            }

            maxDetectedIndex = Math.Max(maxDetectedIndex, detectedIndex);
        }

        return violations;
    }

    private static IReadOnlyList<ComicGeometryMatchCandidate> CreateMatchCandidates(
        IReadOnlyList<BoundingBox> expected,
        IReadOnlyList<BoundingBox> detected)
    {
        var candidates = new List<ComicGeometryMatchCandidate>(expected.Count * detected.Count);

        for (var expectedIndex = 0; expectedIndex < expected.Count; expectedIndex++)
        {
            for (var detectedIndex = 0; detectedIndex < detected.Count; detectedIndex++)
            {
                var intersectionArea = CalculateIntersectionArea(expected[expectedIndex], detected[detectedIndex]);
                var expectedArea = CalculateArea(expected[expectedIndex]);
                var detectedArea = CalculateArea(detected[detectedIndex]);
                var unionArea = expectedArea + detectedArea - intersectionArea;
                var intersectionOverUnion = unionArea <= 0 ? 0 : intersectionArea / (double)unionArea;
                var expectedCoverage = expectedArea <= 0 ? 0 : intersectionArea / (double)expectedArea;
                var detectedCoverage = detectedArea <= 0 ? 0 : intersectionArea / (double)detectedArea;
                var centerDistance = CalculateCenterDistance(expected[expectedIndex], detected[detectedIndex]);
                var score = intersectionOverUnion + expectedCoverage + detectedCoverage;

                candidates.Add(new ComicGeometryMatchCandidate(
                    expectedIndex,
                    detectedIndex,
                    expected[expectedIndex],
                    detected[detectedIndex],
                    intersectionOverUnion,
                    expectedCoverage,
                    detectedCoverage,
                    centerDistance,
                    score));
            }
        }

        return candidates;
    }

    private bool IsAcceptableMatch(ComicGeometryMatchCandidate candidate)
    {
        return candidate.IntersectionOverUnion >= options.MinimumIntersectionOverUnion
            || (candidate.ExpectedCoverage >= options.MinimumExpectedCoverage
                && candidate.DetectedCoverage >= options.MinimumDetectedCoverage);
    }

    private static int CalculateIntersectionArea(BoundingBox first, BoundingBox second)
    {
        var width = Math.Min(first.Right, second.Right) - Math.Max(first.X, second.X);
        var height = Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Y, second.Y);

        return width <= 0 || height <= 0
            ? 0
            : checked(width * height);
    }

    private static int CalculateArea(BoundingBox bounds)
    {
        return checked(bounds.Width * bounds.Height);
    }

    private static double CalculateCenterDistance(BoundingBox first, BoundingBox second)
    {
        var deltaX = first.X + first.Width / 2d - (second.X + second.Width / 2d);
        var deltaY = first.Y + first.Height / 2d - (second.Y + second.Height / 2d);

        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static void ValidateOptions(ComicGeometryQualityGateOptions options)
    {
        ValidateRatio(options.MinimumIntersectionOverUnion, nameof(options.MinimumIntersectionOverUnion));
        ValidateRatio(options.MinimumExpectedCoverage, nameof(options.MinimumExpectedCoverage));
        ValidateRatio(options.MinimumDetectedCoverage, nameof(options.MinimumDetectedCoverage));
        ValidateRatio(options.MinimumReferenceRecall, nameof(options.MinimumReferenceRecall));

        if (options.MaximumExtraDetections < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaximumExtraDetections),
                "Maximum extra comic detections must not be negative.");
        }
    }

    private static void ValidateRatio(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Comic geometry quality ratios must be between 0 and 1.");
        }
    }

    private sealed record ComicGeometryMatchCandidate(
        int ExpectedIndex,
        int DetectedIndex,
        BoundingBox ExpectedBounds,
        BoundingBox DetectedBounds,
        double IntersectionOverUnion,
        double ExpectedCoverage,
        double DetectedCoverage,
        double CenterDistancePixels,
        double Score)
    {
        public ComicGeometryQualityMatch ToMatch(bool isMatched)
        {
            return new ComicGeometryQualityMatch(
                ExpectedIndex,
                ExpectedBounds,
                DetectedIndex,
                DetectedBounds,
                isMatched,
                IntersectionOverUnion,
                ExpectedCoverage,
                DetectedCoverage,
                CenterDistancePixels);
        }
    }
}

public sealed record ComicGeometryQualityGateOptions
{
    public static ComicGeometryQualityGateOptions Default { get; } = new();

    public double MinimumIntersectionOverUnion { get; init; } = 0.45;

    public double MinimumExpectedCoverage { get; init; } = 0.65;

    public double MinimumDetectedCoverage { get; init; } = 0.65;

    public double MinimumReferenceRecall { get; init; } = 0.95;

    public int MaximumExtraDetections { get; init; }

    public bool RequireReadingOrder { get; init; } = true;
}

public sealed class ComicGeometryQualityGateResult
{
    public ComicGeometryQualityGateResult(
        bool passed,
        int expectedCount,
        int detectedCount,
        int matchedCount,
        int missedCount,
        int extraDetectionCount,
        int readingOrderViolationCount,
        IEnumerable<ComicGeometryQualityMatch> matches,
        IEnumerable<ComicGeometryExtraDetection> extraDetections)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(extraDetections);

        if (expectedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedCount), "Expected comic geometry count must not be negative.");
        }

        if (detectedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(detectedCount), "Detected comic geometry count must not be negative.");
        }

        if (matchedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(matchedCount), "Matched comic geometry count must not be negative.");
        }

        if (missedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(missedCount), "Missed comic geometry count must not be negative.");
        }

        if (extraDetectionCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(extraDetectionCount), "Extra comic geometry count must not be negative.");
        }

        if (readingOrderViolationCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(readingOrderViolationCount), "Reading-order violation count must not be negative.");
        }

        Passed = passed;
        ExpectedCount = expectedCount;
        DetectedCount = detectedCount;
        MatchedCount = matchedCount;
        MissedCount = missedCount;
        ExtraDetectionCount = extraDetectionCount;
        ReadingOrderViolationCount = readingOrderViolationCount;
        Matches = matches.ToArray();
        ExtraDetections = extraDetections.ToArray();
    }

    public bool Passed { get; }

    public int ExpectedCount { get; }

    public int DetectedCount { get; }

    public int MatchedCount { get; }

    public int MissedCount { get; }

    public int ExtraDetectionCount { get; }

    public int ReadingOrderViolationCount { get; }

    public IReadOnlyList<ComicGeometryQualityMatch> Matches { get; }

    public IReadOnlyList<ComicGeometryExtraDetection> ExtraDetections { get; }
}

public sealed record ComicGeometryQualityMatch(
    int ExpectedIndex,
    BoundingBox ExpectedBounds,
    int? DetectedIndex,
    BoundingBox? DetectedBounds,
    bool IsMatched,
    double IntersectionOverUnion,
    double ExpectedCoverage,
    double DetectedCoverage,
    double CenterDistancePixels)
{
    public static ComicGeometryQualityMatch CreateMiss(int expectedIndex, BoundingBox expectedBounds)
    {
        return new ComicGeometryQualityMatch(
            expectedIndex,
            expectedBounds,
            DetectedIndex: null,
            DetectedBounds: null,
            IsMatched: false,
            IntersectionOverUnion: 0,
            ExpectedCoverage: 0,
            DetectedCoverage: 0,
            CenterDistancePixels: double.NaN);
    }
}

public sealed record ComicGeometryExtraDetection(int DetectedIndex, BoundingBox Bounds);
