using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GameTranslator.UI.ViewModels;

namespace GameTranslator.UI.Services;

/// <summary>Read button events delivered to our own window during an opted-in live session.</summary>
internal sealed class LiveButtonRecorder : IDisposable
{
    private readonly HwndSource source;
    private readonly MainViewModel vm;
    private readonly HashSet<ushort> held = new();
    private bool registered;
    private bool disposed;

    internal LiveButtonRecorder(Window window, MainViewModel vm)
    {
        this.vm = vm;
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;
        source.AddHook(HandleMessage);
        vm.PropertyChanged += Changed;
        Update();
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(vm.DiagnosticsEnabled) or nameof(vm.DiagnosticsInput)
            or nameof(vm.DiagnosticsButtons) or nameof(vm.IsLiveTranslationRunning))
        {
            if (source.Dispatcher.CheckAccess()) Update();
            else source.Dispatcher.InvokeAsync(Update);
        }
    }

    private void Update()
    {
        if (disposed || vm.ButtonCaptureActive == registered) return;
        var enable = vm.ButtonCaptureActive;
        // INPUTSINK delivers background events without suppressing normal game/window input.
        if (SetDevices(enable))
        {
            registered = enable;
            if (enable) vm.ButtonDeviceAvailable = true;
            held.Clear();
        }
    }

    private bool SetDevices(bool enable)
    {
        var device = new Device { Page = 1, Usage = 2, Flags = enable ? 0x100u : 1u,
            Target = enable ? source.Handle : IntPtr.Zero };
        var keyboard = device;
        keyboard.Usage = 6;
        return RegisterRawInputDevices([device, keyboard], 2, (uint)Marshal.SizeOf<Device>());
    }

    private IntPtr HandleMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0xff || !registered || !vm.ButtonCaptureActive) return IntPtr.Zero;
        uint size = (uint)Marshal.SizeOf<Packet>();
        var headerSize = (uint)Marshal.SizeOf<Header>();
        var read = GetRawInputData(lParam, 0x10000003, out var packet, ref size, headerSize);
        if (read == uint.MaxValue || read < headerSize + 16) return IntPtr.Zero;
        var data = packet.Data;
        if (packet.Header.Type == 1)
        {
            var name = NavigationName(data.VirtualKey);
            if (name is null) return IntPtr.Zero;
            if ((data.KeyFlags & 1) != 0) held.Remove(data.VirtualKey);
            else if (held.Add(data.VirtualKey)) vm.RecordButton(name);
        }
        else if (packet.Header.Type == 0)
        {
            var flags = data.MouseFlags;
            if ((flags & 1) != 0) vm.RecordButton("MouseLeft");
            if ((flags & 4) != 0) vm.RecordButton("MouseRight");
            if ((flags & 16) != 0) vm.RecordButton("MouseMiddle");
            if ((flags & 64) != 0) vm.RecordButton("MouseX1");
            if ((flags & 256) != 0) vm.RecordButton("MouseX2");
            if ((flags & 1024) != 0) vm.RecordButton(data.Wheel > 0 ? "WheelUp" : "WheelDown");
            if ((flags & 2048) != 0) vm.RecordButton(data.Wheel > 0 ? "WheelRight" : "WheelLeft");
        }
        return IntPtr.Zero;
    }

    private static string? NavigationName(ushort key) => key switch
    {
        8 => "Backspace", 9 => "Tab", 13 => "Enter", 27 => "Escape", 32 => "Space",
        33 => "PageUp", 34 => "PageDown", 35 => "End", 36 => "Home", 37 => "Left",
        38 => "Up", 39 => "Right", 40 => "Down", 45 => "Insert", 46 => "Delete", _ => null
    };

    public void Dispose()
    {
        disposed = true;
        vm.PropertyChanged -= Changed;
        if (registered) SetDevices(false);
        source.RemoveHook(HandleMessage);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Device { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Header { public uint Type, Size; public IntPtr Device, Parameter; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Payload
    {
        [FieldOffset(2)] public ushort KeyFlags;
        [FieldOffset(4)] public ushort MouseFlags;
        [FieldOffset(6)] public short Wheel;
        [FieldOffset(6)] public ushort VirtualKey;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Packet { public Header Header; public Payload Data; }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices([In] Device[] devices, uint count, uint size);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetRawInputData(IntPtr input, uint command, out Packet data, ref uint size, uint headerSize);
}
