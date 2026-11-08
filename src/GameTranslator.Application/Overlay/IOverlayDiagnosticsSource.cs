namespace GameTranslator.Application.Overlay;

public interface IOverlayDiagnosticsSource
{
    IReadOnlyList<OverlayPresentationDiagnostic> PresentationDiagnostics { get; }
    long DroppedPresentationDiagnostics { get; }
}

/// <summary>WPF render callbacks precede composition; they do not prove physical display presentation.</summary>
public sealed record OverlayPresentationDiagnostic(long Sequence, DateTimeOffset At, Guid? SnapshotId,
    string Event, int TextItems, int MaskItems, bool Visible, bool CaptureExcluded,
    double ElapsedMs, double Width, double Height, double DpiScaleX, double DpiScaleY,
    bool LayoutValid, string? ExceptionType = null, int? HResult = null);
