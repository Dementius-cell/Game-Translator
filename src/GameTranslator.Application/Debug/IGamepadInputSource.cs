namespace GameTranslator.Application.Debug;

/// <summary>Read-only diagnostic input. Button bits use the documented XInput layout.</summary>
public readonly record struct GamepadInputState(ushort Buttons, byte LeftTrigger, byte RightTrigger,
    short LeftX, short LeftY, short RightX, short RightY);

public interface IGamepadInputSource
{
    /// <summary>Read slot 0–3, or return null when disconnected.</summary>
    GamepadInputState? Read(int slot);
}
