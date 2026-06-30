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
        var previousResult = CreateResult(
            new CaptureRegion(10, 20, 100, 40),
            inputWidth: 100,
            inputHeight: 40,
            new OcrTextBlock("Start", new BoundingBox(4, 5, 24, 10)));
        var previousSnapshot = service.CreateSnapshot(previousResult, ShownAt.AddSeconds(-1));
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
        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal(14, mask.X);
        Assert.Equal(25, mask.Y);
        Assert.Equal(24, mask.Width);
        Assert.Equal(10, mask.Height);
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
            new OcrTextBlock("Bonjour", new BoundingBox(40, 5, 20, 10)));

        var snapshot = service.CreateSnapshot(result, ShownAt, previousSnapshot: null, textStyle);

        var item = Assert.Single(snapshot.TextItems);
        Assert.Equal("Bonjour", item.Text);
        Assert.Equal(textStyle, item.TextStyle);
        Assert.True(item.Width > 40);
        Assert.True(item.Height > 20);
        Assert.Equal(200d, item.X + item.Width / 2d, precision: 0);
        Assert.Equal(220d, item.Y + item.Height / 2d, precision: 0);
        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal(180, mask.X);
        Assert.Equal(210, mask.Y);
        Assert.Equal(40, mask.Width);
        Assert.Equal(20, mask.Height);
    }

    [Fact]
    public void CreateSnapshot_WithExpandedLongText_WrapsAroundSourceCenterAndKeepsSourceMask()
    {
        var service = new OverlayPositioningService();
        var textStyle = new OcrZoneTextStyle
        {
            FontFamily = "Arial",
            FontSize = 20,
            LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter,
        };
        var result = CreateResult(
            new CaptureRegion(0, 0, 400, 300),
            inputWidth: 400,
            inputHeight: 300,
            new OcrTextBlock(
                "This translated sentence is much longer than the original bubble text.",
                new BoundingBox(180, 130, 80, 20)));

        var snapshot = service.CreateSnapshot(result, ShownAt, previousSnapshot: null, textStyle);

        var item = Assert.Single(snapshot.TextItems);
        Assert.True(item.Width <= 200);
        Assert.True(item.Height > 20);
        Assert.Equal(220d, item.X + item.Width / 2d, precision: 0);
        Assert.Equal(140d, item.Y + item.Height / 2d, precision: 0);
        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal(180, mask.X);
        Assert.Equal(130, mask.Y);
        Assert.Equal(80, mask.Width);
        Assert.Equal(20, mask.Height);
    }

    [Fact]
    public void CreateSnapshot_WithExpandedDenseText_AllocatesHeightForWrappedLines()
    {
        var service = new OverlayPositioningService();
        var textStyle = new OcrZoneTextStyle
        {
            FontFamily = "Arial",
            FontSize = 18,
            LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter,
        };
        var result = CreateResult(
            new CaptureRegion(0, 0, 800, 600),
            inputWidth: 800,
            inputHeight: 600,
            new OcrTextBlock(
                "This translated comic bubble contains enough words to require several wrapped lines without clipping the final line.",
                new BoundingBox(340, 250, 60, 16)));

        var snapshot = service.CreateSnapshot(result, ShownAt, previousSnapshot: null, textStyle);

        var item = Assert.Single(snapshot.TextItems);
        Assert.True(item.Width > 60);
        Assert.True(item.Height > 160);
        Assert.Equal(370d, item.X + item.Width / 2d, precision: 0);
        Assert.Equal(258d, item.Y + item.Height / 2d, precision: 0);
        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal(340, mask.X);
        Assert.Equal(250, mask.Y);
        Assert.Equal(60, mask.Width);
        Assert.Equal(16, mask.Height);
    }

    [Fact]
    public void CreateSnapshot_WhenVerticalOcrBoundsAreTall_UsesReadableHorizontalTextBounds()
    {
        var service = new OverlayPositioningService();
        var result = CreateResult(
            new CaptureRegion(100, 200, 200, 400),
            inputWidth: 100,
            inputHeight: 200,
            orientationMode: OcrOrientationMode.Vertical,
            blocks: new[] { new OcrTextBlock("Translated subtitle", new BoundingBox(40, 10, 10, 160)) });

        var snapshot = service.CreateSnapshot(result, ShownAt);

        var item = Assert.Single(snapshot.TextItems);
        Assert.Equal("Translated subtitle", item.Text);
        Assert.Equal(OverlayTextLayoutMode.ExpandFromSourceCenter, item.TextStyle.LayoutMode);
        Assert.True(item.Width >= 96);
        Assert.True(item.Height > 32);
        Assert.Equal(190d, item.X + item.Width / 2d, precision: 0);
        Assert.Equal(380d, item.Y + item.Height / 2d, precision: 0);
    }

    [Fact]
    public void CreateSnapshot_WhenVerticalSourceHasSideRoom_KeepsExpandedTextAnchoredToSourceCenter()
    {
        var service = new OverlayPositioningService();
        var result = CreateResult(
            new CaptureRegion(0, 0, 800, 600),
            inputWidth: 800,
            inputHeight: 600,
            orientationMode: OcrOrientationMode.Vertical,
            blocks: new[] { new OcrTextBlock("Translated vertical text", new BoundingBox(600, 100, 40, 360)) });

        var snapshot = service.CreateSnapshot(result, ShownAt);

        var item = Assert.Single(snapshot.TextItems);
        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal(OverlayTextLayoutMode.ExpandFromSourceCenter, item.TextStyle.LayoutMode);
        Assert.True(item.Width > item.Height * 4);
        Assert.Equal(620d, item.X + item.Width / 2d, precision: 0);
        Assert.Equal(280d, item.Y + item.Height / 2d, precision: 0);
        Assert.True(item.X < mask.X);
        Assert.True(item.X + item.Width > mask.X + mask.Width);
    }

    [Fact]
    public void CreateSnapshot_WhenVerticalSparseBoundsAreSmall_ForcesReadableExpandedText()
    {
        var service = new OverlayPositioningService();
        var result = CreateResult(
            new CaptureRegion(0, 0, 1600, 900),
            inputWidth: 1600,
            inputHeight: 900,
            orientationMode: OcrOrientationMode.Vertical,
            blocks: new[] { new OcrTextBlock("Мир растет", new BoundingBox(1017, 201, 24, 7)) });

        var snapshot = service.CreateSnapshot(result, ShownAt);

        var item = Assert.Single(snapshot.TextItems);
        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal("Мир растет", item.Text);
        Assert.Equal(OverlayTextLayoutMode.ExpandFromSourceCenter, item.TextStyle.LayoutMode);
        Assert.True(item.Width >= 96);
        Assert.True(item.Height > mask.Height);
        Assert.Equal(1029d, item.X + item.Width / 2d, precision: 0);
        Assert.Equal(204.5d, item.Y + item.Height / 2d, precision: 1);
        Assert.True(item.X < mask.X);
        Assert.True(item.X + item.Width > mask.X + mask.Width);
        Assert.Equal(1017, mask.X);
        Assert.Equal(201, mask.Y);
        Assert.Equal(24, mask.Width);
        Assert.Equal(7, mask.Height);
    }

    [Fact]
    public void CreateSnapshot_WithExpandedVerticalOcrBounds_UsesReadableWidthWithoutKeepingTallSourceHeight()
    {
        var service = new OverlayPositioningService();
        var textStyle = new OcrZoneTextStyle
        {
            FontFamily = "Segoe UI",
            FontSize = 16,
            LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter,
        };
        var result = CreateResult(
            new CaptureRegion(100, 0, 300, 500),
            inputWidth: 100,
            inputHeight: 200,
            orientationMode: OcrOrientationMode.Vertical,
            blocks: new[]
            {
                new OcrTextBlock(
                    "Давно не виделись. Рад снова встретиться.",
                    new BoundingBox(40, 10, 10, 160)),
            });

        var snapshot = service.CreateSnapshot(result, ShownAt, previousSnapshot: null, textStyle);

        var item = Assert.Single(snapshot.TextItems);
        Assert.Equal("Давно не виделись. Рад снова встретиться.", item.Text);
        Assert.InRange(item.Width, 180, 216);
        Assert.True(item.Height < 120);
        Assert.Equal(235d, item.X + item.Width / 2d, precision: 0);
        Assert.InRange(item.Y + item.Height / 2d, 224d, 226d);
        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal(221, mask.X);
        Assert.Equal(45, mask.Y);
        Assert.Equal(28, mask.Width);
        Assert.Equal(360, mask.Height);
    }

    [Fact]
    public void CreateSnapshot_WithVeryTallExpandedVerticalOcrBounds_CapsTranslatedOverlaySize()
    {
        var service = new OverlayPositioningService();
        var textStyle = new OcrZoneTextStyle
        {
            FontFamily = "Segoe UI",
            FontSize = 18,
            LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter,
        };
        var result = CreateResult(
            new CaptureRegion(0, 0, 800, 1000),
            inputWidth: 800,
            inputHeight: 1000,
            orientationMode: OcrOrientationMode.Vertical,
            blocks: new[]
            {
                new OcrTextBlock(
                    "The translated Chinese vertical text should remain readable without covering the entire capture zone.",
                    new BoundingBox(275, 50, 250, 900)),
            });

        var snapshot = service.CreateSnapshot(result, ShownAt, previousSnapshot: null, textStyle);

        var item = Assert.Single(snapshot.TextItems);
        Assert.InRange(item.Width, 300, 420);
        Assert.True(item.Height < 180);
        Assert.Equal(400d, item.X + item.Width / 2d, precision: 0);
        Assert.Equal(500d, item.Y + item.Height / 2d, precision: 0);
        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal(352, mask.X);
        Assert.Equal(320, mask.Y);
        Assert.Equal(96, mask.Width);
        Assert.Equal(360, mask.Height);
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
        return CreateResult(region, inputWidth, inputHeight, OcrOrientationMode.Auto, blocks);
    }

    private static OcrResult CreateResult(
        CaptureRegion region,
        int inputWidth,
        int inputHeight,
        OcrOrientationMode orientationMode,
        params OcrTextBlock[] blocks)
    {
        var frame = CreateFrame(region, inputWidth, inputHeight);

        return new OcrResult(
            new OcrRequest(frame, "en", "zone-a", orientationMode: orientationMode),
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
