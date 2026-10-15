using System.Reflection;
using System.Runtime.InteropServices;
using GameTranslator.Application.Abstractions;

namespace GameTranslator.UI.Services;

public sealed class WindowsApplicationInfoService : IApplicationInfoService
{
    public string Version => typeof(WindowsApplicationInfoService).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(WindowsApplicationInfoService).Assembly.GetName().Version?.ToString()
        ?? "Unknown";

    public Task<IReadOnlyList<string>> GetDisplayAdaptersAsync() => Task.Run<IReadOnlyList<string>>(() =>
    {
        var adapters = new List<string>();
        for (uint index = 0; ; index++)
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref device, 0))
                break;
            if ((device.StateFlags & 8) == 0 && !string.IsNullOrWhiteSpace(device.Description))
                adapters.Add(device.Description);
        }
        // Windows may expose the same adapter for several logical display outputs.
        return adapters.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    });

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice displayDevice, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
}
