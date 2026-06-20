using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GameTranslator.Application.Hotkeys;

namespace GameTranslator.UI.Services;

public sealed class WpfGlobalHotkeyRegistrar : IGlobalHotkeyRegistrar, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private readonly HashSet<int> registeredIds = new();
    private HwndSource? source;
    private nint hwnd;
    private bool isDisposed;

    public event EventHandler<GlobalHotkeyRegisteredEventArgs>? HotkeyPressed;

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        hwnd = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(OnWindowMessage);
    }

    public GlobalHotkeyRegistrationResult Register(GlobalHotkeyRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (hwnd == nint.Zero)
        {
            return GlobalHotkeyRegistrationResult.Failure("Main window handle is not ready.");
        }

        if (!TryGetVirtualKeyCode(registration.Gesture.Key, out var virtualKeyCode))
        {
            return GlobalHotkeyRegistrationResult.Failure($"Unsupported hotkey key '{registration.Gesture.Key}'.");
        }

        var modifiers = (uint)registration.Gesture.Modifiers;
        if (registration.Gesture.NoRepeat)
        {
            modifiers |= ModNoRepeat;
        }

        if (!RegisterHotKey(hwnd, registration.Id, modifiers, virtualKeyCode))
        {
            var errorCode = Marshal.GetLastWin32Error();
            return GlobalHotkeyRegistrationResult.Failure(
                $"Hotkey {registration.Gesture.DisplayText} is already in use or reserved by Windows.",
                errorCode);
        }

        registeredIds.Add(registration.Id);
        return GlobalHotkeyRegistrationResult.Success();
    }

    public void Unregister(int id)
    {
        if (hwnd == nint.Zero || !registeredIds.Remove(id))
        {
            return;
        }

        if (!UnregisterHotKey(hwnd, id))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Global hotkey id {id} could not be unregistered.");
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in registeredIds.ToArray())
        {
            Unregister(id);
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        UnregisterAll();
        source?.RemoveHook(OnWindowMessage);
        source = null;
        hwnd = nint.Zero;
        isDisposed = true;
    }

    private nint OnWindowMessage(nint messageHwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey)
        {
            HotkeyPressed?.Invoke(this, new GlobalHotkeyRegisteredEventArgs(wParam.ToInt32()));
            handled = true;
        }

        return nint.Zero;
    }

    private static bool TryGetVirtualKeyCode(string key, out uint virtualKeyCode)
    {
        virtualKeyCode = key.ToUpperInvariant() switch
        {
            "A" => 0x41,
            "B" => 0x42,
            "C" => 0x43,
            "D" => 0x44,
            "E" => 0x45,
            "F" => 0x46,
            "G" => 0x47,
            "H" => 0x48,
            "I" => 0x49,
            "J" => 0x4A,
            "K" => 0x4B,
            "L" => 0x4C,
            "M" => 0x4D,
            "N" => 0x4E,
            "O" => 0x4F,
            "P" => 0x50,
            "Q" => 0x51,
            "R" => 0x52,
            "S" => 0x53,
            "T" => 0x54,
            "U" => 0x55,
            "V" => 0x56,
            "W" => 0x57,
            "X" => 0x58,
            "Y" => 0x59,
            "Z" => 0x5A,
            "0" => 0x30,
            "1" => 0x31,
            "2" => 0x32,
            "3" => 0x33,
            "4" => 0x34,
            "5" => 0x35,
            "6" => 0x36,
            "7" => 0x37,
            "8" => 0x38,
            "9" => 0x39,
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            "F13" => 0x7C,
            "F14" => 0x7D,
            "F15" => 0x7E,
            "F16" => 0x7F,
            "F17" => 0x80,
            "F18" => 0x81,
            "F19" => 0x82,
            "F20" => 0x83,
            "F21" => 0x84,
            "F22" => 0x85,
            "F23" => 0x86,
            "F24" => 0x87,
            "Escape" => 0x1B,
            "Space" => 0x20,
            "Enter" => 0x0D,
            "Tab" => 0x09,
            "Home" => 0x24,
            "End" => 0x23,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            _ => 0,
        };

        return virtualKeyCode != 0 && virtualKeyCode != 0x7B;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
