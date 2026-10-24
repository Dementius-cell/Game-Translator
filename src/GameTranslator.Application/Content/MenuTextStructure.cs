namespace GameTranslator.Application.Content;

/// <summary>Conservative evidence for smooth background changes, never a semantic text identity.</summary>
public static class MenuTextStructure
{
    public static bool Matches(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current,
        int width, int height, int stride)
    {
        // Full-resolution comparisons preserve small changes such as a single digit stroke.
        if (width < 3 || height < 3 || (long)width * height > 262144
            || stride < width * 4L || previous.Length != current.Length
            || previous.Length != (long)stride * height) return false;
        var edges = 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = y * stride + x * 4;
            for (var channel = 0; channel < 3; channel++)
                if (Math.Abs(previous[offset + channel] - current[offset + channel]) > 64) return false;
            if (previous[offset + 3] != current[offset + 3]) return false;
            var a = Luma(previous, offset);
            var b = Luma(current, offset);
            if (x + 1 < width && !Compare(a - Luma(previous, offset + 4), b - Luma(current, offset + 4), ref edges)) return false;
            if (y + 1 < height && !Compare(a - Luma(previous, offset + stride), b - Luma(current, offset + stride), ref edges)) return false;
        }
        return edges >= width * height * .01 && edges <= width * height;
    }

    private static int Luma(ReadOnlySpan<byte> pixels, int offset) =>
        (pixels[offset] * 29 + pixels[offset + 1] * 150 + pixels[offset + 2] * 77) >> 8;

    private static bool Compare(int previous, int current, ref int edges)
    {
        // Ignore weak, slowly varying background gradients, but compare every strong edge.
        static int Edge(int value) => value >= 48 ? 1 : value <= -48 ? -1 : 0;
        var edge = Edge(previous);
        if (edge != Edge(current)) return false;
        if (edge != 0) edges++;
        return edge != 0 || Math.Abs(previous - current) <= 12;
    }
}
