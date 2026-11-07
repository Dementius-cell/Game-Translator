using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace GameTranslator.Infrastructure.Capture;

/// <summary>Copies FP16 WGC surfaces without passing through integer SoftwareBitmap conversion.</summary>
internal sealed class HdrSurfaceReader
{
    private readonly object gate = new();
    private static readonly Guid AccessId = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid TextureId = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    internal byte[] Read(IDirect3DSurface surface, int width, int height, HdrToneMapper mapper)
    {
        // All immediate-context operations owned by this source are serialized, including retired callbacks.
        lock (gate)
        {
            var abi = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
            nint access = 0, texture = 0, device = 0, context = 0, staging = 0;
            try
            {
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(abi, in AccessId, out access));
                Marshal.ThrowExceptionForHR(Method<GetInterface>(access, 3)(access, in TextureId, out texture));
                Method<GetDescription>(texture, 10)(texture, out var description);
                if (description.Format != 10 || description.SampleCount != 1 || width <= 0 || height <= 0
                    || width > description.Width || height > description.Height)
                    throw new InvalidOperationException("Unexpected HDR capture texture format or dimensions.");
                Method<GetPointer>(texture, 3)(texture, out device);
                Method<GetPointer>(device, 40)(device, out context);
                description.Usage = 3; // D3D11_USAGE_STAGING
                description.BindFlags = 0;
                description.CpuAccessFlags = 0x20000; // D3D11_CPU_ACCESS_READ
                description.MiscFlags = 0;
                Marshal.ThrowExceptionForHR(Method<CreateTexture>(device, 5)(device, in description, 0, out staging));
                Method<CopyResource>(context, 47)(context, staging, texture);
                Marshal.ThrowExceptionForHR(Method<Map>(context, 14)(context, staging, 0, 1, 0, out var mapped));
                try
                {
                    var stride = checked(width * 8);
                    if (mapped.RowPitch < stride) throw new InvalidOperationException("Invalid HDR texture row pitch.");
                    var bytes = new byte[checked(stride * height)];
                    for (var y = 0; y < height; y++)
                        Marshal.Copy(mapped.Data + checked(y * (int)mapped.RowPitch), bytes, y * stride, stride);
                    return mapper.Convert(bytes, width, height, stride);
                }
                finally { Method<Unmap>(context, 15)(context, staging, 0); }
            }
            finally
            {
                if (staging != 0) Marshal.Release(staging);
                if (context != 0) Marshal.Release(context);
                if (device != 0) Marshal.Release(device);
                if (texture != 0) Marshal.Release(texture);
                if (access != 0) Marshal.Release(access);
                Marshal.Release(abi);
            }
        }
    }

    // Slots follow the Windows SDK d3d11.h/IDirect3DDxgiInterfaceAccess ABI.
    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    [StructLayout(LayoutKind.Sequential)] private struct TextureDescription
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MappedResource { public nint Data; public uint RowPitch, DepthPitch; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetInterface(nint self, in Guid id, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetPointer(nint self, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetDescription(nint self, out TextureDescription result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture(nint self, in TextureDescription description, nint initialData, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyResource(nint self, nint destination, nint source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Map(nint self, nint resource, uint subresource, uint type, uint flags, out MappedResource mapped);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Unmap(nint self, nint resource, uint subresource);
}
