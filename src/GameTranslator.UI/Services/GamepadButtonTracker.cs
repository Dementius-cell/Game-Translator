using GameTranslator.Application.Debug;

namespace GameTranslator.UI.Services;

internal sealed class GamepadButtonTracker
{
    private uint held;
    private bool connected;
    private static readonly (uint Mask, string Name)[] buttons =
    [
        (0x1, "DPadUp"), (0x2, "DPadDown"), (0x4, "DPadLeft"), (0x8, "DPadRight"),
        (0x10, "Start"), (0x20, "Back"), (0x40, "LeftStick"), (0x80, "RightStick"),
        (0x100, "LB"), (0x200, "RB"), (0x1000, "A"), (0x2000, "B"), (0x4000, "X"), (0x8000, "Y"),
        (1u << 16, "LT"), (1u << 17, "RT"),
        (1u << 18, "LeftStickLeft"), (1u << 19, "LeftStickRight"),
        (1u << 20, "LeftStickDown"), (1u << 21, "LeftStickUp"),
        (1u << 22, "RightStickLeft"), (1u << 23, "RightStickRight"),
        (1u << 24, "RightStickDown"), (1u << 25, "RightStickUp"),
    ];

    public IEnumerable<string> Observe(GamepadInputState? state)
    {
        if (state is not { } pad)
        {
            var wasConnected = connected;
            connected = false;
            held = 0;
            return wasConnected ? ["Disconnected"] : [];
        }
        var events = new List<string>();
        if (!connected) events.Add("Connected");
        connected = true;
        uint current = (uint)(pad.Buttons & 0xf3ff); // Exclude undefined/reserved bits.
        SetAnalog(16, pad.LeftTrigger, 30, 20);
        SetAnalog(17, pad.RightTrigger, 30, 20);
        SetAnalog(18, -(int)pad.LeftX, 16000, 12000);
        SetAnalog(19, pad.LeftX, 16000, 12000);
        SetAnalog(20, -(int)pad.LeftY, 16000, 12000);
        SetAnalog(21, pad.LeftY, 16000, 12000);
        SetAnalog(22, -(int)pad.RightX, 16000, 12000);
        SetAnalog(23, pad.RightX, 16000, 12000);
        SetAnalog(24, -(int)pad.RightY, 16000, 12000);
        SetAnalog(25, pad.RightY, 16000, 12000);
        var pressed = current & ~held;
        foreach (var (mask, name) in buttons)
            if ((pressed & mask) != 0) events.Add(name);
        held = current;
        return events;

        void SetAnalog(int bit, int value, int press, int release)
        {
            var mask = 1u << bit;
            if (value > ((held & mask) != 0 ? release : press)) current |= mask;
        }
    }
}
