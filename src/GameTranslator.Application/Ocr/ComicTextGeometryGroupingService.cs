namespace GameTranslator.Application.Ocr;

/// <summary>
/// Builds compact vertical comic text groups from reliable word geometry.
/// </summary>
public sealed class ComicTextGeometryGroupingService
{
    private readonly ComicTextGeometryGroupingOptions options;

    public ComicTextGeometryGroupingService()
        : this(ComicTextGeometryGroupingOptions.Default)
    {
    }

    public ComicTextGeometryGroupingService(ComicTextGeometryGroupingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        this.options = options;
    }

    /// <summary>
    /// Groups confident vertical CJK word fragments from one OCR recognition pass.
    /// </summary>
    public IReadOnlyList<ComicTextGeometryGroup> GroupVerticalWords(IEnumerable<OcrWord> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        var candidates = words
            .Where(IsReliableVerticalCandidate)
            .DistinctBy(word => (word.Text, word.Bounds, word.Confidence, word.RecognitionPassId))
            .OrderByDescending(word => GetCenterX(word.Bounds))
            .ThenBy(word => word.Bounds.Y)
            .ThenBy(word => word.Bounds.X)
            .ToArray();
        if (candidates.Length == 0)
        {
            return Array.Empty<ComicTextGeometryGroup>();
        }

        var medianWidth = CalculateMedian(candidates.Select(word => word.Bounds.Width));
        var medianHeight = CalculateMedian(candidates.Select(word => word.Bounds.Height));
        var clusters = new List<List<OcrWord>>();

        foreach (var candidate in candidates)
        {
            var group = FindClosestConnectedGroup(clusters, candidate, medianWidth, medianHeight);
            if (group is null)
            {
                clusters.Add(new List<OcrWord> { candidate });
                continue;
            }

            group.Add(candidate);
        }

        return clusters
            .Where(IsAcceptableGroup)
            .Select(CreateGroup)
            .OrderBy(group => group.SemanticBounds.Y)
            .ThenByDescending(group => group.SemanticBounds.X)
            .ToArray();
    }

    private bool IsReliableVerticalCandidate(OcrWord word)
    {
        if (word.Confidence is not { } confidence || confidence < options.MinimumWordConfidence)
        {
            return false;
        }

        return word.Bounds.Width <= word.Bounds.Height * options.MaximumCandidateWidthToHeightRatio;
    }

    private List<OcrWord>? FindClosestConnectedGroup(
        IReadOnlyList<List<OcrWord>> groups,
        OcrWord candidate,
        double medianWidth,
        double medianHeight)
    {
        return groups
            .Where(group => CanAddToGroup(group, candidate, medianWidth, medianHeight))
            .OrderBy(group => CalculateGroupDistance(group, candidate))
            .ThenByDescending(group => group.Max(word => GetCenterX(word.Bounds)))
            .ThenBy(group => group.Min(word => word.Bounds.Y))
            .FirstOrDefault();
    }

    private bool CanAddToGroup(
        IReadOnlyList<OcrWord> group,
        OcrWord candidate,
        double medianWidth,
        double medianHeight)
    {
        var combinedBounds = CreateCombinedBounds(group.Select(word => word.Bounds).Append(candidate.Bounds));
        if (combinedBounds.Width > medianWidth * options.MaximumGroupWidthFactor)
        {
            return false;
        }

        return group.Any(member => AreNeighbors(member.Bounds, candidate.Bounds, medianWidth, medianHeight));
    }

    private bool AreNeighbors(
        BoundingBox first,
        BoundingBox second,
        double medianWidth,
        double medianHeight)
    {
        var horizontalDistance = Math.Abs(GetCenterX(first) - GetCenterX(second));
        var maximumHorizontalDistance = Math.Max(
            medianWidth * options.MaximumNeighborHorizontalDistanceFactor,
            Math.Max(first.Width, second.Width) * options.MaximumNeighborHorizontalDistanceFactor);
        if (horizontalDistance > maximumHorizontalDistance)
        {
            return false;
        }

        var verticalGap = Math.Max(0, Math.Max(first.Y - second.Bottom, second.Y - first.Bottom));
        var maximumVerticalGap = Math.Max(
            medianHeight * options.MaximumVerticalGapFactor,
            Math.Max(first.Height, second.Height) * options.MaximumVerticalGapFactor);

        return verticalGap <= maximumVerticalGap;
    }

    private bool IsAcceptableGroup(IReadOnlyList<OcrWord> group)
    {
        if (group.Count < options.MinimumGroupWordCount)
        {
            return false;
        }

        var bounds = CreateCombinedBounds(group.Select(word => word.Bounds));
        return bounds.Height >= bounds.Width * options.MinimumGroupHeightToWidthRatio;
    }

    private static ComicTextGeometryGroup CreateGroup(IReadOnlyList<OcrWord> words)
    {
        var orderedWords = OrderWordsForVerticalReading(words);
        var memberBounds = orderedWords.Select(word => word.Bounds).ToArray();
        var semanticBounds = CreateCombinedBounds(memberBounds);
        var text = string.Concat(orderedWords.Select(word => word.Text));

        return new ComicTextGeometryGroup(text, semanticBounds, memberBounds, orderedWords);
    }

    private static IReadOnlyList<OcrWord> OrderWordsForVerticalReading(IReadOnlyList<OcrWord> words)
    {
        var medianWidth = CalculateMedian(words.Select(word => word.Bounds.Width));
        var columns = new List<List<OcrWord>>();

        foreach (var word in words
                     .OrderByDescending(word => GetCenterX(word.Bounds))
                     .ThenBy(word => word.Bounds.Y)
                     .ThenBy(word => word.Bounds.X))
        {
            var column = columns
                .Where(existing => Math.Abs(
                    existing.Average(item => GetCenterX(item.Bounds)) - GetCenterX(word.Bounds))
                    <= medianWidth * 0.75d)
                .OrderBy(existing => Math.Abs(
                    existing.Average(item => GetCenterX(item.Bounds)) - GetCenterX(word.Bounds)))
                .FirstOrDefault();
            if (column is null)
            {
                columns.Add(new List<OcrWord> { word });
                continue;
            }

            column.Add(word);
        }

        return columns
            .OrderByDescending(column => column.Average(word => GetCenterX(word.Bounds)))
            .SelectMany(column => column
                .OrderBy(word => word.Bounds.Y)
                .ThenByDescending(word => GetCenterX(word.Bounds)))
            .ToArray();
    }

    private static double CalculateGroupDistance(IReadOnlyList<OcrWord> group, OcrWord candidate)
    {
        return group
            .Select(word =>
            {
                var horizontalDistance = Math.Abs(GetCenterX(word.Bounds) - GetCenterX(candidate.Bounds));
                var verticalGap = Math.Max(0, Math.Max(
                    word.Bounds.Y - candidate.Bounds.Bottom,
                    candidate.Bounds.Y - word.Bounds.Bottom));

                return horizontalDistance + verticalGap;
            })
            .Min();
    }

    private static BoundingBox CreateCombinedBounds(IEnumerable<BoundingBox> bounds)
    {
        var materializedBounds = bounds.ToArray();
        var left = materializedBounds.Min(bound => bound.X);
        var top = materializedBounds.Min(bound => bound.Y);
        var right = materializedBounds.Max(bound => bound.Right);
        var bottom = materializedBounds.Max(bound => bound.Bottom);

        return new BoundingBox(left, top, right - left, bottom - top);
    }

    private static double CalculateMedian(IEnumerable<int> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("Comic word grouping requires at least one word.", nameof(values));
        }

        return ordered.Length % 2 == 1
            ? ordered[ordered.Length / 2]
            : (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2d;
    }

    private static double GetCenterX(BoundingBox bounds)
    {
        return bounds.X + bounds.Width / 2d;
    }

    private static void ValidateOptions(ComicTextGeometryGroupingOptions options)
    {
        ValidateNonNegativeFinite(options.MinimumWordConfidence, nameof(options.MinimumWordConfidence));
        ValidatePositive(options.MinimumGroupWordCount, nameof(options.MinimumGroupWordCount));
        ValidatePositive(options.MaximumCandidateWidthToHeightRatio, nameof(options.MaximumCandidateWidthToHeightRatio));
        ValidatePositive(options.MaximumNeighborHorizontalDistanceFactor, nameof(options.MaximumNeighborHorizontalDistanceFactor));
        ValidatePositive(options.MaximumVerticalGapFactor, nameof(options.MaximumVerticalGapFactor));
        ValidatePositive(options.MaximumGroupWidthFactor, nameof(options.MaximumGroupWidthFactor));
        ValidatePositive(options.MinimumGroupHeightToWidthRatio, nameof(options.MinimumGroupHeightToWidthRatio));
    }

    private static void ValidateNonNegativeFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Comic word grouping values must be finite and non-negative.");
        }
    }

    private static void ValidatePositive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Comic word grouping values must be finite and positive.");
        }
    }

    private static void ValidatePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Comic word grouping values must be positive.");
        }
    }
}

public sealed record ComicTextGeometryGroupingOptions
{
    public static ComicTextGeometryGroupingOptions Default { get; } = new();

    public double MinimumWordConfidence { get; init; } = 50d;

    public int MinimumGroupWordCount { get; init; } = 2;

    public double MaximumCandidateWidthToHeightRatio { get; init; } = 1.5d;

    public double MaximumNeighborHorizontalDistanceFactor { get; init; } = 3d;

    public double MaximumVerticalGapFactor { get; init; } = 2.5d;

    public double MaximumGroupWidthFactor { get; init; } = 6d;

    public double MinimumGroupHeightToWidthRatio { get; init; } = 1.1d;
}

public sealed class ComicTextGeometryGroup
{
    public ComicTextGeometryGroup(
        string text,
        BoundingBox semanticBounds,
        IEnumerable<BoundingBox> memberBounds,
        IEnumerable<OcrWord> words)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(memberBounds);
        ArgumentNullException.ThrowIfNull(words);

        var members = memberBounds.ToArray();
        var wordList = words.ToArray();
        if (members.Length == 0 || wordList.Length == 0)
        {
            throw new ArgumentException("Comic geometry group must include source words and member bounds.");
        }

        Text = text;
        SemanticBounds = semanticBounds;
        MemberBounds = members;
        Words = wordList;
    }

    public string Text { get; }

    public BoundingBox SemanticBounds { get; }

    public IReadOnlyList<BoundingBox> MemberBounds { get; }

    public IReadOnlyList<OcrWord> Words { get; }
}
