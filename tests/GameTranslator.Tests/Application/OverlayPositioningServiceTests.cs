using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class OverlayPositioningServiceTests
{
    private static readonly DateTimeOffset FrameTime = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ShownAt = new(2026, 6, 17, 12, 0, 1, TimeSpan.Zero);

    [Fact]
    public void CreateSnapshot_WhenFrameMatchesRegion_AppliesRegionOffset()
    {
        var service = new OverlayPositioningService();
        var result = CreateResult(
            new CaptureRegion(10, 20, 100, 40),
            inputWidth: 100,
            inputHeight: 40,
            new OcrTextBlock("Start", new BoundingBox(4, 5, 24, 10)));

        var snapshot = service.CreateSnapshot(result, ShownAt);

        var item = Assert.Single(snapshot.TextItems);
        Assert.Equal(ShownAt, snapshot.ShownAt);
        Assert.Equal("Start", item.Text);
        Assert.Equal(14, item.X);
        Assert.Equal(25, item.Y);
        Assert.Equal(24, item.Width);
        Assert.Equal(10, item.Height);

        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal(OverlayMaskMode.Solid, mask.Mode);
        Assert.Equal("#000000", mask.Color);
        Assert.Equal(1, mask.Opacity);
        Assert.Equal(item.X, mask.X);
        Assert.Equal(item.Y, mask.Y);
        Assert.Equal(item.Width, mask.Width);
        Assert.Equal(item.Height, mask.Height);
    }

    [Fact]
    public void CreateSnapshot_WhenOcrInputIsScaled_MapsBoundsIntoCaptureRegionSpace()
    {
        var service = new OverlayPositioningService();
        var result = CreateResult(
            new CaptureRegion(100, 200, 200, 80),
            inputWidth: 100,
            inputHeight: 40,
            new OcrTextBlock("Scaled", new BoundingBox(10, 5, 20, 10)));

        var snapshot = service.CreateSnapshot(result, ShownAt);

        var item = Assert.Single(snapshot.TextItems);
        Assert.Equal(120, item.X);
        Assert.Equal(210, item.Y);
        Assert.Equal(40, item.Width);
        Assert.Equal(20, item.Height);
    }

    [Fact]
    public void CreateSnapshot_WhenScaledSizeRoundsToZero_KeepsPositiveOverlaySize()
    {
        var service = new OverlayPositioningService();
        var result = CreateResult(
            new CaptureRegion(0, 0, 10, 10),
            inputWidth: 100,
            inputHeight: 100,
            new OcrTextBlock("Tiny", new BoundingBox(1, 1, 1, 1)));

        var snapshot = service.CreateSnapshot(result, ShownAt);

        var item = Assert.Single(snapshot.TextItems);
        Assert.Equal(1, item.Width);
        Assert.Equal(1, item.Height);
    }

    [Fact]
    public void CreateSnapshot_WhenMatchingTextOnlyJittersWithinTolerance_ReusesPreviousBounds()
    {
        var service = new OverlayPositioningService();
        var previousSnapshot = CreateSnapshot(new OverlayTextItem("Start", 14, 25, 24, 10));
        var result = CreateResult(
            new CaptureRegion(10, 20, 100, 40),
            inputWidth: 100,
            inputHeight: 40,
            new OcrTextBlock("Start", new BoundingBox(7, 2, 27, 8)));

        var snapshot = service.CreateSnapshot(result, ShownAt, previousSnapshot);

        var item = Assert.Single(snapshot.TextItems);
        Assert.Equal("Start", item.Text);
        Assert.Equal(14, item.X);
        Assert.Equal(25, item.Y);
        Assert.Equal(24, item.Width);
        Assert.Equal(10, item.Height);
    }

    [Fact]
    public void CreateSnapshot_WhenMatchingTextMovesBeyondTolerance_UsesCurrentBounds()
    {
        var service = new OverlayPositioningService();
        var previousSnapshot = CreateSnapshot(new OverlayTextItem("Start", 14, 25, 24, 10));
        var result = CreateResult(
            new CaptureRegion(10, 20, 100, 40),
            inputWidth: 100,
            inputHeight: 40,
            new OcrTextBlock("Start", new BoundingBox(9, 5, 24, 10)));

        var snapshot = service.CreateSnapshot(result, ShownAt, previousSnapshot);

        var item = Assert.Single(snapshot.TextItems);
        Assert.Equal("Start", item.Text);
        Assert.Equal(19, item.X);
        Assert.Equal(25, item.Y);
        Assert.Equal(24, item.Width);
        Assert.Equal(10, item.Height);
    }

    [Fact]
    public void CreateSnapshot_WhenTextChangesWithinTolerance_UsesCurrentBounds()
    {
        var service = new OverlayPositioningService();
        var previousSnapshot = CreateSnapshot(new OverlayTextItem("Start", 14, 25, 24, 10));
        var result = CreateResult(
            new CaptureRegion(10, 20, 100, 40),
            inputWidth: 100,
            inputHeight: 40,
            new OcrTextBlock("Continue", new BoundingBox(7, 2, 27, 8)));

        var snapshot = service.CreateSnapshot(result, ShownAt, previousSnapshot);

        var item = Assert.Single(snapshot.TextItems);
        Assert.Equal("Continue", item.Text);
        Assert.Equal(17, item.X);
        Assert.Equal(22, item.Y);
        Assert.Equal(27, item.Width);
        Assert.Equal(8, item.Height);
    }

    [Fact]
    public void CreateSnapshot_WhenNoTextBlocks_ReturnsEmptySnapshot()
    {
        var service = new OverlayPositioningService();
        var result = CreateResult(new CaptureRegion(0, 0, 100, 40), 100, 40);

        var snapshot = service.CreateSnapshot(result, ShownAt);

        Assert.Empty(snapshot.TextItems);
        Assert.Empty(snapshot.MaskItems);
        Assert.Equal(ShownAt, snapshot.ShownAt);
    }

    [Fact]
    public void CreateSnapshot_WithOverlaySettings_ExpandsMaskByPaddingAndPreservesMaskSettings()
    {
        var service = new OverlayPositioningService();
        var settings = new OverlaySettings
        {
            MaskMode = OverlayMaskMode.Darken,
            MaskColor = "#202020",
            Opacity = 0.65,
            Padding = 6,
        };
        var result = CreateResult(
            new CaptureRegion(10, 20, 100, 40),
            inputWidth: 100,
            inputHeight: 40,
            new OcrTextBlock("Start", new BoundingBox(4, 5, 24, 10)));

        var snapshot = service.CreateSnapshot(result, ShownAt, settings);

        Assert.Equal(settings, snapshot.OverlaySettings);
        var text = Assert.Single(snapshot.TextItems);
        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal(OverlayMaskMode.Darken, mask.Mode);
        Assert.Equal("#202020", mask.Color);
        Assert.Equal(0.65, mask.Opacity);
        Assert.Equal(text.X - 6, mask.X);
        Assert.Equal(text.Y - 6, mask.Y);
        Assert.Equal(text.Width + 12, mask.Width);
        Assert.Equal(text.Height + 12, mask.Height);
    }

    [Fact]
    public void CreateSnapshot_WithExpandedTextStyle_CentersExpandedTextAroundSourceBounds()
    {
        var service = new OverlayPositioningService();
        var textStyle = new OcrZoneTextStyle
        {
            FontFamily = "Arial",
            FontSize = 20,
            IsBold = false,
            IsItalic = true,
            LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter,
        };
        var result = CreateResult(
            new CaptureRegion(100, 200, 200, 80),
            inputWidth: 100,
            inputHeight: 40,
            new OcrTextBlock("Bonjour", new BoundingBox(10, 5, 20, 10)));

        var snapshot = service.CreateSnapshot(result, ShownAt, previousSnapshot: null, textStyle);

        var item = Assert.Single(snapshot.TextItems);
        Assert.Equal("Bonjour", item.Text);
        Assert.Equal(textStyle, item.TextStyle);
        Assert.True(item.Width > 40);
        Assert.True(item.Height > 20);
        Assert.Equal(140d, item.X + item.Width / 2d, precision: 0);
        Assert.Equal(220d, item.Y + item.Height / 2d, precision: 0);
        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal(item.X, mask.X);
        Assert.Equal(item.Y, mask.Y);
        Assert.Equal(item.Width, mask.Width);
        Assert.Equal(item.Height, mask.Height);
    }

    [Fact]
    public void CreateSnapshot_WhenPaddingWouldMoveMaskOffScreen_ClampsMaskOrigin()
    {
        var service = new OverlayPositioningService();
        var settings = new OverlaySettings
        {
            MaskMode = OverlayMaskMode.Solid,
            MaskColor = "#101010",
            Opacity = 0.5,
            Padding = 8,
        };
        var result = CreateResult(
            new CaptureRegion(0, 0, 100, 40),
            inputWidth: 100,
            inputHeight: 40,
            new OcrTextBlock("Edge", new BoundingBox(2, 3, 20, 10)));

        var snapshot = service.CreateSnapshot(result, ShownAt, settings);

        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal(0, mask.X);
        Assert.Equal(0, mask.Y);
        Assert.Equal(36, mask.Width);
        Assert.Equal(26, mask.Height);
    }

    private static OverlaySnapshot CreateSnapshot(params OverlayTextItem[] textItems)
    {
        return new OverlaySnapshot(textItems, ShownAt.AddSeconds(-1));
    }

    private static OcrResult CreateResult(
        CaptureRegion region,
        int inputWidth,
        int inputHeight,
        params OcrTextBlock[] blocks)
    {
        var frame = CreateFrame(region, inputWidth, inputHeight);

        return new OcrResult(
            new OcrRequest(frame, "en", "zone-a"),
            blocks,
            FrameTime);
    }

    private static CapturedFrame CreateFrame(CaptureRegion region, int width, int height)
    {
        var stride = checked(width * 4);
        var pixels = Enumerable.Repeat((byte)42, checked(stride * height)).ToArray();

        return new CapturedFrame(
            region,
            width,
            height,
            stride,
            "Bgra32",
            pixels,
            FrameTime);
    }
}
