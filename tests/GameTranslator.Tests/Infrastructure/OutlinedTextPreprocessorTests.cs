using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;
using GameTranslator.Infrastructure.Ocr;

namespace GameTranslator.Tests.Infrastructure;

public sealed class OutlinedTextPreprocessorTests
{
    [Theory]
    [InlineData(190, 40, 50)]
    [InlineData(40, 60, 200)]
    [InlineData(40, 180, 60)]
    public void DetachedGlyphs_RemoveEnclosingOutlineWithoutColorAssumptions(byte red, byte green, byte blue)
    {
        var frame = CreateFrame(red, green, blue);
        var original = frame.PixelData.ToArray();
        var result = OutlinedTextPreprocessor.TryPrepare(frame);
        Assert.NotNull(result);
        Assert.Equal(frame.Region, result.Region);
        Assert.Equal(frame.CapturedAt, result.CapturedAt);
        Assert.Equal(frame.Width, result.Width);
        Assert.Equal(frame.Height, result.Height);
        Assert.Equal(255, Pixel(result, 3, 4)); // The wide enclosing outline disappears.
        Assert.Equal(0, Pixel(result, 20, 35)); // Detached glyph remains at the same position.
        Assert.Equal(0, Pixel(result, 24, 18)); // Detached punctuation/accent remains too.
        Assert.Equal(255, Pixel(result, 10, 35)); // Interior background remains white.
        Assert.Equal(original, frame.PixelData.ToArray());
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 2)]
    public void OrdinaryColoredTextOrInsufficientGlyphEvidence_DoesNotCreateRecovery(bool outline, int glyphs)
    {
        Assert.Null(OutlinedTextPreprocessor.TryPrepare(CreateFrame(190, 40, 50, outline, glyphs)));
    }

    [Fact]
    public void MonochromeText_DoesNotCreateRecovery() =>
        Assert.Null(OutlinedTextPreprocessor.TryPrepare(CreateFrame(0, 0, 0)));

    [Fact]
    public void Cancellation_IsObservedBeforePixelWork()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            OutlinedTextPreprocessor.TryPrepare(CreateFrame(190, 40, 50), cancellation.Token));
    }

    [Theory]
    [InlineData(50, 80)]
    [InlineData(200, 20)]
    [InlineData(80, 200)]
    [InlineData(2000, 600)]
    public void UnsupportedGeometry_IsNotProcessed(int width, int height)
    {
        var frame = new CapturedFrame(new CaptureRegion(0, 0, width, height), width, height,
            width * 4, "Bgra32", new byte[width * height * 4], DateTimeOffset.UtcNow);
        Assert.Null(OutlinedTextPreprocessor.TryPrepare(frame));
    }

    [Theory]
    [InlineData(.3, .96, true)]
    [InlineData(.3, .89, false)]
    [InlineData(.94, .95, false)]
    [InlineData(.95, .90, false)]
    [InlineData(double.NaN, .99, false)]
    [InlineData(.1, double.PositiveInfinity, false)]
    public void Replacement_RequiresHighAndImprovedConfidence(double original, double recovered, bool accepted) =>
        Assert.Equal(accepted, TesseractOcrEngine.IsOutlinedRecoveryConfidenceAccepted(original, recovered));

    [Fact]
    public void NativeFloatConfidence_AtNinetyPercentIsAccepted() =>
        Assert.True(TesseractOcrEngine.IsOutlinedRecoveryConfidenceAccepted(.3, .90f));

    [Theory]
    [InlineData(OcrOrientationMode.Vertical, .3)]
    [InlineData(OcrOrientationMode.Auto, .3)]
    [InlineData(OcrOrientationMode.Horizontal, .95)]
    [InlineData(OcrOrientationMode.Horizontal, double.NaN)]
    public void IneligibleRecognition_DoesNotRetry(OcrOrientationMode orientation, double confidence)
    {
        var request = new OcrRequest(CreateFrame(190, 40, 50), "eng")
        {
            DetectorLineBounds = new[] { new BoundingBox(0, 0, 200, 80) },
        };
        Assert.Empty(TesseractOcrEngine.ResolveOutlinedDetectorLines(request, orientation, confidence));
    }

    [Fact]
    public void LegacyRequestWithoutDetectorHints_DoesNotRetry() =>
        Assert.Empty(TesseractOcrEngine.ResolveOutlinedDetectorLines(
            new OcrRequest(CreateFrame(190, 40, 50), "eng"), OcrOrientationMode.Horizontal, .3));

    [Fact]
    public void DetectorLines_PreserveBoundsAndUseTopToBottomOrder()
    {
        var first = new BoundingBox(0, 0, 190, 40);
        var second = new BoundingBox(10, 35, 100, 40);
        var request = new OcrRequest(CreateFrame(190, 40, 50), "eng")
        {
            DetectorLineBounds = new[] { second, first },
        };
        Assert.Equal(new[] { first, second },
            TesseractOcrEngine.ResolveOutlinedDetectorLines(request, OcrOrientationMode.Horizontal, .3));
        Assert.Equal(new[] { second, first }, request.DetectorLineBounds);
    }

    [Fact]
    public void StronglyOverlappingDetectorLines_DoNotRetry()
    {
        var request = new OcrRequest(CreateFrame(190, 40, 50), "eng")
        {
            DetectorLineBounds = new[] { new BoundingBox(0, 0, 190, 50), new BoundingBox(0, 30, 100, 50) },
        };
        Assert.Empty(TesseractOcrEngine.ResolveOutlinedDetectorLines(request, OcrOrientationMode.Horizontal, .3));
    }

    private static byte Pixel(CapturedFrame frame, int x, int y) => frame.PixelData.Span[y * frame.Stride + x * 4];

    private static CapturedFrame CreateFrame(byte red, byte green, byte blue, bool outline = true, int glyphs = 4)
    {
        const int width = 200, height = 80, stride = width * 4 + 12;
        var pixels = Enumerable.Repeat((byte)255, stride * height).ToArray();
        if (outline)
        {
            Paint(3, 4, 194, 2);
            Paint(3, 74, 194, 2);
            Paint(3, 4, 2, 72);
            Paint(195, 4, 2, 72);
        }
        for (var index = 0; index < glyphs; index++) Paint(20 + index * 45, 30, 20, 30);
        Paint(23, 17, 3, 3);
        return new CapturedFrame(new CaptureRegion(100, 200, width, height), width, height,
            stride, "Bgra32", pixels, DateTimeOffset.UtcNow);

        void Paint(int left, int top, int rectangleWidth, int rectangleHeight)
        {
            for (var y = top; y < top + rectangleHeight; y++)
            for (var x = left; x < left + rectangleWidth; x++)
            {
                var offset = y * stride + x * 4;
                pixels[offset] = blue;
                pixels[offset + 1] = green;
                pixels[offset + 2] = red;
            }
        }
    }
}
