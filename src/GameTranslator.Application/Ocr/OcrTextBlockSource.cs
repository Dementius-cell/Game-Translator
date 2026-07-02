using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Ocr;

/// <summary>
/// Carries source OCR geometry for a translated semantic text block.
/// </summary>
public sealed class OcrTextBlockSource
{
    public OcrTextBlockSource(
        BoundingBox semanticBounds,
        IEnumerable<BoundingBox> memberBounds,
        OcrOrientationMode orientationMode = OcrOrientationMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(memberBounds);

        var members = memberBounds.ToArray();
        if (members.Length == 0)
        {
            throw new ArgumentException("Text block source must include at least one member bound.", nameof(memberBounds));
        }

        SemanticBounds = semanticBounds;
        MemberBounds = members;
        OrientationMode = Enum.IsDefined(orientationMode)
            ? orientationMode
            : OcrOrientationMode.Auto;
    }

    public BoundingBox SemanticBounds { get; }

    public IReadOnlyList<BoundingBox> MemberBounds { get; }

    public OcrOrientationMode OrientationMode { get; }
}
