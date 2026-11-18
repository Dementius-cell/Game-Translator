using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;
using GameTranslator.Infrastructure.Ocr;

namespace GameTranslator.Tests.Infrastructure;

public sealed class MenuChromaticTextureTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    public void NeutralStrokesOnSmallColoredTexture_PreserveInkCoordinatesAndProvenance(int scale, int colorAxis)
    {
        var request = Request(scale, colorAxis: colorAxis);
        var before = request.Frame.PixelData.ToArray();
        var frame = Assert.IsType<CapturedFrame>(MenuChromaticTexture.TryCreate(request));
        Assert.Equal(request.Frame.Region, frame.Region);
        Assert.Equal(request.Frame.Width, frame.Width);
        Assert.Equal(request.Frame.Height, frame.Height);
        Assert.Equal(request.Frame.Stride, frame.Stride);
        Assert.Same(request.Frame.Origin, frame.Origin);
        Assert.Equal(request.Frame.CapturedAt, frame.CapturedAt);
        Assert.Equal(request.Frame.IsCachedReuse, frame.IsCachedReuse);
        var after = frame.PixelData.Span;
        for (var y = 0; y < frame.Height; y++)
        for (var x = 0; x < frame.Width; x++)
        {
            var i = y * frame.Stride + x * 4;
            Assert.Equal(Math.Min(before[i], Math.Min(before[i + 1], before[i + 2])), after[i]);
            Assert.Equal(after[i], after[i + 1]);
            Assert.Equal(after[i], after[i + 2]);
            Assert.Equal(before[i + 3], after[i + 3]);
        }
        Assert.Equal(before, request.Frame.PixelData.ToArray());
    }

    [Theory]
    [InlineData("colored-stroke")]
    [InlineData("colored-punctuation")]
    [InlineData("monochrome")]
    [InlineData("light-background")]
    [InlineData("no-neutral-ink")]
    [InlineData("sparse")]
    public void UnsupportedOrPotentiallySemanticColor_IsNotSuppressed(string variant)
        => Assert.Null(MenuChromaticTexture.TryCreate(Request(variant: variant)));

    [Theory]
    [InlineData(OcrOrientationMode.Auto, ContentLayoutMode.StaticMenu, OcrPreprocessingMode.None)]
    [InlineData(OcrOrientationMode.Vertical, ContentLayoutMode.StaticMenu, OcrPreprocessingMode.None)]
    [InlineData(OcrOrientationMode.Horizontal, ContentLayoutMode.DialogComic, OcrPreprocessingMode.None)]
    [InlineData(OcrOrientationMode.Horizontal, ContentLayoutMode.StaticMenu, OcrPreprocessingMode.Manual)]
    [InlineData(OcrOrientationMode.Horizontal, ContentLayoutMode.StaticMenu, OcrPreprocessingMode.Adaptive)]
    public void OtherRoutes_AreUnchanged(OcrOrientationMode orientation, ContentLayoutMode mode, OcrPreprocessingMode preprocessing)
    {
        var original = Request();
        var request = new OcrRequest(original.Frame, "eng", orientationMode: orientation, contentLayoutMode: mode,
            preprocessingSettings: new() { Mode = preprocessing })
        { DetectorLineBounds = original.DetectorLineBounds };
        Assert.Null(MenuChromaticTexture.TryCreate(request));
    }

    [Fact]
    public void Cancellation_IsPreserved()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => MenuChromaticTexture.TryCreate(Request(), cancellation.Token));
    }

    private static OcrRequest Request(int scale = 1, string variant = "normal", int colorAxis = 0)
    {
        var width = 160 * scale;
        var height = 40 * scale;
        var stride = width * 4 + 8;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var xx = x / scale;
            var yy = y / scale;
            var i = y * stride + x * 4;
            var background = (byte)(variant == "light-background" ? 225 : 10);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = background;
            pixels[i + 3] = 255;
            if (xx % 7 is >= 2 and <= 4 && yy % 7 is >= 2 and <= 4
                && (variant != "sparse" || xx < 7 && yy < 7))
            {
                pixels[i] = 80;
                pixels[i + 1] = variant == "monochrome" ? (byte)80 : (byte)55;
                pixels[i + 2] = variant == "monochrome" ? (byte)80 : (byte)10;
            }
            if (variant != "no-neutral-ink" && xx is >= 16 and <= 140 && xx % 14 < 3 && yy is >= 9 and <= 31)
                pixels[i] = pixels[i + 1] = pixels[i + 2] = 160;
            if ((variant == "colored-stroke" && xx is >= 80 and <= 82 && yy is >= 8 and <= 32)
                || (variant == "colored-punctuation" && xx is >= 80 and <= 82 && yy is >= 28 and <= 30))
            {
                pixels[i] = variant == "colored-punctuation" ? (byte)230 : (byte)80;
                pixels[i + 1] = 55;
                pixels[i + 2] = 10;
            }
        }
        if (colorAxis != 0)
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = y * stride + x * 4;
                (pixels[i], pixels[i + colorAxis]) = (pixels[i + colorAxis], pixels[i]);
            }
        var origin = new CaptureFrameOrigin(Guid.NewGuid(), TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch);
        var frame = new CapturedFrame(new(7, 9, width, height), width, height, stride, "Bgra32", pixels,
            DateTimeOffset.UnixEpoch, origin, true);
        return new(frame, "eng", orientationMode: OcrOrientationMode.Horizontal, contentLayoutMode: ContentLayoutMode.StaticMenu)
        { DetectorLineBounds = [new(0, 0, width, height)] };
    }
}
