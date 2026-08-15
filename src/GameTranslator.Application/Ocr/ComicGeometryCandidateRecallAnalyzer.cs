namespace GameTranslator.Application.Ocr;

/// <summary>
/// Measures whether reliable OCR words support expected comic text geometry before grouping.
/// </summary>
public sealed class ComicGeometryCandidateRecallAnalyzer
{
    private readonly ComicGeometryCandidateRecallOptions options;

    public ComicGeometryCandidateRecallAnalyzer()
        : this(ComicGeometryCandidateRecallOptions.Default)
    {
    }

    public ComicGeometryCandidateRecallAnalyzer(ComicGeometryCandidateRecallOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        this.options = options;
    }

    public ComicGeometryCandidateRecallResult Evaluate(
        IEnumerable<OcrWord> words,
        IEnumerable<BoundingBox> expectedSemanticBounds)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(expectedSemanticBounds);

        var expected = expectedSemanticBounds.ToArray();
        if (expected.Length == 0)
        {
            throw new ArgumentException(
                "Comic candidate recall requires at least one expected semantic bound.",
                nameof(expectedSemanticBounds));
        }

        var allWords = words.ToArray();
        var reliableWords = allWords
            .Select((word, index) => new CandidateWord(index, word))
            .Where(candidate => IsReliable(candidate.Word))
            .ToArray();
        var supportedCandidateIndexes = new HashSet<int>();
        var references = expected
            .Select((bounds, expectedIndex) => CreateReference(
                expectedIndex,
                bounds,
                reliableWords,
                supportedCandidateIndexes))
            .ToArray();
        var supportedReferenceCount = references.Count(reference => reference.IsSupported);

        return new ComicGeometryCandidateRecallResult(
            allWords.Length,
            reliableWords.Length,
            expected.Length,
            supportedReferenceCount,
            reliableWords.Length - supportedCandidateIndexes.Count,
            references);
    }

    private bool IsReliable(OcrWord word)
    {
        return word.Confidence is { } confidence
            && confidence >= options.MinimumWordConfidence;
    }

    private ComicGeometryCandidateReference CreateReference(
        int expectedIndex,
        BoundingBox expectedBounds,
        IReadOnlyList<CandidateWord> reliableWords,
        ISet<int> supportedCandidateIndexes)
    {
        var supportingCandidates = reliableWords
            .Select(candidate => new CandidateIntersection(
                candidate,
                CalculateIntersection(candidate.Word.Bounds, expectedBounds)))
            .Where(candidate => IsSupportingCandidate(candidate, expectedBounds))
            .ToArray();
        foreach (var candidate in supportingCandidates)
        {
            supportedCandidateIndexes.Add(candidate.Candidate.Index);
        }

        var coveredArea = CalculateUnionArea(supportingCandidates.Select(candidate => candidate.Intersection!.Value));
        var expectedArea = CalculateArea(expectedBounds);
        var expectedCoverage = expectedArea == 0
            ? 0
            : coveredArea / (double)expectedArea;

        return new ComicGeometryCandidateReference(
            expectedIndex,
            expectedBounds,
            supportingCandidates.Length,
            expectedCoverage,
            supportingCandidates.Length >= options.MinimumSupportingWordCount,
            supportingCandidates.Select(candidate => candidate.Candidate.Index).ToArray());
    }

    private bool IsSupportingCandidate(CandidateIntersection candidate, BoundingBox expectedBounds)
    {
        if (candidate.Intersection is not { } intersection)
        {
            return false;
        }

        var candidateArea = CalculateArea(candidate.Candidate.Word.Bounds);
        if (candidateArea == 0)
        {
            return false;
        }

        var candidateCoverage = CalculateArea(intersection) / (double)candidateArea;
        return candidateCoverage >= options.MinimumCandidateInsideExpectedRatio;
    }

    private static BoundingBox? CalculateIntersection(BoundingBox first, BoundingBox second)
    {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);

        return right <= left || bottom <= top
            ? null
            : new BoundingBox(left, top, right - left, bottom - top);
    }

    private static long CalculateUnionArea(IEnumerable<BoundingBox> bounds)
    {
        var rectangles = bounds.ToArray();
        if (rectangles.Length == 0)
        {
            return 0;
        }

        var yCoordinates = rectangles
            .SelectMany(bounds => new[] { bounds.Y, bounds.Bottom })
            .Distinct()
            .Order()
            .ToArray();
        long area = 0;

        for (var index = 0; index < yCoordinates.Length - 1; index++)
        {
            var top = yCoordinates[index];
            var bottom = yCoordinates[index + 1];
            var xIntervals = rectangles
                .Where(bounds => bounds.Y < bottom && bounds.Bottom > top)
                .Select(bounds => (Left: bounds.X, Right: bounds.Right))
                .OrderBy(interval => interval.Left)
                .ThenBy(interval => interval.Right)
                .ToArray();
            if (xIntervals.Length == 0)
            {
                continue;
            }

            var coveredWidth = 0L;
            var currentLeft = xIntervals[0].Left;
            var currentRight = xIntervals[0].Right;
            foreach (var interval in xIntervals.Skip(1))
            {
                if (interval.Left > currentRight)
                {
                    coveredWidth += currentRight - currentLeft;
                    currentLeft = interval.Left;
                    currentRight = interval.Right;
                    continue;
                }

                currentRight = Math.Max(currentRight, interval.Right);
            }

            coveredWidth += currentRight - currentLeft;
            area += coveredWidth * (bottom - top);
        }

        return area;
    }

    private static long CalculateArea(BoundingBox bounds)
    {
        return (long)bounds.Width * bounds.Height;
    }

    private static void ValidateOptions(ComicGeometryCandidateRecallOptions options)
    {
        if (!double.IsFinite(options.MinimumWordConfidence) || options.MinimumWordConfidence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MinimumWordConfidence),
                "Minimum candidate confidence must be finite and non-negative.");
        }

        if (!double.IsFinite(options.MinimumCandidateInsideExpectedRatio)
            || options.MinimumCandidateInsideExpectedRatio is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MinimumCandidateInsideExpectedRatio),
                "Minimum candidate overlap ratio must be between zero and one.");
        }

        if (options.MinimumSupportingWordCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MinimumSupportingWordCount),
                "Minimum supporting word count must be positive.");
        }
    }

    private sealed record CandidateWord(int Index, OcrWord Word);

    private sealed record CandidateIntersection(CandidateWord Candidate, BoundingBox? Intersection);
}

public sealed record ComicGeometryCandidateRecallOptions
{
    public static ComicGeometryCandidateRecallOptions Default { get; } = new();

    public double MinimumWordConfidence { get; init; } = 50d;

    public double MinimumCandidateInsideExpectedRatio { get; init; } = 0.5d;

    public int MinimumSupportingWordCount { get; init; } = 2;
}

public sealed class ComicGeometryCandidateRecallResult
{
    public ComicGeometryCandidateRecallResult(
        int inputWordCount,
        int reliableWordCount,
        int referenceCount,
        int supportedReferenceCount,
        int outsideCandidateCount,
        IEnumerable<ComicGeometryCandidateReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);

        InputWordCount = inputWordCount;
        ReliableWordCount = reliableWordCount;
        ReferenceCount = referenceCount;
        SupportedReferenceCount = supportedReferenceCount;
        OutsideCandidateCount = outsideCandidateCount;
        References = references.ToArray();
    }

    public int InputWordCount { get; }

    public int ReliableWordCount { get; }

    public int ReferenceCount { get; }

    public int SupportedReferenceCount { get; }

    public int UnsupportedReferenceCount => ReferenceCount - SupportedReferenceCount;

    public int OutsideCandidateCount { get; }

    public double ReferenceRecall => ReferenceCount == 0
        ? 0
        : SupportedReferenceCount / (double)ReferenceCount;

    public double AverageExpectedCoverage => References.Count == 0
        ? 0
        : References.Average(reference => reference.ExpectedCoverage);

    public IReadOnlyList<ComicGeometryCandidateReference> References { get; }
}

public sealed record ComicGeometryCandidateReference(
    int ExpectedIndex,
    BoundingBox ExpectedBounds,
    int SupportingWordCount,
    double ExpectedCoverage,
    bool IsSupported,
    IReadOnlyList<int> SupportingWordIndexes);
