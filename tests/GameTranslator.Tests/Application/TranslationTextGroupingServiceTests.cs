using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class TranslationTextGroupingServiceTests
{
    private static readonly DateTimeOffset FrameTime = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateTranslationSourceResult_WhenNearbyBlocksRunsInLargeUserZone_DoesNotBridgeDistantBubbles()
    {
        var result = CreateResult(
            inputWidth: 1840,
            inputHeight: 880,
            new OcrTextBlock("Long time no see.", new BoundingBox(470, 44, 350, 42)),
            new OcrTextBlock("Where have you been?", new BoundingBox(470, 98, 350, 42)),
            new OcrTextBlock("This translated paragraph stays in its own balloon.", new BoundingBox(930, 40, 360, 150)));
        var zone = new OcrZone
        {
            Id = "comic-page",
            Name = "Comic page",
            TranslationGroupingMode = TranslationGroupingMode.NearbyBlocks,
            TextGrouping = new OcrZoneTextGroupingSettings
            {
                MergeDistancePercent = 6.5,
            },
        };

        var grouped = TranslationTextGroupingService.CreateTranslationSourceResult(result, zone);

        Assert.Equal(
            new[]
            {
                "Long time no see. Where have you been?",
                "This translated paragraph stays in its own balloon.",
            },
            grouped.TextBlocks.Select(block => block.Text));
        Assert.Equal(new[] { 2, 1 }, grouped.TextBlockSources.Select(source => source.MemberBounds.Count));
    }

    [Fact]
    public void CreateTranslationSourceResult_WhenBlocksAreGrouped_PreservesRawWordMetadata()
    {
        var rawResult = CreateResult(
            inputWidth: 300,
            inputHeight: 120,
            new OcrTextBlock("Long", new BoundingBox(40, 20, 32, 14)),
            new OcrTextBlock("time", new BoundingBox(78, 20, 34, 14)));
        var expectedWords = new[]
        {
            new OcrWord("Long", new BoundingBox(40, 20, 32, 14), 94.5, "tesseract:SingleBlock"),
            new OcrWord("time", new BoundingBox(78, 20, 34, 14), 96.25, "tesseract:SingleBlock"),
        };
        var result = new OcrResult(
            rawResult.Request,
            rawResult.TextBlocks,
            rawResult.RecognizedAt,
            rawResult.TextBlockSources,
            expectedWords);
        var zone = new OcrZone
        {
            Id = "comic-page",
            Name = "Comic page",
            TranslationGroupingMode = TranslationGroupingMode.WholeZone,
        };

        var grouped = TranslationTextGroupingService.CreateTranslationSourceResult(result, zone);

        Assert.Single(grouped.TextBlocks);
        Assert.Collection(
            grouped.Words,
            word => Assert.Same(expectedWords[0], word),
            word => Assert.Same(expectedWords[1], word));
    }

    private static OcrResult CreateResult(int inputWidth, int inputHeight, params OcrTextBlock[] blocks)
    {
        var region = new CaptureRegion(0, 0, inputWidth, inputHeight);
        var stride = checked(inputWidth * 4);
        var frame = new CapturedFrame(
            region,
            inputWidth,
            inputHeight,
            stride,
            "Bgra32",
            new byte[checked(stride * inputHeight)],
            FrameTime);

        return new OcrResult(
            new OcrRequest(frame, "en", "comic-page", orientationMode: OcrOrientationMode.Horizontal),
            blocks,
            FrameTime);
    }
}
