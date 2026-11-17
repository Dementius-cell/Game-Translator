namespace GameTranslator.Application.Overlay;

/// <summary>Optional, session-only control for recording the application's overlay.</summary>
public interface IOverlayCaptureControl
{
    bool IsRecordingAllowed { get; }
    TimeSpan? CaptureHiddenAfter => null;
    void SetRecordingAllowed(bool allowed);
}
