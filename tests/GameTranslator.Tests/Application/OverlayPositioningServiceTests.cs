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
    public void CreateSnapshot_WithExpandedTextStyle_DampensRightOverflowAndKeepsSourceMask()
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
        Assert.InRange(item.X + item.Width / 2d, 185d, 186d);
        Assert.InRange(item.Y + item.Height / 2d, 220d, 240d);
        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal(180, mask.X);
        Assert.Equal(210, mask.Y);
        Assert.Equal(40, mask.Width);
        Assert.Equal(20, mask.Height);
    }

    [Fact]
    public void CreateSnapshot_WithExpandedHorizontalNeighbors_KeepsTranslationInsideZoneAndAwayFromOtherSemanticGroups()
    {
        var service = new OverlayPositioningService();
        var textStyle = new OcrZoneTextStyle
        {
            FontFamily = "Segoe UI",
            FontSize = 20,
            IsBold = true,
            LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter,
        };
        var blocks = new[]
        {
            new OcrTextBlock("Save game", new BoundingBox(38, 40, 180, 42)),
            new OcrTextBlock("Load game", new BoundingBox(38, 94, 180, 42)),
            new OcrTextBlock("HP 120/150 MP 45/80 Gold 9,999", new BoundingBox(234, 40, 190, 96)),
        };
        var result = CreateResultWithSources(
            new CaptureRegion(1396, 110, 480, 220),
            inputWidth: 480,
            inputHeight: 220,
            OcrOrientationMode.Horizontal,
            blocks,
            blocks
                .Select(block => new OcrTextBlockSource(block.Bounds, new[] { block.Bounds }, OcrOrientationMode.Horizontal))
                .ToArray());

        var snapshot = service.CreateSnapshot(result, ShownAt, previousSnapshot: null, textStyle);

        var hp = snapshot.TextItems[2];
        Assert.True(hp.X >= 1396);
        Assert.True(hp.X + hp.Width <= 1876);
        Assert.False(Intersects(hp, new BoundingBox(1434, 150, 180, 42)));
        Assert.False(Intersects(hp, new BoundingBox(1434, 204, 180, 42)));
        Assert.Equal(1630, snapshot.MaskItems[2].X);
        Assert.Equal(150, snapshot.MaskItems[2].Y);
        Assert.Equal(190, snapshot.MaskItems[2].Width);
        Assert.Equal(96, snapshot.MaskItems[2].Height);
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
        Assert.InRange(item.X + item.Width / 2d, 210d, 220d);
        Assert.InRange(item.Y + item.Height / 2d, 140d, 160d);
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
        Assert.InRange(item.X + item.Width / 2d, 350d, 370d);
        Assert.True(item.Y >= 0);
        Assert.True(item.Y + item.Height <= 600);
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
        Assert.Equal(100, item.X);
        Assert.Equal(200, item.Width);
        Assert.True(item.Width > item.Height * 4);
        Assert.Equal(200d, item.X + item.Width / 2d, precision: 0);
        Assert.Equal(380d, item.Y + item.Height / 2d, precision: 0);
    }

    [Fact]
    public void CreateSnapshot_WithExpandedVerticalOcrBounds_BoundsAreaAndKeepsSourceMask()
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
        Assert.Equal(30, item.Width);
        Assert.True(item.Height <= 400);
        Assert.True(item.Width * item.Height <= 30 * 400 * 1.10);
        Assert.Equal(235d, item.X + item.Width / 2d, precision: 0);
        Assert.InRange(item.Y, 25, 425);
        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal(220, mask.X);
        Assert.Equal(25, mask.Y);
        Assert.Equal(30, mask.Width);
        Assert.Equal(400, mask.Height);
    }

    [Fact]
    public void CreateSnapshot_WithHorizontalMultilineRightOverflow_AppliesLineOffsetAndDampening()
    {
        var service = new OverlayPositioningService();
        var textStyle = new OcrZoneTextStyle
        {
            FontSize = 16,
            LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter,
        };
        var block = new OcrTextBlock("Translated option", new BoundingBox(100, 100, 80, 180));
        var result = CreateResultWithSources(
            new CaptureRegion(0, 0, 300, 300),
            inputWidth: 300,
            inputHeight: 300,
            OcrOrientationMode.Horizontal,
            new[] { block },
            new[]
            {
                new OcrTextBlockSource(
                    block.Bounds,
                    new[]
                    {
                        new BoundingBox(100, 100, 80, 12),
                        new BoundingBox(100, 124, 80, 12),
                    },
                    OcrOrientationMode.Horizontal),
            });

        var snapshot = service.CreateSnapshot(result, ShownAt, previousSnapshot: null, textStyle);

        var item = Assert.Single(snapshot.TextItems);
        Assert.Equal(92, item.Y);
        Assert.True(item.X < 100);
        Assert.True(item.X + item.Width > 180);
        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal(100, mask.X);
        Assert.Equal(100, mask.Y);
        Assert.Equal(80, mask.Width);
        Assert.Equal(180, mask.Height);
    }

    [Fact]
    public void CreateSnapshot_WithCompactVerticalExpandedText_SkipsXDampeningAndKeepsBaseFont()
    {
        var service = new OverlayPositioningService();
        var textStyle = new OcrZoneTextStyle
        {
            FontSize = 14,
            LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter,
        };
        var block = new OcrTextBlock("Calm fit", new BoundingBox(70, 108, 58, 116));
        var result = CreateResultWithSources(
            new CaptureRegion(0, 0, 260, 260),
            inputWidth: 260,
            inputHeight: 260,
            OcrOrientationMode.Auto,
            new[] { block },
            new[]
            {
                new OcrTextBlockSource(
                    block.Bounds,
                    new[] { block.Bounds },
                    OcrOrientationMode.Vertical),
            });

        var snapshot = service.CreateSnapshot(result, ShownAt, previousSnapshot: null, textStyle);

        var item = Assert.Single(snapshot.TextItems);
        Assert.Equal(14, item.TextStyle.FontSize);
        Assert.Equal(58, item.Width);
        Assert.Equal(70, item.X);
        Assert.Equal(99d, item.X + item.Width / 2d, precision: 0);
        Assert.Equal(128, item.X + item.Width);
        Assert.True(item.Width * item.Height <= 58 * 116 * 1.10);
        var mask = Assert.Single(snapshot.MaskItems);
        Assert.Equal(70, mask.X);
        Assert.Equal(108, mask.Y);
        Assert.Equal(58, mask.Width);
        Assert.Equal(116, mask.Height);
    }

    [Fact]
    public void CreateSnapshot_WithWideVerticalFitToSource_DampensRightOverflow()
    {
        var service = new OverlayPositioningService();
        var block = new OcrTextBlock("Wide vertical", new BoundingBox(100, 50, 120, 180));
        var result = CreateResultWithSources(
            new CaptureRegion(0, 0, 320, 260),
            inputWidth: 320,
            inputHeight: 260,
            OcrOrientationMode.Auto,
            new[] { block },
            new[]
            {
                new OcrTextBlockSource(
                    block.Bounds,
                    new[] { block.Bounds },
                    OcrOrientationMode.Vertical),
            });

        var snapshot = service.CreateSnapshot(result, ShownAt);

        var item = Assert.Single(snapshot.TextItems);
        Assert.Equal(55, item.X);
        Assert.Equal(80, item.Y);
        Assert.Equal(180, item.Width);
        Assert.Equal(120, item.Height);
    }

    [Fact]
    public void CreateSnapshot_WithMixedOrientationGroups_AppliesRulesIndependently()
    {
        var service = new OverlayPositioningService();
        var blocks = new[]
        {
            new OcrTextBlock("Vertical", new BoundingBox(34, 60, 60, 72)),
            new OcrTextBlock("Single", new BoundingBox(126, 38, 104, 38)),
            new OcrTextBlock("Book page", new BoundingBox(42, 168, 144, 58)),
        };
        var result = CreateResultWithSources(
            new CaptureRegion(0, 0, 320, 260),
            inputWidth: 320,
            inputHeight: 260,
            OcrOrientationMode.Auto,
            blocks,
            new[]
            {
                new OcrTextBlockSource(blocks[0].Bounds, new[] { blocks[0].Bounds }, OcrOrientationMode.Vertical),
                new OcrTextBlockSource(blocks[1].Bounds, new[] { blocks[1].Bounds }, OcrOrientationMode.Horizontal),
                new OcrTextBlockSource(
                    blocks[2].Bounds,
                    new[]
                    {
                        new BoundingBox(42, 168, 144, 20),
                        new BoundingBox(42, 202, 144, 20),
                    },
                    OcrOrientationMode.Horizontal),
            });

        var snapshot = service.CreateSnapshot(result, ShownAt);

        Assert.Equal(new[] { "Vertical", "Single", "Book page" }, snapshot.TextItems.Select(item => item.Text));
        Assert.Equal(66, snapshot.TextItems[0].Y);
        Assert.Equal(38, snapshot.TextItems[1].Y);
        Assert.Equal(160, snapshot.TextItems[2].Y);
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

    private static bool Intersects(OverlayTextItem item, BoundingBox bounds)
    {
        var width = Math.Min(item.X + item.Width, bounds.Right) - Math.Max(item.X, bounds.X);
        var height = Math.Min(item.Y + item.Height, bounds.Bottom) - Math.Max(item.Y, bounds.Y);

        return width > 2 && height > 2;
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

    private static OcrResult CreateResultWithSources(
        CaptureRegion region,
        int inputWidth,
        int inputHeight,
        OcrOrientationMode orientationMode,
        IReadOnlyList<OcrTextBlock> blocks,
        IReadOnlyList<OcrTextBlockSource> sources)
    {
        var frame = CreateFrame(region, inputWidth, inputHeight);

        return new OcrResult(
            new OcrRequest(frame, "en", "zone-a", orientationMode: orientationMode),
            blocks,
            FrameTime,
            sources);
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
