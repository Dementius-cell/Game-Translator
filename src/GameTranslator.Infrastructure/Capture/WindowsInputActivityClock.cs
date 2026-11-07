using System.Runtime.InteropServices;
using GameTranslator.Application.Capture;

namespace GameTranslator.Infrastructure.Capture;

internal sealed class WindowsInputActivityClock(GameTranslator.Application.Settings.LiveDiagnosticsPreferences preferences) : IInputActivityClock
{
    public int? ReadAgeMilliseconds()
    {
        if (!preferences.Current.Enabled || !preferences.Current.Input) return null;
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return null;
        // LASTINPUTINFO uses the low 32 bits of the system tick counter.
        var elapsed = unchecked((uint)Environment.TickCount - info.Time);
        return elapsed <= int.MaxValue ? (int)elapsed : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
