using System.ComponentModel;
using System.Runtime.InteropServices;
using GameTranslator.Application.Debug;

namespace GameTranslator.UI.Services;

internal sealed class XInputGamepadInputSource : IGamepadInputSource
{
    public GamepadInputState? Read(int slot)
    {
        var error = XInputGetState((uint)slot, out var state);
        if (error == 1167) return null; // ERROR_DEVICE_NOT_CONNECTED
        if (error != 0) throw new Win32Exception((int)error);
        var pad = state.Gamepad;
        return new(pad.Buttons, pad.LeftTrigger, pad.RightTrigger, pad.LeftX, pad.LeftY, pad.RightX, pad.RightY);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeState { public uint PacketNumber; public NativeGamepad Gamepad; }

    [DllImport("xinput1_4.dll", ExactSpelling = true)]
    private static extern uint XInputGetState(uint userIndex, out NativeState state);
}
