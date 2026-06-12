using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace GameTranslator.Infrastructure.Capture;

internal static class GraphicsCaptureItemFactory
{
    private const uint MonitorDefaultToPrimary = 1;
    private const string GraphicsCaptureItemRuntimeClassName = "Windows.Graphics.Capture.GraphicsCaptureItem";
    private static readonly Guid GraphicsCaptureItemInterfaceId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid GraphicsCaptureItemInteropInterfaceId = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    public static GraphicsCaptureItem CreateForPrimaryMonitor()
    {
        var monitor = MonitorFromPoint(new Point(0, 0), MonitorDefaultToPrimary);
        if (monitor == nint.Zero)
        {
            throw new InvalidOperationException("Primary monitor handle could not be resolved.");
        }

        var itemInterop = CreateInteropFactory();
        var itemPointer = itemInterop.CreateForMonitor(monitor, in GraphicsCaptureItemInterfaceId);
        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    private static IGraphicsCaptureItemInterop CreateInteropFactory()
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(
            GraphicsCaptureItemRuntimeClassName,
            GraphicsCaptureItemRuntimeClassName.Length,
            out var runtimeClassName));
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(
                runtimeClassName,
                in GraphicsCaptureItemInteropInterfaceId,
                out var factoryPointer));
            try
            {
                return (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPointer);
            }
            finally
            {
                Marshal.Release(factoryPointer);
            }
        }
        finally
        {
            WindowsDeleteString(runtimeClassName);
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(nint activatableClassId, in Guid iid, out nint factory);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out nint hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(nint hstring);

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);

        nint CreateForMonitor(nint monitor, in Guid iid);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Point
    {
        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }

        public int Y { get; }
    }
}
