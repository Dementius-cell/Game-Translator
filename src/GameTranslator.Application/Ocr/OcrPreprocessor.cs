using GameTranslator.Application.Capture;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Ocr;

public sealed class OcrPreprocessor
{
    public CapturedFrame Apply(CapturedFrame frame, OcrPreprocessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsEnabled)
        {
            return frame;
        }

        if (!string.Equals(frame.PixelFormat, "Bgra32", StringComparison.OrdinalIgnoreCase))
        {
            return frame;
        }

        var pixels = frame.PixelData.ToArray();
        ApplyBrightnessContrast(pixels, frame.Stride, frame.Width, frame.Height, settings.Brightness, settings.Contrast);

        if (settings.NoiseReductionEnabled)
        {
            pixels = ApplyNoiseReduction(pixels, frame.Stride, frame.Width, frame.Height);
        }

        if (settings.Sharpness > 0)
        {
            pixels = ApplySharpness(pixels, frame.Stride, frame.Width, frame.Height, settings.Sharpness);
        }

        if (settings.ThresholdingEnabled)
        {
            ApplyThreshold(pixels, frame.Stride, frame.Width, frame.Height, settings.Threshold);
        }

        if (Math.Abs(settings.Scale - 1) > 0.001)
        {
            return ScaleFrame(frame, pixels, settings.Scale);
        }

        return new CapturedFrame(
            frame.Region,
            frame.Width,
            frame.Height,
            frame.Stride,
            frame.PixelFormat,
            pixels,
            frame.CapturedAt);
    }

    private static void ApplyBrightnessContrast(byte[] pixels, int stride, int width, int height, int brightness, double contrast)
    {
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var offset = row + x * 4;
                pixels[offset] = AdjustChannel(pixels[offset], brightness, contrast);
                pixels[offset + 1] = AdjustChannel(pixels[offset + 1], brightness, contrast);
                pixels[offset + 2] = AdjustChannel(pixels[offset + 2], brightness, contrast);
            }
        }
    }

    private static byte[] ApplyNoiseReduction(byte[] pixels, int stride, int width, int height)
    {
        var output = pixels.ToArray();
        if (width < 3 || height < 3)
        {
            return output;
        }

        Span<byte> values = stackalloc byte[9];
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var offset = y * stride + x * 4;
                for (var channel = 0; channel < 3; channel++)
                {
                    var index = 0;
                    for (var sampleY = y - 1; sampleY <= y + 1; sampleY++)
                    {
                        for (var sampleX = x - 1; sampleX <= x + 1; sampleX++)
                        {
                            values[index++] = pixels[sampleY * stride + sampleX * 4 + channel];
                        }
                    }

                    values.Sort();
                    output[offset + channel] = values[4];
                }
            }
        }

        return output;
    }

    private static byte[] ApplySharpness(byte[] pixels, int stride, int width, int height, double amount)
    {
        var output = pixels.ToArray();
        if (width < 3 || height < 3)
        {
            return output;
        }

        var clampedAmount = Math.Clamp(amount, 0, 2);
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var offset = y * stride + x * 4;
                for (var channel = 0; channel < 3; channel++)
                {
                    var center = pixels[offset + channel];
                    var left = pixels[y * stride + (x - 1) * 4 + channel];
                    var right = pixels[y * stride + (x + 1) * 4 + channel];
                    var top = pixels[(y - 1) * stride + x * 4 + channel];
                    var bottom = pixels[(y + 1) * stride + x * 4 + channel];
                    var blurred = (left + right + top + bottom) / 4d;
                    output[offset + channel] = ClampToByte(center + (center - blurred) * clampedAmount);
                }
            }
        }

        return output;
    }

    private static void ApplyThreshold(byte[] pixels, int stride, int width, int height, byte threshold)
    {
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var offset = row + x * 4;
                var luminance = (pixels[offset + 2] * 0.2126) + (pixels[offset + 1] * 0.7152) + (pixels[offset] * 0.0722);
                var value = luminance >= threshold ? (byte)255 : (byte)0;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
            }
        }
    }

    private static CapturedFrame ScaleFrame(CapturedFrame source, byte[] pixels, double scale)
    {
        var scaledWidth = Math.Max(1, (int)Math.Round(source.Width * scale, MidpointRounding.AwayFromZero));
        var scaledHeight = Math.Max(1, (int)Math.Round(source.Height * scale, MidpointRounding.AwayFromZero));
        var scaledStride = checked(scaledWidth * 4);
        var scaledPixels = new byte[checked(scaledStride * scaledHeight)];

        for (var y = 0; y < scaledHeight; y++)
        {
            var sourceY = Math.Min(source.Height - 1, (int)(y / scale));
            for (var x = 0; x < scaledWidth; x++)
            {
                var sourceX = Math.Min(source.Width - 1, (int)(x / scale));
                var sourceOffset = sourceY * source.Stride + sourceX * 4;
                var targetOffset = y * scaledStride + x * 4;
                scaledPixels[targetOffset] = pixels[sourceOffset];
                scaledPixels[targetOffset + 1] = pixels[sourceOffset + 1];
                scaledPixels[targetOffset + 2] = pixels[sourceOffset + 2];
                scaledPixels[targetOffset + 3] = pixels[sourceOffset + 3];
            }
        }

        return new CapturedFrame(
            source.Region,
            scaledWidth,
            scaledHeight,
            scaledStride,
            source.PixelFormat,
            scaledPixels,
            source.CapturedAt);
    }

    private static byte AdjustChannel(byte value, int brightness, double contrast)
    {
        return ClampToByte(((value - 128) * contrast) + 128 + brightness);
    }

    private static byte ClampToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), byte.MinValue, byte.MaxValue);
    }
}