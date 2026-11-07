namespace GameTranslator.Infrastructure.Capture;

// Describes the active Windows output mode, not the monitor's advertised capability.
internal sealed record CaptureColorInfo(string Mode, double SdrWhiteNits, string Detection, bool IsSdrWhiteLevelKnown = false)
{
    internal static CaptureColorInfo Unknown { get; } = new("Unknown", 80, "unavailable");
    internal bool UseFloatCapture => Mode != "SDR";
    internal string PoolFormat => UseFloatCapture ? "R16G16B16A16Float" : "B8G8R8A8UIntNormalized";
    internal string ToneMapping => UseFloatCapture ? "scRGB SDR-white normalized, fixed shoulder, sRGB v1" : "none (SDR)";

    internal static CaptureColorInfo FromAdvancedColor(uint flags, uint? activeMode, uint? whiteLevel)
    {
        var mode = activeMode switch
        {
            0 => "SDR", 1 => "WCG", 2 => "HDR",
            null => (flags & 2) == 0 ? "SDR" : (flags & 4) != 0 ? "WCG" : "HDR",
            _ => "Unknown"
        };
        var white = whiteLevel is > 0 ? whiteLevel.Value * .08 : 80;
        var whiteKnown = whiteLevel is > 0 && double.IsFinite(white) && white >= 80 && white <= 10000;
        if (!whiteKnown) white = 80;
        return new(mode, white, activeMode.HasValue ? "DisplayConfig advanced-color-v2" : "DisplayConfig advanced-color-v1", whiteKnown);
    }
}
