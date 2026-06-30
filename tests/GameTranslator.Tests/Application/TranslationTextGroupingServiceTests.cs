using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class TranslationTextGroupingServiceTests
{
    [Fact]
    public void CreateTextGroupingResult_WhenVerticalCjkBlocksIncludeDarkUiNoise_FiltersNoiseBeforeMasksAndTranslation()
    {
        var frame = CreateFrameWithVerticalCjkRegions();
        var sourceResult = new OcrResult(
            new OcrRequest(frame, "zh-TW", "zone-a", orientationMode: OcrOrientationMode.Vertical),
            new[]
            {
                new OcrTextBlock("ge", new BoundingBox(30, 6, 32, 10)),
                new OcrTextBlock("生", new BoundingBox(92, 6, 16, 12)),
                new OcrTextBlock("你好", new BoundingBox(150, 52, 20, 38)),
                new OcrTextBlock("3", new BoundingBox(153, 94, 14, 20)),
                new OcrTextBlock("說人", new BoundingBox(40, 124, 24, 14)),
            },
            new DateTimeOffset(2026, 6, 30, 6, 0, 0, TimeSpan.Zero));
        var zone = new OcrZone
        {
            Id = "zone-a",
            Name = "Vertical CJK",
            AbsoluteBounds = new AbsoluteRectangle(0, 0, frame.Width, frame.Height),
            RelativeBounds = new RelativeRectangle(0, 0, 1, 1),
            TranslationGroupingMode = TranslationGroupingMode.NearbyBlocks,
            TextGrouping = new OcrZoneTextGroupingSettings
            {
                MergeDistancePercent = 10,
            },
        };

        var result = TranslationTextGroupingService.CreateTextGroupingResult(sourceResult, zone);

        Assert.Equal(new[] { "你好", "3" }, result.MaskSourceResult.TextBlocks.Select(block => block.Text));
        var translatedBlock = Assert.Single(result.TranslationSourceResult.TextBlocks);
        Assert.Equal("你好 3", translatedBlock.Text);
        Assert.Equal(new BoundingBox(150, 52, 20, 62), translatedBlock.Bounds);
    }

    [Fact]
    public void CreateTextGroupingResult_WhenVerticalCjkHasWideHorizontalNoise_DoesNotBridgeColumnsThroughNoise()
    {
        var frame = CreateFrame(320, 180, CreatePixels(320, 180, 245));
        var sourceResult = new OcrResult(
            new OcrRequest(frame, "zh-TW", "zone-a", orientationMode: OcrOrientationMode.Vertical),
            new[]
            {
                new OcrTextBlock("\u4f60", new BoundingBox(180, 40, 18, 22)),
                new OcrTextBlock("\u597d", new BoundingBox(181, 68, 18, 22)),
                new OcrTextBlock("\u5065", new BoundingBox(145, 42, 18, 22)),
                new OcrTextBlock("\u592a", new BoundingBox(146, 70, 18, 22)),
                new OcrTextBlock("\u4e00 \u4e8c \u4e09 \u56db \u4e94", new BoundingBox(20, 50, 260, 10)),
                new OcrTextBlock("\u516d \u4e03 \u516b \u4e5d", new BoundingBox(20, 78, 240, 10)),
                new OcrTextBlock("\u8b17", new BoundingBox(300, 120, 14, 14)),
            },
            new DateTimeOffset(2026, 6, 30, 6, 10, 0, TimeSpan.Zero));
        var zone = new OcrZone
        {
            Id = "zone-a",
            Name = "Vertical CJK",
            AbsoluteBounds = new AbsoluteRectangle(0, 0, frame.Width, frame.Height),
            RelativeBounds = new RelativeRectangle(0, 0, 1, 1),
            TranslationGroupingMode = TranslationGroupingMode.NearbyBlocks,
            TextGrouping = new OcrZoneTextGroupingSettings
            {
                MergeDistancePercent = 10,
            },
        };

        var result = TranslationTextGroupingService.CreateTextGroupingResult(sourceResult, zone);

        var translatedBlock = Assert.Single(result.TranslationSourceResult.TextBlocks);
        Assert.Equal("\u4f60 \u597d \u5065 \u592a", translatedBlock.Text);
        Assert.Equal(new BoundingBox(145, 40, 54, 52), translatedBlock.Bounds);
        Assert.Equal(
            new[] { "\u4f60", "\u597d", "\u5065", "\u592a" },
            result.MaskSourceResult.TextBlocks.Select(block => block.Text));
    }

    [Fact]
    public void CreateTextGroupingResult_WhenVerticalCjkGroupIsOnHalftoneBackground_DoesNotUseItForTranslationOrMasks()
    {
        var pixels = CreatePixels(320, 180, 245);
        FillRectangle(pixels, 320, 28, 28, 90, 90, 160);
        FillRectangle(pixels, 320, 58, 44, 18, 50, 245);
        FillRectangle(pixels, 320, 88, 44, 18, 50, 245);
        var frame = CreateFrame(320, 180, pixels);
        var sourceResult = new OcrResult(
            new OcrRequest(frame, "zh-TW", "zone-a", orientationMode: OcrOrientationMode.Vertical),
            new[]
            {
                new OcrTextBlock("\u4f60", new BoundingBox(180, 40, 18, 22)),
                new OcrTextBlock("\u597d", new BoundingBox(181, 68, 18, 22)),
                new OcrTextBlock("\u4eba", new BoundingBox(58, 44, 18, 22)),
                new OcrTextBlock("\u591a", new BoundingBox(88, 44, 18, 22)),
            },
            new DateTimeOffset(2026, 6, 30, 6, 15, 0, TimeSpan.Zero));
        var zone = new OcrZone
        {
            Id = "zone-a",
            Name = "Vertical CJK",
            AbsoluteBounds = new AbsoluteRectangle(0, 0, frame.Width, frame.Height),
            RelativeBounds = new RelativeRectangle(0, 0, 1, 1),
            TranslationGroupingMode = TranslationGroupingMode.NearbyBlocks,
            TextGrouping = new OcrZoneTextGroupingSettings
            {
                MergeDistancePercent = 10,
            },
        };

        var result = TranslationTextGroupingService.CreateTextGroupingResult(sourceResult, zone);

        var translatedBlock = Assert.Single(result.TranslationSourceResult.TextBlocks);
        Assert.Equal("\u4f60 \u597d", translatedBlock.Text);
        Assert.Equal(
            new[] { "\u4f60", "\u597d" },
            result.MaskSourceResult.TextBlocks.Select(block => block.Text));
    }

    [Fact]
    public void CreateTextGroupingResult_WhenVerticalCjkFilterWouldRemoveEverything_KeepsOriginalBlocks()
    {
        var pixels = CreatePixels(80, 80, 40);
        var frame = CreateFrame(80, 80, pixels);
        var sourceResult = new OcrResult(
            new OcrRequest(frame, "zh-TW", "zone-a", orientationMode: OcrOrientationMode.Vertical),
            new[]
            {
                new OcrTextBlock("生", new BoundingBox(10, 10, 16, 16)),
                new OcrTextBlock("人", new BoundingBox(40, 10, 16, 16)),
            },
            new DateTimeOffset(2026, 6, 30, 6, 0, 0, TimeSpan.Zero));
        var zone = new OcrZone
        {
            Id = "zone-a",
            Name = "Dark CJK UI",
            AbsoluteBounds = new AbsoluteRectangle(0, 0, frame.Width, frame.Height),
            RelativeBounds = new RelativeRectangle(0, 0, 1, 1),
            TranslationGroupingMode = TranslationGroupingMode.BlockByBlock,
        };

        var result = TranslationTextGroupingService.CreateTextGroupingResult(sourceResult, zone);

        Assert.Same(sourceResult, result.MaskSourceResult);
        Assert.Equal(new[] { "生", "人" }, result.TranslationSourceResult.TextBlocks.Select(block => block.Text));
    }

    private static CapturedFrame CreateFrameWithVerticalCjkRegions()
    {
        var pixels = CreatePixels(220, 160, 180);
        FillRectangle(pixels, 220, 0, 0, 220, 26, 40);
        FillRectangle(pixels, 220, 136, 36, 52, 94, 245);

        return CreateFrame(220, 160, pixels);
    }

    private static CapturedFrame CreateFrame(int width, int height, byte[] pixels)
    {
        var stride = checked(width * 4);
        return new CapturedFrame(
            new CaptureRegion(0, 0, width, height),
            width,
            height,
            stride,
            "Bgra32",
            pixels,
            new DateTimeOffset(2026, 6, 30, 5, 59, 59, TimeSpan.Zero));
    }

    private static byte[] CreatePixels(int width, int height, byte value)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = value;
            pixels[index + 1] = value;
            pixels[index + 2] = value;
            pixels[index + 3] = byte.MaxValue;
        }

        return pixels;
    }

    private static void FillRectangle(
        byte[] pixels,
        int frameWidth,
        int x,
        int y,
        int width,
        int height,
        byte value)
    {
        var stride = checked(frameWidth * 4);
        for (var row = y; row < y + height; row++)
        {
            var rowOffset = row * stride;
            for (var column = x; column < x + width; column++)
            {
                var offset = rowOffset + column * 4;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = byte.MaxValue;
            }
        }
    }
}
