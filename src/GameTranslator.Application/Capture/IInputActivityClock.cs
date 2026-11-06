namespace GameTranslator.Application.Capture;

/// <summary>Elapsed milliseconds since the latest session input, if observable.</summary>
public interface IInputActivityClock
{
    int? ReadAgeMilliseconds();
}
