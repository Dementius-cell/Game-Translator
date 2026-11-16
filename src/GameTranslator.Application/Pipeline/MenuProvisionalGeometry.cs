using GameTranslator.Application.Ocr;

namespace GameTranslator.Application.Pipeline;

// Association hypothesis only. Never evidence of unchanged pixels or recognized text.
internal static class MenuProvisionalGeometry
{
    internal static bool Matches(BoundingBox anchor, IReadOnlyList<BoundingBox> anchorMembers,
        BoundingBox current, IReadOnlyList<BoundingBox> currentMembers)
    {
        if (anchorMembers.Count != 1 || anchorMembers[0] != anchor
            || currentMembers.Count != 1 || currentMembers[0] != current) return false;
        var width = Math.Max(0, Math.Min(anchor.Right, current.Right) - Math.Max(anchor.X, current.X));
        var height = Math.Max(0, Math.Min(anchor.Bottom, current.Bottom) - Math.Max(anchor.Y, current.Y));
        var intersection = (double)width * height;
        return intersection / ((double)anchor.Width * anchor.Height + (double)current.Width * current.Height - intersection) >= .5
            && Math.Abs(anchor.X - current.X) <= .5 * Math.Min(anchor.Width, current.Width)
            && Math.Abs(anchor.Right - current.Right) <= .5 * Math.Min(anchor.Width, current.Width)
            && Math.Abs(anchor.Y - current.Y) <= .5 * Math.Min(anchor.Height, current.Height)
            && Math.Abs(anchor.Bottom - current.Bottom) <= .5 * Math.Min(anchor.Height, current.Height);
    }
}
