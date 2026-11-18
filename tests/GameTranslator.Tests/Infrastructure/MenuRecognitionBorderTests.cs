using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;
using GameTranslator.Infrastructure.Ocr;

namespace GameTranslator.Tests.Infrastructure;

public sealed class MenuRecognitionBorderTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(4, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void TightLine_AddsRelativeMarginAndPreservesSourcePixels(int scale, bool inverted)
    {
        var request = Request(scale, inverted: inverted);
        var before = request.Frame.PixelData.ToArray();
        var border = Assert.IsType<MenuRecognitionBorder>(MenuRecognitionBorder.TryCreate(request));
        Assert.Equal(3 * scale, border.Padding);
        Assert.Equal(request.Frame.Region, border.Frame.Region);
        Assert.Same(request.Frame.Origin, border.Frame.Origin);
        Assert.Equal(request.Frame.CapturedAt, border.Frame.CapturedAt);
        Assert.Equal(request.Frame.IsCachedReuse, border.Frame.IsCachedReuse);
        for (var y = 0; y < request.Frame.Height; y++)
            Assert.Equal(before.AsSpan(y * request.Frame.Stride, request.Frame.Width * 4).ToArray(),
                border.Frame.PixelData.Slice((y + border.Padding) * border.Frame.Stride + border.Padding * 4,
                    request.Frame.Width * 4).ToArray());
        Assert.Equal(before, request.Frame.PixelData.ToArray());
        Assert.Equal(new BoundingBox(0, 0, request.Frame.Width, request.Frame.Height),
            border.ToSource(new(border.Padding, border.Padding, request.Frame.Width, request.Frame.Height)));
        Assert.Null(border.ToSource(new(0, 0, border.Padding, border.Padding)));
        Assert.Equal(new BoundingBox(0, 0, 2, 2), border.ToSource(new(0, 0, border.Padding + 2, border.Padding + 2)));
    }

    [Theory]
    [InlineData(OcrOrientationMode.Auto, ContentLayoutMode.StaticMenu, OcrPreprocessingMode.None)]
    [InlineData(OcrOrientationMode.Vertical, ContentLayoutMode.StaticMenu, OcrPreprocessingMode.None)]
    [InlineData(OcrOrientationMode.Horizontal, ContentLayoutMode.DialogComic, OcrPreprocessingMode.None)]
    [InlineData(OcrOrientationMode.Horizontal, ContentLayoutMode.StaticMenu, OcrPreprocessingMode.Manual)]
    [InlineData(OcrOrientationMode.Horizontal, ContentLayoutMode.StaticMenu, OcrPreprocessingMode.Adaptive)]
    public void OtherRoutes_AreUnchanged(OcrOrientationMode orientation, ContentLayoutMode mode, OcrPreprocessingMode preprocessing)
    {
        Assert.Null(MenuRecognitionBorder.TryCreate(Request(orientation: orientation, mode: mode, preprocessing: preprocessing)));
    }

    [Fact]
    public void ExistingQuietMargin_IsUnchanged() => Assert.Null(MenuRecognitionBorder.TryCreate(Request(quietMargin: true)));

    [Fact]
    public void FlatCrop_IsUnchanged() => Assert.Null(MenuRecognitionBorder.TryCreate(Request(flat: true)));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MissingOrMultipleDetectorMembers_AreUnchanged(int count)
    {
        var original = Request();
        var request = new OcrRequest(original.Frame, "eng", orientationMode: OcrOrientationMode.Horizontal,
            contentLayoutMode: ContentLayoutMode.StaticMenu)
        { DetectorLineBounds = Enumerable.Repeat(new BoundingBox(0, 0, 48, 12), count).ToArray() };
        Assert.Null(MenuRecognitionBorder.TryCreate(request));
    }

    [Fact]
    public void DetectorContextOutsideMember_IsUnchanged()
    {
        var request = new OcrRequest(Request().Frame, "eng", orientationMode: OcrOrientationMode.Horizontal,
            contentLayoutMode: ContentLayoutMode.StaticMenu)
        { DetectorLineBounds = [new(1, 1, 46, 10)] };
        Assert.Null(MenuRecognitionBorder.TryCreate(request));
    }

    private static OcrRequest Request(int scale = 1, bool inverted = false, bool quietMargin = false,
        bool flat = false, OcrOrientationMode orientation = OcrOrientationMode.Horizontal,
        ContentLayoutMode mode = ContentLayoutMode.StaticMenu, OcrPreprocessingMode preprocessing = OcrPreprocessingMode.None)
    {
        var width = 48 * scale;
        var height = 12 * scale;
        var stride = width * 4 + 8; // Exercise row padding as well as packed pixels.
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var ink = !flat && x >= 12 * scale && x < 24 * scale
                && (!quietMargin || y >= scale && y < height - scale);
            var value = (byte)((ink ^ inverted) ? 200 : 30);
            var offset = y * stride + x * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
        var frame = new CapturedFrame(new(0, 0, width, height), width, height, stride, "Bgra32", pixels,
            DateTimeOffset.UnixEpoch, new(Guid.NewGuid(), TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch)
            { ObservedAtTimestamp = TimeSpan.FromMilliseconds(900) }, true);
        return new(frame, "eng", preprocessingSettings: new() { Mode = preprocessing }, orientationMode: orientation,
            contentLayoutMode: mode) { DetectorLineBounds = [new(0, 0, width, height)] };
    }
}
