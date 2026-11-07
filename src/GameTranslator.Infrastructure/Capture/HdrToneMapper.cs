using System.Buffers.Binary;

namespace GameTranslator.Infrastructure.Capture;

/// <summary>Stable, frame-independent scRGB to opaque BGRA for OCR; never adjusts the display.</summary>
internal sealed class HdrToneMapper
{
    private readonly byte[] channel = new byte[65536];

    internal HdrToneMapper(double sdrWhiteNits)
    {
        if (!double.IsFinite(sdrWhiteNits) || sdrWhiteNits < 80 || sdrWhiteNits > 10000)
            throw new ArgumentOutOfRangeException(nameof(sdrWhiteNits));
        for (var i = 0; i < channel.Length; i++)
        {
            var value = (double)BitConverter.UInt16BitsToHalf((ushort)i) * 80 / sdrWhiteNits;
            // Preserve dark/mid-tone contrast; reserve a smooth shoulder for HDR highlights.
            // No per-frame exposure: changing scene brightness cannot make OCR pixels pump.
            var linear = double.IsNaN(value) || value <= 0 ? 0
                : value <= .75 ? value : 1 - .25 / (1 + (value - .75) / .25);
            var srgb = linear <= .0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - .055;
            channel[i] = (byte)Math.Clamp((int)Math.Round(srgb * 255), 0, 255);
        }
    }

    internal byte[] Convert(ReadOnlySpan<byte> rgbaHalf, int width, int height, int sourceStride)
    {
        if (width <= 0 || height <= 0 || sourceStride < checked(width * 8)
            || rgbaHalf.Length < checked(sourceStride * height)) throw new ArgumentException("Invalid HDR frame dimensions.");
        var result = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            var source = rgbaHalf.Slice(y * sourceStride, width * 8);
            var target = result.AsSpan(y * width * 4, width * 4);
            for (int x = 0, offset = 0; x < target.Length; x += 4, offset += 8)
            {
                target[x] = channel[BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset + 4, 2))];
                target[x + 1] = channel[BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset + 2, 2))];
                target[x + 2] = channel[BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2))];
                target[x + 3] = 255;
            }
        }
        return result;
    }
}
