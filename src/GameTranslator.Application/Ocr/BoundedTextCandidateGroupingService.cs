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
    private const int DenseLayoutMinimumCandidates = 20;
    private const double DenseTopAlignmentMaximumZoneFraction = 0.05d;

    public IReadOnlyList<TextCandidate> Group(
        IEnumerable<TextCandidate> candidates,
        int zoneHeight)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (zoneHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoneHeight));
        }

        var materialized = candidates.ToArray();
        if (IsDenseTopAlignedLayout(materialized, zoneHeight))
        {
            return Order(materialized);
        }

        var verticalCandidates = materialized
            .Where(candidate => candidate.Bounds.Height >= candidate.Bounds.Width)
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

        while (verticalCandidates.Count > 0)
        {
            var group = new List<TextCandidate> { verticalCandidates[0] };
            verticalCandidates.RemoveAt(0);
            while (group.Count < MaximumVerticalGroupMembers)
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
            while (group.Count < MaximumHorizontalStackGroupMembers)
            {
                var next = horizontalCandidates
                    .Where(candidate => CanExtendVerticalStack(group, candidate))
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
        TextCandidate candidate)
    {
        if (SharedHorizontalOverlapRatio(group.Append(candidate)) < MinimumSharedHorizontalOverlapRatio)
        {
            return false;
        }

        return group.Min(member => VerticalGap(member.Bounds, candidate.Bounds))
            <= group.Min(member => MaximumVerticalGap(member.Bounds, candidate.Bounds));
    }

    private static TextCandidate CreateGroupedCandidate(IReadOnlyList<TextCandidate> group)
    {
        var left = group.Min(candidate => candidate.Bounds.X);
        var top = group.Min(candidate => candidate.Bounds.Y);
        var right = group.Max(candidate => candidate.Bounds.Right);
        var bottom = group.Max(candidate => candidate.Bounds.Bottom);
        return new TextCandidate(
            new BoundingBox(left, top, right - left, bottom - top),
            group.Min(candidate => candidate.Confidence));
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
