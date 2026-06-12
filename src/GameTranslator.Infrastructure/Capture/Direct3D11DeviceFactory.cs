using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace GameTranslator.Infrastructure.Capture;

internal static class Direct3D11DeviceFactory
{
    private const uint D3D11SdkVersion = 7;
    private const uint D3D11CreateDeviceBgraSupport = 0x20;
    private static readonly Guid DxgiDeviceInterfaceId = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    public static IDirect3DDevice CreateDevice()
    {
        var result = D3D11CreateDevice(
            nint.Zero,
            D3DDriverType.Hardware,
            nint.Zero,
            D3D11CreateDeviceBgraSupport,
            nint.Zero,
            0,
            D3D11SdkVersion,
            out var d3dDevice,
            out _,
            out var d3dContext);

        if (result < 0)
        {
            result = D3D11CreateDevice(
                nint.Zero,
                D3DDriverType.Warp,
                nint.Zero,
                D3D11CreateDeviceBgraSupport,
                nint.Zero,
                0,
                D3D11SdkVersion,
                out d3dDevice,
                out _,
                out d3dContext);
        }

        Marshal.ThrowExceptionForHR(result);

        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, in DxgiDeviceInterfaceId, out var dxgiDevice));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var graphicsDevice));
                try
                {
                    return MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevice);
                }
                finally
                {
                    Marshal.Release(graphicsDevice);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            Marshal.Release(d3dContext);
            Marshal.Release(d3dDevice);
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        nint adapter,
        D3DDriverType driverType,
        nint software,
        uint flags,
        nint featureLevels,
        uint featureLevelsCount,
        uint sdkVersion,
        out nint device,
        out D3DFeatureLevel featureLevel,
        out nint immediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    private enum D3DDriverType : uint
    {
        Hardware = 1,
        Warp = 5,
    }

    private enum D3DFeatureLevel : uint
    {
    }
}
