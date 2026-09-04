using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Ocr;

/// <summary>
/// Forms bounded vertical text candidates without transitive connected-component merging.
/// </summary>
public sealed class BoundedTextCandidateGroupingService
{
    private const double HorizontalGapFactor = 0.5d;
    private const double MinimumSharedVerticalOverlapRatio = 0.5d;
    private const double VerticalGapFactor = 1.5d;
    private const double MinimumSharedHorizontalOverlapRatio = 0.5d;
    private const int MaximumVerticalGroupMembers = 4;
    private const int MaximumHorizontalStackGroupMembers = 6;
    private const int MaximumComplexSouthEastAsianHorizontalStackGroupMembers = 8;
    private const int AutomaticHorizontalStackSafetyLimit = 10;
    private const double MinimumStrictContinuationHorizontalOverlapRatio = 0.8d;
    private const int MaximumStrictContinuationVerticalGap = 12;
    private const int MinimumSignificantVerticalGapIncrease = 4;
    private const double MaximumNormalizedVerticalGapIncrease = 0.2d;
    private const double MinimumAdaptiveColumnWidthRatio = 0.5d;
    private const double MaximumAdaptiveColumnWidthRatio = 2d;
    private const double MaximumNormalizedTopOffset = 0.5d;
    private const double MaximumNormalizedBottomOffset = 0.75d;
    private const double MaximumNormalizedCenterOffset = 0.5d;
    private const double MinimumTightRaggedBottomSharedOverlapRatio = 0.95d;
    private const double MaximumTightRaggedBottomNormalizedTopOffset = 0.05d;
    private const double MaximumTightRaggedBottomNormalizedBottomOffset = 1d;
    private const int MaximumTightRaggedBottomHorizontalGap = 2;
    private const int MinimumSignificantHorizontalGapIncrease = 4;
    private const double MaximumNormalizedHorizontalGapIncrease = 0.2d;
    private const int DenseLayoutMinimumCandidates = 20;
    private const double DenseTopAlignmentMaximumZoneFraction = 0.05d;

    public IReadOnlyList<TextCandidate> Group(
        IEnumerable<TextCandidate> candidates,
        int zoneHeight)
    {
        return Group(candidates, zoneHeight, WritingSystemGroupingProfile.SpacedLeftToRight);
    }

    /// <summary>
    /// Forms bounded candidates using the global geometry limits plus a writing-system-specific tolerance where evidence supports it.
    /// </summary>
    public IReadOnlyList<TextCandidate> Group(
        IEnumerable<TextCandidate> candidates,
        int zoneHeight,
        WritingSystemGroupingProfile groupingProfile)
    {
        return Group(candidates, zoneHeight, groupingProfile, OcrCandidateGroupingSettings.Default);
    }

    /// <summary>
    /// Forms bounded candidates with optional per-zone hard limits. Auto mode follows the
    /// writing-system policy; explicit values remain hard limits.
    /// </summary>
    public IReadOnlyList<TextCandidate> Group(
        IEnumerable<TextCandidate> candidates,
        int zoneHeight,
        WritingSystemGroupingProfile groupingProfile,
        OcrCandidateGroupingSettings? candidateGroupingSettings)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (zoneHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoneHeight));
        }

        if (!Enum.IsDefined(groupingProfile))
        {
            throw new ArgumentOutOfRangeException(nameof(groupingProfile));
        }

        var groupingSettings = candidateGroupingSettings ?? OcrCandidateGroupingSettings.Default;
        ValidateGroupingSettings(groupingSettings);

        var materialized = candidates.ToArray();
        if (IsDenseTopAlignedLayout(materialized, zoneHeight))
        {
            return Order(materialized);
        }

        var usesAdaptiveCjkVerticalAuto = groupingProfile is WritingSystemGroupingProfile.CjkVertical
            && groupingSettings.MaximumVerticalColumns is null;
        var verticalCandidatesSource = materialized
            .Where(candidate => candidate.Bounds.Height >= candidate.Bounds.Width);
        var verticalCandidates = usesAdaptiveCjkVerticalAuto
            ? verticalCandidatesSource
                .OrderByDescending(candidate => candidate.Bounds.X)
                .ThenBy(candidate => candidate.Bounds.Y)
                .ThenBy(candidate => candidate.Bounds.Width)
                .ThenBy(candidate => candidate.Bounds.Height)
                .ToList()
            : verticalCandidatesSource
                .OrderBy(candidate => candidate.Bounds.X)
                .ThenBy(candidate => candidate.Bounds.Y)
                .ThenBy(candidate => candidate.Bounds.Width)
                .ThenBy(candidate => candidate.Bounds.Height)
                .ToList();
        var grouped = materialized
            .Where(candidate => candidate.Bounds.Height < candidate.Bounds.Width)
            .OrderBy(candidate => candidate.Bounds.Y)
            .ThenBy(candidate => candidate.Bounds.X)
            .ThenBy(candidate => candidate.Bounds.Width)
            .ThenBy(candidate => candidate.Bounds.Height)
            .ToList();

        if (usesAdaptiveCjkVerticalAuto)
        {
            GroupAdaptiveCjkVerticalCandidates(verticalCandidates, grouped);
        }
        else
        {
            while (verticalCandidates.Count > 0)
            {
                var group = new List<TextCandidate> { verticalCandidates[0] };
                verticalCandidates.RemoveAt(0);
                var maximumVerticalGroupMembers = groupingSettings.MaximumVerticalColumns
                    ?? MaximumVerticalGroupMembers;
                while (group.Count < maximumVerticalGroupMembers)
                {
                    var next = verticalCandidates
                        .Where(candidate => CanExtend(group, candidate))
                        .OrderBy(candidate => group.Min(member => HorizontalGap(member.Bounds, candidate.Bounds)))
                        .ThenBy(candidate => candidate.Bounds.X)
                        .ThenBy(candidate => candidate.Bounds.Y)
                        .ThenBy(candidate => candidate.Bounds.Width)
                        .ThenBy(candidate => candidate.Bounds.Height)
                        .FirstOrDefault();
                    if (next is null)
                    {
                        break;
                    }

                    verticalCandidates.Remove(next);
                    group.Add(next);
                }

                grouped.Add(CreateGroupedCandidate(group));
            }
        }

        var horizontalCandidates = grouped
            .Where(candidate => candidate.Bounds.Height < candidate.Bounds.Width)
            .OrderBy(candidate => candidate.Bounds.Y)
            .ThenBy(candidate => candidate.Bounds.X)
            .ThenBy(candidate => candidate.Bounds.Width)
            .ThenBy(candidate => candidate.Bounds.Height)
            .ToList();
        grouped.RemoveAll(candidate => candidate.Bounds.Height < candidate.Bounds.Width);

        while (horizontalCandidates.Count > 0)
        {
            var group = new List<TextCandidate> { horizontalCandidates[0] };
            horizontalCandidates.RemoveAt(0);
            var automaticPrimaryLimit = GetMaximumHorizontalStackGroupMembers(groupingProfile);
            var usesAdaptiveSpacedHorizontalAuto = groupingProfile is WritingSystemGroupingProfile.SpacedLeftToRight
                && groupingSettings.MaximumHorizontalLines is null;
            var maximumHorizontalGroupMembers = groupingSettings.MaximumHorizontalLines;
            if (maximumHorizontalGroupMembers is null && !usesAdaptiveSpacedHorizontalAuto)
            {
                maximumHorizontalGroupMembers = AutomaticHorizontalStackSafetyLimit;
            }

            while (maximumHorizontalGroupMembers is null
                || group.Count < maximumHorizontalGroupMembers.Value)
            {
                var next = horizontalCandidates
                    .Where(candidate => CanExtendVerticalStack(group, candidate, groupingProfile))
                    .Where(candidate => groupingSettings.MaximumHorizontalLines is not null
                        || group.Count < automaticPrimaryLimit
                        || (CanStrictlyExtendAutomaticVerticalStack(group, candidate)
                            && (!usesAdaptiveSpacedHorizontalAuto
                                || group.Count < AutomaticHorizontalStackSafetyLimit
                                || !HasSignificantAutomaticVerticalGapIncrease(group, candidate))))
                    .OrderBy(candidate => group.Min(member => VerticalGap(member.Bounds, candidate.Bounds)))
                    .ThenBy(candidate => candidate.Bounds.Y)
                    .ThenBy(candidate => candidate.Bounds.X)
                    .ThenBy(candidate => candidate.Bounds.Width)
                    .ThenBy(candidate => candidate.Bounds.Height)
                    .FirstOrDefault();
                if (next is null)
                {
                    break;
                }

                horizontalCandidates.Remove(next);
                group.Add(next);
            }

            grouped.Add(CreateGroupedCandidate(group));
        }

        return Order(grouped);
    }

    private static void GroupAdaptiveCjkVerticalCandidates(
        List<TextCandidate> candidates,
        ICollection<TextCandidate> grouped)
    {
        while (candidates.Count > 0)
        {
            var group = new List<TextCandidate> { candidates[0] };
            candidates.RemoveAt(0);
            while (true)
            {
                var next = FindImmediateCjkVerticalNeighbor(group[^1], candidates);
                if (next is null || !CanExtendAdaptiveCjkVerticalGroup(group, next))
                {
                    break;
                }

                candidates.Remove(next);
                group.Add(next);
            }

            grouped.Add(CreateGroupedCandidate(group));
        }
    }

    private static TextCandidate? FindImmediateCjkVerticalNeighbor(
        TextCandidate current,
        IEnumerable<TextCandidate> candidates)
    {
        var currentCenterX = GetCenterX(current.Bounds);
        return candidates
            .Where(candidate => GetCenterX(candidate.Bounds) < currentCenterX)
            .Where(candidate => SharedVerticalOverlapRatio(new[] { current, candidate })
                >= MinimumSharedVerticalOverlapRatio)
            .OrderByDescending(candidate => GetCenterX(candidate.Bounds))
            .ThenBy(candidate => candidate.Bounds.Y)
            .ThenBy(candidate => candidate.Bounds.Width)
            .ThenBy(candidate => candidate.Bounds.Height)
            .FirstOrDefault();
    }

    private static bool CanExtendAdaptiveCjkVerticalGroup(
        IReadOnlyList<TextCandidate> group,
        TextCandidate candidate)
    {
        if (SharedVerticalOverlapRatio(group.Append(candidate)) < MinimumSharedVerticalOverlapRatio)
        {
            return false;
        }

        var adjacent = group[^1];
        var adjacentGap = HorizontalGap(adjacent.Bounds, candidate.Bounds);
        if (adjacentGap > MaximumHorizontalGap(adjacent.Bounds, candidate.Bounds))
        {
            return false;
        }

        if (!HasCoherentAdaptiveColumnWidth(group, candidate)
            || !HasCoherentAdaptiveVerticalAlignment(group, candidate, adjacentGap))
        {
            return false;
        }

        return !HasSignificantAdaptiveHorizontalGapIncrease(group, candidate, adjacentGap);
    }

    private static bool HasCoherentAdaptiveColumnWidth(
        IReadOnlyList<TextCandidate> group,
        TextCandidate candidate)
    {
        var prospectiveGroup = group.Append(candidate).ToArray();
        var medianWidth = Median(prospectiveGroup.Select(member => member.Bounds.Width));
        return prospectiveGroup.All(member =>
        {
            var widthRatio = member.Bounds.Width / medianWidth;
            return widthRatio is >= MinimumAdaptiveColumnWidthRatio and <= MaximumAdaptiveColumnWidthRatio;
        });
    }

    private static bool HasCoherentAdaptiveVerticalAlignment(
        IReadOnlyList<TextCandidate> group,
        TextCandidate candidate,
        int adjacentGap)
    {
        var prospectiveGroup = group.Append(candidate).ToArray();
        var medianHeight = Median(prospectiveGroup.Select(member => member.Bounds.Height));
        var medianTop = Median(prospectiveGroup.Select(member => member.Bounds.Y));
        var medianBottom = Median(prospectiveGroup.Select(member => member.Bounds.Bottom));
        var medianCenter = Median(prospectiveGroup.Select(member => GetCenterY(member.Bounds)));

        var hasStandardAlignment = prospectiveGroup.All(member =>
            Math.Abs(member.Bounds.Y - medianTop) / medianHeight <= MaximumNormalizedTopOffset
            && Math.Abs(member.Bounds.Bottom - medianBottom) / medianHeight <= MaximumNormalizedBottomOffset
            && Math.Abs(GetCenterY(member.Bounds) - medianCenter) / medianHeight <= MaximumNormalizedCenterOffset);
        if (hasStandardAlignment)
        {
            return true;
        }

        if (prospectiveGroup.Length < 3
            || adjacentGap > MaximumTightRaggedBottomHorizontalGap
            || SharedVerticalOverlapRatio(prospectiveGroup) < MinimumTightRaggedBottomSharedOverlapRatio
            || Enumerable.Range(1, prospectiveGroup.Length - 1).Any(index =>
                HorizontalGap(prospectiveGroup[index - 1].Bounds, prospectiveGroup[index].Bounds)
                    > MaximumTightRaggedBottomHorizontalGap))
        {
            return false;
        }

        return prospectiveGroup.All(member =>
            Math.Abs(member.Bounds.Y - medianTop) / medianHeight <= MaximumTightRaggedBottomNormalizedTopOffset
            && Math.Abs(member.Bounds.Bottom - medianBottom) / medianHeight <= MaximumTightRaggedBottomNormalizedBottomOffset
            && Math.Abs(GetCenterY(member.Bounds) - medianCenter) / medianHeight <= MaximumNormalizedCenterOffset);
    }

    private static bool HasSignificantAdaptiveHorizontalGapIncrease(
        IReadOnlyList<TextCandidate> group,
        TextCandidate candidate,
        int adjacentGap)
    {
        if (group.Count < 2)
        {
            return false;
        }

        var previousGaps = Enumerable.Range(1, group.Count - 1)
            .Select(index => HorizontalGap(group[index - 1].Bounds, group[index].Bounds));
        var medianPreviousGap = Median(previousGaps);
        var medianColumnWidth = Median(group
            .Append(candidate)
            .Select(member => member.Bounds.Width));
        var normalizedGapIncrease = (adjacentGap - medianPreviousGap) / medianColumnWidth;

        return adjacentGap - medianPreviousGap > MinimumSignificantHorizontalGapIncrease
            && normalizedGapIncrease > MaximumNormalizedHorizontalGapIncrease;
    }

    private static void ValidateGroupingSettings(OcrCandidateGroupingSettings settings)
    {
        if (!IsValidGroupingLimit(settings.MaximumHorizontalLines)
            || !IsValidGroupingLimit(settings.MaximumVerticalColumns))
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                $"Candidate grouping limits must be {OcrCandidateGroupingSettings.MinimumLimit} through {OcrCandidateGroupingSettings.MaximumLimit}, or Auto.");
        }
    }

    private static bool IsValidGroupingLimit(int? value)
    {
        return value is null
            or >= OcrCandidateGroupingSettings.MinimumLimit and <= OcrCandidateGroupingSettings.MaximumLimit;
    }

    private static bool CanStrictlyExtendAutomaticVerticalStack(
        IReadOnlyList<TextCandidate> group,
        TextCandidate candidate)
    {
        var groupLeft = group.Min(member => member.Bounds.X);
        var groupRight = group.Max(member => member.Bounds.Right);
        var overlap = Math.Min(groupRight, candidate.Bounds.Right) - Math.Max(groupLeft, candidate.Bounds.X);
        var smallerWidth = Math.Min(groupRight - groupLeft, candidate.Bounds.Width);
        if (overlap <= 0
            || overlap / (double)smallerWidth < MinimumStrictContinuationHorizontalOverlapRatio)
        {
            return false;
        }

        var last = group
            .OrderByDescending(member => member.Bounds.Bottom)
            .ThenByDescending(member => member.Bounds.Y)
            .First();
        return candidate.Bounds.Y >= last.Bounds.Y
            && VerticalGap(last.Bounds, candidate.Bounds) <= MaximumStrictContinuationVerticalGap;
    }

    private static bool HasSignificantAutomaticVerticalGapIncrease(
        IReadOnlyList<TextCandidate> group,
        TextCandidate candidate)
    {
        if (group.Count < 2)
        {
            return false;
        }

        var ordered = group
            .OrderBy(member => member.Bounds.Y)
            .ThenBy(member => member.Bounds.X)
            .ToArray();
        var previousGaps = Enumerable.Range(1, ordered.Length - 1)
            .Select(index => VerticalGap(ordered[index - 1].Bounds, ordered[index].Bounds));
        var medianPreviousGap = Median(previousGaps);
        var last = ordered
            .OrderByDescending(member => member.Bounds.Bottom)
            .ThenByDescending(member => member.Bounds.Y)
            .First();
        var adjacentGap = VerticalGap(last.Bounds, candidate.Bounds);
        var medianLineHeight = Median(group
            .Append(candidate)
            .Select(member => member.Bounds.Height));
        var normalizedGapIncrease = (adjacentGap - medianPreviousGap) / medianLineHeight;

        return adjacentGap - medianPreviousGap > MinimumSignificantVerticalGapIncrease
            && normalizedGapIncrease > MaximumNormalizedVerticalGapIncrease;
    }

    private static bool IsDenseTopAlignedLayout(
        IReadOnlyList<TextCandidate> candidates,
        int zoneHeight)
    {
        if (candidates.Count < DenseLayoutMinimumCandidates)
        {
            return false;
        }

        var tops = candidates.Select(candidate => candidate.Bounds.Y).Order().ToArray();
        var percentile80Top = tops[(int)Math.Round((tops.Length - 1) * 0.8d)];
        return (percentile80Top - tops[0]) / (double)zoneHeight
            <= DenseTopAlignmentMaximumZoneFraction;
    }

    private static bool CanExtend(
        IReadOnlyList<TextCandidate> group,
        TextCandidate candidate)
    {
        if (SharedVerticalOverlapRatio(group.Append(candidate)) < MinimumSharedVerticalOverlapRatio)
        {
            return false;
        }

        return group.Min(member => HorizontalGap(member.Bounds, candidate.Bounds))
            <= group.Min(member => MaximumHorizontalGap(member.Bounds, candidate.Bounds));
    }

    private static bool CanExtendVerticalStack(
        IReadOnlyList<TextCandidate> group,
        TextCandidate candidate,
        WritingSystemGroupingProfile groupingProfile)
    {
        if (SharedHorizontalOverlapRatio(group.Append(candidate))
            < GetMinimumSharedHorizontalOverlapRatio(groupingProfile))
        {
            return false;
        }

        return group.Min(member => VerticalGap(member.Bounds, candidate.Bounds))
            <= group.Min(member => MaximumVerticalGap(member.Bounds, candidate.Bounds));
    }

    private static double GetMinimumSharedHorizontalOverlapRatio(
        WritingSystemGroupingProfile groupingProfile)
    {
        // Thai, Lao, Khmer and Myanmar use complex word-boundary rules. Their detected
        // line boxes may be more ragged than spaced-language lines in one dialog bubble.
        // All gap, group-size and dense-layout bounds remain the global limits.
        return groupingProfile is WritingSystemGroupingProfile.ComplexSouthEastAsian
            ? 0.4d
            : MinimumSharedHorizontalOverlapRatio;
    }

    private static int GetMaximumHorizontalStackGroupMembers(
        WritingSystemGroupingProfile groupingProfile)
    {
        // Preserve the global cap for every other profile. Complex SEA dialog text
        // can produce more short lines inside a single bounded bubble.
        return groupingProfile is WritingSystemGroupingProfile.ComplexSouthEastAsian
            ? MaximumComplexSouthEastAsianHorizontalStackGroupMembers
            : MaximumHorizontalStackGroupMembers;
    }

    private static TextCandidate CreateGroupedCandidate(IReadOnlyList<TextCandidate> group)
    {
        var left = group.Min(candidate => candidate.Bounds.X);
        var top = group.Min(candidate => candidate.Bounds.Y);
        var right = group.Max(candidate => candidate.Bounds.Right);
        var bottom = group.Max(candidate => candidate.Bounds.Bottom);
        return new TextCandidate(
            new BoundingBox(left, top, right - left, bottom - top),
            group.Min(candidate => candidate.Confidence))
        {
            SourceCandidateBounds = group
                .SelectMany(candidate => candidate.SourceCandidateBounds)
                .ToArray(),
        };
    }

    private static double SharedVerticalOverlapRatio(IEnumerable<TextCandidate> candidates)
    {
        var materialized = candidates.ToArray();
        var sharedTop = materialized.Max(candidate => candidate.Bounds.Y);
        var sharedBottom = materialized.Min(candidate => candidate.Bounds.Bottom);
        return sharedBottom <= sharedTop
            ? 0d
            : (sharedBottom - sharedTop) / (double)materialized.Min(candidate => candidate.Bounds.Height);
    }

    private static double SharedHorizontalOverlapRatio(IEnumerable<TextCandidate> candidates)
    {
        var materialized = candidates.ToArray();
        var sharedLeft = materialized.Max(candidate => candidate.Bounds.X);
        var sharedRight = materialized.Min(candidate => candidate.Bounds.Right);
        return sharedRight <= sharedLeft
            ? 0d
            : (sharedRight - sharedLeft) / (double)materialized.Min(candidate => candidate.Bounds.Width);
    }

    private static int HorizontalGap(BoundingBox first, BoundingBox second)
    {
        return Math.Max(0, Math.Max(first.X, second.X) - Math.Min(first.Right, second.Right));
    }

    private static int MaximumHorizontalGap(BoundingBox first, BoundingBox second)
    {
        return Math.Max(12, (int)Math.Round(Math.Min(first.Width, second.Width) * HorizontalGapFactor));
    }

    private static int VerticalGap(BoundingBox first, BoundingBox second)
    {
        return Math.Max(0, Math.Max(first.Y, second.Y) - Math.Min(first.Bottom, second.Bottom));
    }

    private static int MaximumVerticalGap(BoundingBox first, BoundingBox second)
    {
        return Math.Max(12, (int)Math.Round(Math.Min(first.Height, second.Height) * VerticalGapFactor));
    }

    private static double GetCenterX(BoundingBox bounds)
    {
        return bounds.X + bounds.Width / 2d;
    }

    private static double GetCenterY(BoundingBox bounds)
    {
        return bounds.Y + bounds.Height / 2d;
    }

    private static double Median(IEnumerable<int> values)
    {
        return Median(values.Select(value => (double)value));
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(values));
        }

        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2d
            : ordered[middle];
    }

    private static IReadOnlyList<TextCandidate> Order(IEnumerable<TextCandidate> candidates)
    {
        return candidates
            .OrderBy(candidate => candidate.Bounds.Y)
            .ThenBy(candidate => candidate.Bounds.X)
            .ThenBy(candidate => candidate.Bounds.Width)
            .ThenBy(candidate => candidate.Bounds.Height)
            .ToArray();
    }
}
