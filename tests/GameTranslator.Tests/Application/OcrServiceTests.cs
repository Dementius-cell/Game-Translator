using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class OcrServiceTests
{
    private static readonly CaptureRegion FirstRegion = new(10, 20, 100, 40);
    private static readonly CaptureRegion SecondRegion = new(220, 120, 80, 32);
    private static readonly DateTimeOffset FrameTime = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecognizeAsync_WhenFrameHasText_ReturnsMappedTextBlocks()
    {
        var engine = new FakeOcrEngine
        {
            BlocksFactory = request => new[]
            {
                new OcrTextBlock("Start", new BoundingBox(4, 5, 24, 10)),
                new OcrTextBlock("Game", new BoundingBox(30, 18, 40, 12)),
            },
        };
        var service = new OcrService(engine);
        var request = CreateRequest(FirstRegion, "en", "zone-a");

        var result = await service.RecognizeAsync(request);

        Assert.Equal(new[] { request }, engine.Requests);
        Assert.Equal("zone-a", result.ZoneId);
        Assert.Equal(FirstRegion, result.Region);
        Assert.Equal("en", result.Language);
        Assert.Equal(100, result.InputWidth);
        Assert.Equal(40, result.InputHeight);
        Assert.Equal("Start\r\nGame", result.Text);
        Assert.Collection(
            result.TextBlocks,
            block =>
            {
                Assert.Equal("Start", block.Text);
                Assert.Equal(new BoundingBox(4, 5, 24, 10), block.Bounds);
            },
            block =>
            {
                Assert.Equal("Game", block.Text);
                Assert.Equal(new BoundingBox(30, 18, 40, 12), block.Bounds);
            });
    }

    [Fact]
    public async Task RecognizeAsync_WhenFrameHasNoText_ReturnsEmptyResult()
    {
        var engine = new FakeOcrEngine();
        var service = new OcrService(engine);

        var result = await service.RecognizeAsync(CreateRequest(FirstRegion, "en", "zone-empty"));

        Assert.Empty(result.TextBlocks);
        Assert.Equal(string.Empty, result.Text);
        Assert.Equal("zone-empty", result.ZoneId);
    }

    [Fact]
    public async Task RecognizeAsync_WhenEngineFails_PropagatesOcrEngineException()
    {
        var expected = new OcrEngineException("OCR engine is unavailable.");
        var engine = new FakeOcrEngine
        {
            Failure = expected,
        };
        var service = new OcrService(engine);

        var actual = await Assert.ThrowsAsync<OcrEngineException>(
            () => service.RecognizeAsync(CreateRequest(FirstRegion, "en", "zone-a")));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task RecognizeAsync_WhenCancellationIsRequested_DoesNotCallEngine()
    {
        var engine = new FakeOcrEngine();
        var service = new OcrService(engine);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.RecognizeAsync(CreateRequest(FirstRegion, "en", "zone-a"), cancellation.Token));

        Assert.Empty(engine.Requests);
    }

    [Fact]
    public async Task RecognizeAsync_ForMultipleZones_PreservesOrderAndZoneMetadata()
    {
        var engine = new FakeOcrEngine
        {
            BlocksFactory = request => new[]
            {
                new OcrTextBlock($"Text for {request.ZoneId}", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var service = new OcrService(engine);
        var requests = new[]
        {
            CreateRequest(FirstRegion, "en", "zone-a"),
            CreateRequest(SecondRegion, "ru", "zone-b"),
        };

        var results = await service.RecognizeAsync(requests);

        Assert.Equal(requests, engine.Requests);
        Assert.Collection(
            results,
            result =>
            {
                Assert.Equal("zone-a", result.ZoneId);
                Assert.Equal(FirstRegion, result.Region);
                Assert.Equal("en", result.Language);
                Assert.Equal("Text for zone-a", result.Text);
            },
            result =>
            {
                Assert.Equal("zone-b", result.ZoneId);
                Assert.Equal(SecondRegion, result.Region);
                Assert.Equal("ru", result.Language);
                Assert.Equal("Text for zone-b", result.Text);
            });
    }



    [Fact]
    public async Task RecognizeAsync_WhenRequestSelectsEngine_UsesMatchingRegisteredEngine()
    {
        var windowsEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.WindowsEngineId,
        };
        var tesseractEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Tesseract text", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var service = new OcrService(new IOcrEngine[] { windowsEngine, tesseractEngine });
        var request = new OcrRequest(
            CreateFrame(FirstRegion),
            "en",
            "zone-a",
            preprocessingSettings: null,
            OcrSettings.TesseractEngineId,
            OcrOrientationMode.Vertical);

        var result = await service.RecognizeAsync(request);

        Assert.Empty(windowsEngine.Requests);
        var engineRequest = Assert.Single(tesseractEngine.Requests);
        Assert.Equal(OcrSettings.TesseractEngineId, engineRequest.EngineId);
        Assert.Equal(OcrOrientationMode.Vertical, engineRequest.OrientationMode);
        Assert.Equal("Tesseract text", result.Text);
    }

    [Fact]
    public async Task RecognizeAsync_WithPreprocessingSettings_PassesPreprocessedFrameToEngine()
    {
        var engine = new FakeOcrEngine();
        var service = new OcrService(engine, new OcrPreprocessor());
        var request = new OcrRequest(
            CreateFrame(FirstRegion),
            "en",
            "zone-a",
            new OcrPreprocessingSettings
            {
                IsEnabled = true,
                Scale = 2,
            },
            orientationMode: OcrOrientationMode.Horizontal);

        await service.RecognizeAsync(request);

        var engineRequest = Assert.Single(engine.Requests);
        Assert.Equal(200, engineRequest.Frame.Width);
        Assert.Equal(80, engineRequest.Frame.Height);
        Assert.Equal(request.PreprocessingSettings, engineRequest.PreprocessingSettings);
        Assert.Equal(OcrOrientationMode.Horizontal, engineRequest.OrientationMode);
    }
    [Fact]
    public void OcrResult_WhenTextBlockExceedsFrame_ThrowsArgumentException()
    {
        var request = CreateRequest(FirstRegion, "en", "zone-a");
        var block = new OcrTextBlock("overflow", new BoundingBox(90, 30, 20, 11));

        Assert.Throws<ArgumentException>(() => new OcrResult(request, new[] { block }, FrameTime));
    }

    [Theory]
    [InlineData(-1, 0, 10, 10)]
    [InlineData(0, -1, 10, 10)]
    [InlineData(0, 0, 0, 10)]
    [InlineData(0, 0, 10, 0)]
    public void BoundingBox_WhenBoundsAreInvalid_ThrowsArgumentOutOfRangeException(
        int x,
        int y,
        int width,
        int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundingBox(x, y, width, height));
    }

    [Fact]
    public void OcrRequest_WhenLanguageIsEmpty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new OcrRequest(CreateFrame(FirstRegion), string.Empty, "zone-a"));
    }

    private static OcrRequest CreateRequest(CaptureRegion region, string language, string zoneId)
    {
        return new OcrRequest(CreateFrame(region), language, zoneId);
    }

    private static CapturedFrame CreateFrame(CaptureRegion region)
    {
        var stride = checked(region.Width * 4);
        var pixels = Enumerable
            .Repeat((byte)42, checked(stride * region.Height))
            .ToArray();

        return new CapturedFrame(
            region,
            region.Width,
            region.Height,
            stride,
            "Bgra32",
            pixels,
            FrameTime);
    }

    private sealed class FakeOcrEngine : IOcrEngine
    {
        public string EngineId { get; init; } = OcrSettings.WindowsEngineId;

        public List<OcrRequest> Requests { get; } = new();

        public Exception? Failure { get; init; }

        public Func<OcrRequest, IReadOnlyList<OcrTextBlock>>? BlocksFactory { get; init; }

        public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Failure is not null)
            {
                return Task.FromException<OcrResult>(Failure);
            }

            Requests.Add(request);

            var blocks = BlocksFactory?.Invoke(request) ?? Array.Empty<OcrTextBlock>();
            return Task.FromResult(new OcrResult(request, blocks, FrameTime.AddMilliseconds(Requests.Count)));
        }
    }
}
