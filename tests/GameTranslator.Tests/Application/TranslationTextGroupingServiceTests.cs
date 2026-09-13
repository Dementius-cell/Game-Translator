using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class TranslationTextGroupingServiceTests
{
    private static readonly DateTimeOffset FrameTime = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("ja", "日 本 語", "日本語")]
    [InlineData("ja", "明 日 は 晴 れ で す。", "明日は晴れです。")]
    [InlineData("ko", "가 나", "가 나")]
    [InlineData("en", "Hello world", "Hello world")]
    [InlineData("ja", "HP 10", "HP 10")]
    public void SingleBlock_NormalizesTranslationOnly_InEveryGroupingAndOrientation(string language, string text, string expected)
    {
        foreach (var mode in Enum.GetValues<TranslationGroupingMode>())
        foreach (var orientation in new[] { OcrOrientationMode.Horizontal, OcrOrientationMode.Vertical })
        {
            var block = new OcrTextBlock(text, new BoundingBox(10, 10, 80, 30));
            var raw = CreateResult(300, 120, orientation, language, block);
            var word = new OcrWord(text, block.Bounds, 90, "test");
            var source = new OcrResult(raw.Request, raw.TextBlocks, raw.RecognizedAt, raw.TextBlockSources, new[] { word })
            {
                LineRecognition = new OcrLineRecognitionDiagnostics(1, 1),
            };
            var result = TranslationTextGroupingService.CreateTextGroupingResult(source,
                new OcrZone { Id = "test", Name = "Test", TranslationGroupingMode = mode });
            Assert.Same(source, result.MaskSourceResult);
            Assert.Equal(text, Assert.Single(source.TextBlocks).Text);
            var translated = result.TranslationSourceResult;
            Assert.Equal(expected, Assert.Single(translated.TextBlocks).Text);
            Assert.Equal(block.Bounds, translated.TextBlocks[0].Bounds);
            Assert.Same(source.TextBlockSources[0], Assert.Single(translated.TextBlockSources));
            Assert.Same(word, Assert.Single(translated.Words));
            Assert.Same(source.LineRecognition, translated.LineRecognition);
            if (text == expected) Assert.Same(source, translated);
        }
    }

    [Theory]
    [InlineData(TranslationGroupingMode.WholeZone)]
    [InlineData(TranslationGroupingMode.NearbyBlocks)]
    [InlineData(TranslationGroupingMode.BlockByBlock)]
    public void PunctuationNoise_MixedBlocks_KeepUsefulTextAndItsGeometry(TranslationGroupingMode mode)
    {
        var useful = new OcrTextBlock("A! 40", new BoundingBox(40, 20, 35, 14));
        var source = CreateResult(300, 120,
            new OcrTextBlock("|", new BoundingBox(5, 20, 10, 14)), useful);
        var zone = new OcrZone { Id = "test", Name = "Test", TranslationGroupingMode = mode };
        var result = TranslationTextGroupingService.CreateTextGroupingResult(source, zone);
        Assert.Same(source, result.MaskSourceResult);
        Assert.Equal(2, source.TextBlocks.Count);
        Assert.Same(useful, Assert.Single(result.TranslationSourceResult.TextBlocks));
        Assert.Equal(useful.Bounds, Assert.Single(result.TranslationSourceResult.TextBlockSources).SemanticBounds);
        Assert.Equal(new[] { useful.Bounds }, result.TranslationSourceResult.TextBlockSources[0].MemberBounds);
    }

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

    [Fact]
    public void CreateTextGroupingResult_PreservesRawBlocksForMasking()
    {
        var source = CreateResult(
            inputWidth: 300,
            inputHeight: 120,
            new OcrTextBlock("Right", new BoundingBox(90, 20, 35, 14)),
            new OcrTextBlock("Left", new BoundingBox(40, 20, 35, 14)));
        var zone = new OcrZone
        {
            Id = "compatibility-zone",
            Name = "Compatibility zone",
            TranslationGroupingMode = TranslationGroupingMode.WholeZone,
        };

        var result = TranslationTextGroupingService.CreateTextGroupingResult(source, zone);

        Assert.Equal(new[] { "Left Right" }, result.TranslationSourceResult.TextBlocks.Select(block => block.Text));
        Assert.Same(source, result.MaskSourceResult);
        Assert.Equal(new[] { "Right", "Left" }, result.MaskSourceResult.TextBlocks.Select(block => block.Text));
    }

    [Fact]
    public void CreateTranslationSourceResult_WhenVerticalCjkUsesWholeZone_OrdersColumnsRightToLeftAndTopToBottom()
    {
        var leftBottom = new OcrTextBlock("Left bottom", new BoundingBox(42, 70, 20, 30));
        var rightBottom = new OcrTextBlock("Right bottom", new BoundingBox(100, 70, 22, 30));
        var leftTop = new OcrTextBlock("Left top", new BoundingBox(40, 10, 22, 30));
        var rightTop = new OcrTextBlock("Right top", new BoundingBox(102, 10, 20, 30));
        var result = CreateResult(
            inputWidth: 180,
            inputHeight: 140,
            OcrOrientationMode.Vertical,
            "ja",
            leftBottom,
            rightBottom,
            leftTop,
            rightTop);
        var zone = new OcrZone
        {
            Id = "vertical-comic-page",
            Name = "Vertical comic page",
            TranslationGroupingMode = TranslationGroupingMode.WholeZone,
        };

        var grouped = TranslationTextGroupingService.CreateTranslationSourceResult(result, zone);

        var groupedBlock = Assert.Single(grouped.TextBlocks);
        Assert.Equal("Right top Right bottom Left top Left bottom", groupedBlock.Text);
        Assert.Equal(
            new[] { rightTop.Bounds, rightBottom.Bounds, leftTop.Bounds, leftBottom.Bounds },
            Assert.Single(grouped.TextBlockSources).MemberBounds);
    }

    [Theory]
    [InlineData("he", TranslationGroupingMode.WholeZone)]
    [InlineData("heb", TranslationGroupingMode.WholeZone)]
    [InlineData("he", TranslationGroupingMode.NearbyBlocks)]
    public void HebrewSplitRow_PreservesMixedTextAndRawGeometry(string language, TranslationGroupingMode mode)
    {
        var left = new OcrTextBlock("עולם", new BoundingBox(10, 10, 35, 20));
        var right = new OcrTextBlock("שלום HP 123", new BoundingBox(50, 10, 100, 20));
        var next = new OcrTextBlock("שלב 45", new BoundingBox(50, 35, 100, 20));
        var raw = CreateResult(400, 200, OcrOrientationMode.Horizontal, language, left, next, right);
        var result = TranslationTextGroupingService.CreateTextGroupingResult(raw,
            new OcrZone { Id = "he", Name = "Hebrew", TranslationGroupingMode = mode });
        Assert.Equal("שלום HP 123 עולם שלב 45", Assert.Single(result.TranslationSourceResult.TextBlocks).Text);
        Assert.Same(raw, result.MaskSourceResult);
        Assert.Equal(new[] { left, next, right }, raw.TextBlocks);
        Assert.Equal(new[] { right.Bounds, left.Bounds, next.Bounds },
            Assert.Single(result.TranslationSourceResult.TextBlockSources).MemberBounds);
        Assert.Equal(new[] { right.Bounds, left.Bounds, next.Bounds },
            TranslationTextGroupingService.ResolveOrderedMemberBoundsForDiagnostics(raw));
    }

    [Fact]
    public void HebrewSeparateBubbles_StaySeparateAndOrderedRightToLeft()
    {
        var left = new OcrTextBlock("שמאל 12", new BoundingBox(10, 10, 70, 20));
        var right = new OcrTextBlock("ימין ABC", new BoundingBox(250, 10, 100, 20));
        var raw = CreateResult(400, 200, OcrOrientationMode.Horizontal, "he", left, right);
        var result = TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { Id = "he", Name = "Hebrew", TranslationGroupingMode = TranslationGroupingMode.NearbyBlocks });
        Assert.Equal(new[] { right.Text, left.Text }, result.TextBlocks.Select(block => block.Text));
        Assert.Equal(new[] { right.Bounds, left.Bounds }, result.TextBlocks.Select(block => block.Bounds));
        Assert.All(result.TextBlockSources, source => Assert.Single(source.MemberBounds));
    }
    [Theory]
    [InlineData("ar", TranslationGroupingMode.WholeZone)]
    [InlineData("ara", TranslationGroupingMode.WholeZone)]
    [InlineData("ar-SA", TranslationGroupingMode.WholeZone)]
    [InlineData("ar", TranslationGroupingMode.NearbyBlocks)]
    public void ArabicSplitRow_PreservesMixedTextAndRawGeometry(string language, TranslationGroupingMode mode)
    {
        var left = new OcrTextBlock("العالم", new BoundingBox(10, 10, 35, 20));
        var right = new OcrTextBlock("مرحبا HP 123", new BoundingBox(50, 10, 100, 20));
        var next = new OcrTextBlock("مرحلة ٤٥", new BoundingBox(50, 35, 100, 20));
        var raw = CreateResult(400, 200, OcrOrientationMode.Horizontal, language, left, next, right);
        var result = TranslationTextGroupingService.CreateTextGroupingResult(raw,
            new OcrZone { Id = "ar", Name = "Arabic", TranslationGroupingMode = mode });
        Assert.Equal("مرحبا HP 123 العالم مرحلة ٤٥", Assert.Single(result.TranslationSourceResult.TextBlocks).Text);
        Assert.Same(raw, result.MaskSourceResult);
        Assert.Equal(new[] { left, next, right }, raw.TextBlocks);
        Assert.Equal(new[] { right.Bounds, left.Bounds, next.Bounds },
            Assert.Single(result.TranslationSourceResult.TextBlockSources).MemberBounds);
        Assert.Equal(new[] { right.Bounds, left.Bounds, next.Bounds },
            TranslationTextGroupingService.ResolveOrderedMemberBoundsForDiagnostics(raw));
    }

    [Fact]
    public void ArabicSeparateBubbles_StaySeparateAndOrderedRightToLeft()
    {
        var left = new OcrTextBlock("يسار ١٢", new BoundingBox(10, 10, 70, 20));
        var right = new OcrTextBlock("يمين ABC", new BoundingBox(250, 10, 100, 20));
        var raw = CreateResult(400, 200, OcrOrientationMode.Horizontal, "ar", left, right);
        var result = TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { Id = "ar", Name = "Arabic", TranslationGroupingMode = TranslationGroupingMode.NearbyBlocks });
        Assert.Equal(new[] { right.Text, left.Text }, result.TextBlocks.Select(block => block.Text));
        Assert.Equal(new[] { right.Bounds, left.Bounds }, result.TextBlocks.Select(block => block.Bounds));
        Assert.All(result.TextBlockSources, source => Assert.Single(source.MemberBounds));
    }
    private static OcrResult CreateResult(int inputWidth, int inputHeight, params OcrTextBlock[] blocks)
    {
        return CreateResult(
            inputWidth,
            inputHeight,
            OcrOrientationMode.Horizontal,
            "en",
            blocks);
    }

    private static OcrResult CreateResult(
        int inputWidth,
        int inputHeight,
        OcrOrientationMode orientationMode,
        string language,
        params OcrTextBlock[] blocks)
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
            new OcrRequest(frame, language, "comic-page", orientationMode: orientationMode),
            blocks,
            FrameTime);
    }
}
