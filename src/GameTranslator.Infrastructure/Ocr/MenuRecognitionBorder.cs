using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Infrastructure.Ocr;

// Recognition-only margin: never expands detector, source or overlay geometry.
internal sealed record MenuRecognitionBorder(CapturedFrame Frame, int Padding, int SourceWidth, int SourceHeight)
{
    internal static bool CanPrepareSingleLine(OcrRequest request)
    {
        var frame = request.Frame;
        return request.ContentLayoutMode == ContentLayoutMode.StaticMenu
            && request.OrientationMode == OcrOrientationMode.Horizontal
            && request.LayoutMode != OcrLayoutMode.Comic
            && request.PreprocessingSettings.ResolveMode() == OcrPreprocessingMode.None
            && request.DetectorLineBounds.Count == 1
            && request.DetectorLineBounds[0] == new BoundingBox(0, 0, frame.Width, frame.Height)
            && frame.PixelFormat == "Bgra32" && frame.Stride >= (long)frame.Width * 4
            && frame.Width >= (long)frame.Height * 3 && (long)frame.Width * frame.Height <= 1_000_000;
    }

    internal static MenuRecognitionBorder? TryCreate(OcrRequest request, CapturedFrame? preparedFrame = null)
    {
        if (!CanPrepareSingleLine(request)) return null;
        var frame = preparedFrame ?? request.Frame;

        var histogram = new int[256];
        var edgeHistogram = new int[256];
        var edges = 0;
        var source = frame.PixelData.Span;
        for (var y = 0; y < frame.Height; y++)
        for (var x = 0; x < frame.Width; x++)
        {
            var i = y * frame.Stride + x * 4;
            var gray = (source[i] * 29 + source[i + 1] * 150 + source[i + 2] * 77 + 128) >> 8;
            histogram[gray]++;
            if (x == 0 || y == 0 || x == frame.Width - 1 || y == frame.Height - 1)
            {
                edgeHistogram[gray]++;
                edges++;
            }
        }
        var count = frame.Width * frame.Height;
        var low = Quantile(histogram, count, .1);
        var high = Quantile(histogram, count, .9);
        if (high <= low) return null;
        var background = Quantile(edgeHistogram, edges, .5);
        var midpoint = (low + high) / 2d;
        var touchesEdge = Enumerable.Range(0, 256).Any(value => edgeHistogram[value] > 0
            && (background > midpoint ? value < midpoint : value > midpoint));
        // Already separated text must keep the original segmentation behaviour.
        if (!touchesEdge) return null;

        var padding = (int)Math.Ceiling(frame.Height * .25);
        var width = checked(frame.Width + padding * 2);
        var height = checked(frame.Height + padding * 2);
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = background;
            pixels[i + 3] = 255;
        }
        for (var y = 0; y < frame.Height; y++)
            source.Slice(y * frame.Stride, frame.Width * 4)
                .CopyTo(pixels.AsSpan((y + padding) * stride + padding * 4));
        return new(new CapturedFrame(frame.Region, width, height, stride, frame.PixelFormat,
            pixels, frame.CapturedAt, frame.Origin, frame.IsCachedReuse), padding, frame.Width, frame.Height);
    }

    internal BoundingBox? ToSource(BoundingBox bounds)
    {
        var left = Math.Max(0, bounds.X - Padding);
        var top = Math.Max(0, bounds.Y - Padding);
        var right = Math.Min(SourceWidth, bounds.Right - Padding);
        var bottom = Math.Min(SourceHeight, bounds.Bottom - Padding);
        return right > left && bottom > top ? new(left, top, right - left, bottom - top) : null;
    }

    private static byte Quantile(int[] histogram, int count, double fraction)
    {
        var rank = (int)Math.Ceiling(count * fraction);
        for (var i = 0; i < histogram.Length; i++)
        {
            rank -= histogram[i];
            if (rank <= 0) return (byte)i;
        }
        return 255;
    }
}
