namespace GameTranslator.Domain.Profiles;

/// <summary>
/// Selects a named, profile-compatible post-processing policy for transient text detection.
/// Existing profiles use <see cref="Standard"/>; experimental Chinese values remain explicit opt-ins.
/// </summary>
public enum TextCandidateDetectorPreset
{
    Standard,
    ChineseExperimental,
    ChineseStrictExperimental,
}
