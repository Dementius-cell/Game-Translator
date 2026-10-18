namespace GameTranslator.Application.Ocr;

// Local threshold T = mean * (1 + k * (stddev / R - 1)).
// A 31-pixel window, k=0.2 and R=128 are fixed, deterministic defaults.
internal static class SauvolaThreshold
{
    internal const int MaximumPixels = 4_000_000;

    public static void Apply(byte[] pixels, int width, int height, int stride, CancellationToken token)
    {
        if ((long)width * height > MaximumPixels)
            throw new OcrEngineException("Adaptive thresholding supports OCR crops up to 4 million pixels.");
        var pitch = width + 1;
        var sums = new double[checked(pitch * (height + 1))];
        var squares = new double[sums.Length];
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            double sum = 0, square = 0;
            for (var x = 0; x < width; x++)
            {
                var offset = y * stride + x * 4;
                var value = pixels[offset] * .0722 + pixels[offset + 1] * .7152 + pixels[offset + 2] * .2126;
                sum += value;
                square += value * value;
                var index = (y + 1) * pitch + x + 1;
                sums[index] = sums[index - pitch] + sum;
                squares[index] = squares[index - pitch] + square;
            }
        }
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            var top = Math.Max(0, y - 15);
            var bottom = Math.Min(height, y + 16);
            for (var x = 0; x < width; x++)
            {
                var left = Math.Max(0, x - 15);
                var right = Math.Min(width, x + 16);
                var count = (right - left) * (bottom - top);
                double Area(double[] table) => table[bottom * pitch + right] - table[top * pitch + right]
                    - table[bottom * pitch + left] + table[top * pitch + left];
                var mean = Area(sums) / count;
                var deviation = Math.Sqrt(Math.Max(0, Area(squares) / count - mean * mean));
                var threshold = mean * (1 + .2 * (deviation / 128 - 1));
                var offset = y * stride + x * 4;
                var luminance = pixels[offset] * .0722 + pixels[offset + 1] * .7152 + pixels[offset + 2] * .2126;
                var value = luminance <= threshold ? (byte)0 : (byte)255;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
            }
        }
    }
}
