using System.Runtime.InteropServices;

namespace GameTranslator.Infrastructure.Capture;

/// <summary>Read-only active-output queries. Never changes Windows HDR/display settings.</summary>
internal static class MonitorColorInfoReader
{
    internal static CaptureColorInfo Read(nint monitor)
    {
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo)) return CaptureColorInfo.Unknown;
        // The topology may change between sizing and querying; retry only a bounded number of times.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (GetDisplayConfigBufferSizes(2, out var pathCount, out var modeCount) != 0)
                return CaptureColorInfo.Unknown;
            if (pathCount > 1024 || modeCount > 4096) return CaptureColorInfo.Unknown;
            var paths = new PathInfo[pathCount];
            var modes = new ModeInfo[modeCount];
            var result = QueryDisplayConfig(2, ref pathCount, paths, ref modeCount, modes, nint.Zero);
            if (result == 122) continue; // ERROR_INSUFFICIENT_BUFFER
            if (result != 0) return CaptureColorInfo.Unknown;
            for (var i = 0; i < pathCount; i++)
            {
                var path = paths[i];
                var name = new SourceName { Header = Header.For<SourceName>(1, path.Source.Adapter, path.Source.Id) };
                if (GetSourceName(ref name) != 0 || !string.Equals(name.Name, monitorInfo.Device, StringComparison.OrdinalIgnoreCase)) continue;
                var white = new WhiteLevel { Header = Header.For<WhiteLevel>(11, path.Target.Adapter, path.Target.Id) };
                uint? whiteLevel = GetWhiteLevel(ref white) == 0 ? white.Value : null;
                var info2 = new ColorInfo2 { Header = Header.For<ColorInfo2>(15, path.Target.Adapter, path.Target.Id) };
                if (GetColorInfo2(ref info2) == 0)
                    return CaptureColorInfo.FromAdvancedColor(info2.Flags, info2.ActiveMode, whiteLevel);
                var info = new ColorInfo { Header = Header.For<ColorInfo>(9, path.Target.Adapter, path.Target.Id) };
                if (GetColorInfo(ref info) == 0)
                    return CaptureColorInfo.FromAdvancedColor(info.Flags, null, whiteLevel);
            }
            return CaptureColorInfo.Unknown;
        }
        return CaptureColorInfo.Unknown;
    }

    [StructLayout(LayoutKind.Sequential)] internal struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] internal struct Header
    {
        public uint Type, Size;
        public Luid Adapter;
        public uint Id;
        public static Header For<T>(uint type, Luid adapter, uint id) => new() { Type = type, Size = (uint)Marshal.SizeOf<T>(), Adapter = adapter, Id = id };
    }
    [StructLayout(LayoutKind.Sequential)] internal struct SourceInfo { public Luid Adapter; public uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)] internal struct TargetInfo
    {
        public Luid Adapter;
        public uint Id, ModeIndex, Technology, Rotation, Scaling, RefreshNumerator, RefreshDenominator, ScanLine, Available, Status;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct PathInfo { public SourceInfo Source; public TargetInfo Target; public uint Flags; }
    // DISPLAYCONFIG_MODE_INFO's unused 48-byte union follows its 16-byte header.
    [StructLayout(LayoutKind.Explicit, Size = 64)] internal struct ModeInfo { [FieldOffset(0)] public uint Type; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct SourceName
    {
        public Header Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct ColorInfo { public Header Header; public uint Flags, Encoding, Bits; }
    [StructLayout(LayoutKind.Sequential)] internal struct ColorInfo2 { public Header Header; public uint Flags, Encoding, Bits, ActiveMode; }
    [StructLayout(LayoutKind.Sequential)] internal struct WhiteLevel { public Header Header; public uint Value; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    {
        public int Size, Left, Top, Right, Bottom, WorkLeft, WorkTop, WorkRight, WorkBottom;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths, ref uint modeCount, [Out] ModeInfo[] modes, nint topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetSourceName(ref SourceName packet);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetColorInfo(ref ColorInfo packet);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetColorInfo2(ref ColorInfo2 packet);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetWhiteLevel(ref WhiteLevel packet);
}
