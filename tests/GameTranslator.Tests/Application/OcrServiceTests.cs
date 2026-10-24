using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class OcrServiceTests
{
    private static readonly CaptureRegion FirstRegion = new(10, 20, 100, 40);
    private static readonly CaptureRegion SecondRegion = new(220, 120, 80, 32);
    private static readonly DateTimeOffset FrameTime = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task Preparation_PreservesSourceFrameOriginAndCachedReuse(double scale, bool border)
    {
        var origin = new CaptureFrameOrigin(Guid.NewGuid(), TimeSpan.FromSeconds(42), FrameTime);
        var frame = new CapturedFrame(FirstRegion, 100, 40, 400, "Bgra32", new byte[16000], FrameTime,
            origin, isCachedReuse: true);
        var engine = new FakeOcrEngine();
        var service = new OcrService(engine);
        await service.RecognizeAsync(new OcrRequest(frame, "en", "origin", new OcrPreprocessingSettings
        {
            Mode = OcrPreprocessingMode.Manual, Brightness = 10, Scale = scale, AddWhiteBorder = border,
        }));
        var prepared = Assert.Single(engine.Requests).Frame;
        Assert.Equal((int)(100 * scale) + (border ? 20 : 0), prepared.Width);
        Assert.Same(origin, prepared.Origin);
        Assert.True(prepared.IsCachedReuse);
        Assert.Equal(FrameTime, prepared.CapturedAt);
    }

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
        Assert.Equal(OcrLayoutMode.Auto, engineRequest.LayoutMode);
    }

    [Fact]
    public async Task RecognizeAsync_WithPreprocessingSettings_PreservesLayoutModeForTheEngine()
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
            layoutMode: OcrLayoutMode.Comic);

        await service.RecognizeAsync(request);

        var engineRequest = Assert.Single(engine.Requests);
        Assert.Equal(OcrLayoutMode.Comic, engineRequest.LayoutMode);
    }

    [Fact]
    public async Task RecognizeAsync_WithPreprocessingSettings_PreservesContentLayoutModeForTheEngine()
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
            contentLayoutMode: ContentLayoutMode.DialogComic,
            candidateGroupingSettings: new OcrCandidateGroupingSettings
            {
                MaximumHorizontalLines = 9,
            })
        {
            DetectorPreset = TextCandidateDetectorPreset.ChineseExperimental,
        };

        await service.RecognizeAsync(request);

        var engineRequest = Assert.Single(engine.Requests);
        Assert.Equal(ContentLayoutMode.DialogComic, engineRequest.ContentLayoutMode);
        Assert.Equal(9, engineRequest.CandidateGroupingSettings.MaximumHorizontalLines);
        Assert.Equal(TextCandidateDetectorPreset.ChineseExperimental, engineRequest.DetectorPreset);
    }

    [Fact]
    public void OcrResult_WhenTextBlockExceedsFrame_ThrowsArgumentException()
    {
        var request = CreateRequest(FirstRegion, "en", "zone-a");
        var block = new OcrTextBlock("overflow", new BoundingBox(90, 30, 20, 11));

        Assert.Throws<ArgumentException>(() => new OcrResult(request, new[] { block }, FrameTime));
    }

    [Fact]
    public void OcrResult_WithOptionalWordMetadata_PreservesEngineReportedGeometryAndQuality()
    {
        var request = CreateRequest(FirstRegion, "en", "zone-a");
        var word = new OcrWord(
            "Start",
            new BoundingBox(4, 5, 24, 10),
            confidence: 96.25,
            recognitionPassId: "tesseract:SingleBlock");

        var result = new OcrResult(
            request,
            new[] { new OcrTextBlock("Start", new BoundingBox(4, 5, 24, 10)) },
            FrameTime,
            words: new[] { word });

        var actual = Assert.Single(result.Words);
        Assert.Equal("Start", actual.Text);
        Assert.Equal(new BoundingBox(4, 5, 24, 10), actual.Bounds);
        Assert.Equal(96.25, actual.Confidence);
        Assert.Equal("tesseract:SingleBlock", actual.RecognitionPassId);
    }

    [Fact]
    public void OcrResult_WhenWordExceedsFrame_ThrowsArgumentException()
    {
        var request = CreateRequest(FirstRegion, "en", "zone-a");
        var word = new OcrWord(
            "overflow",
            new BoundingBox(90, 30, 20, 11),
            confidence: null,
            recognitionPassId: "tesseract:SingleBlock");

        Assert.Throws<ArgumentException>(
            () => new OcrResult(request, Array.Empty<OcrTextBlock>(), FrameTime, words: new[] { word }));
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

    [Fact]
    public async Task WhiteBorder_WithDownscale_RestoresCropCoordinatesAndDetectorHints()
    {
        var engine = new FakeOcrEngine { BlocksFactory = request => new[]
        {
            new OcrTextBlock("inside", new BoundingBox(12, 13, 10, 5)),
            new OcrTextBlock("padding only", new BoundingBox(0, 0, 4, 4)),
        } };
        var request = new OcrRequest(CreateFrame(FirstRegion), "en", "zone-a",
            new OcrPreprocessingSettings { IsEnabled = true, Scale = .5, AddWhiteBorder = true })
            { HasMenuValueRowEvidence = true, DetectorLineBounds = new[] { new BoundingBox(4, 6, 20, 10) } };
        var result = await new OcrService(engine).RecognizeAsync(request);
        var processed = Assert.Single(engine.Requests);
        Assert.True(processed.HasMenuValueRowEvidence);
        Assert.True(result.Request.HasMenuValueRowEvidence);
        Assert.Equal(70, processed.Frame.Width);
        Assert.Equal(40, processed.Frame.Height);
        Assert.Equal(new BoundingBox(12, 13, 10, 5), Assert.Single(processed.DetectorLineBounds));
        Assert.Equal(255, processed.Frame.PixelData.Span[0]);
        Assert.Equal(50, result.InputWidth);
        Assert.Equal(20, result.InputHeight);
        Assert.Equal(FirstRegion, result.Region);
        Assert.Equal("zone-a", result.ZoneId);
        Assert.Equal(new BoundingBox(2, 3, 10, 5), Assert.Single(result.TextBlocks).Bounds);
        Assert.Equal(new BoundingBox(2, 3, 10, 5), Assert.Single(result.TextBlockSources).SemanticBounds);
    }

    [Fact]
    public async Task WhiteBorder_RestoresWordsAndVerticalSources()
    {
        var request = new OcrRequest(CreateFrame(FirstRegion), "ja", "zone-a",
            new OcrPreprocessingSettings { IsEnabled = true, AddWhiteBorder = true });
        var bounds = new BoundingBox(10, 10, 20, 20);
        var engine = new FakeOcrEngine { ResultFactory = padded => new OcrResult(padded,
            new[] { new OcrTextBlock("text", bounds) }, FrameTime,
            new[] { new OcrTextBlockSource(bounds, new[] { bounds }, OcrOrientationMode.Vertical) { CandidateRegionBounds = bounds } },
            new[] { new OcrWord("word", bounds, 92, "test-pass") }) };
        var restored = await new OcrService(engine).RecognizeAsync(request);
        Assert.Equal(new BoundingBox(0, 0, 20, 20), Assert.Single(restored.Words).Bounds);
        Assert.Equal(92, restored.Words[0].Confidence);
        Assert.Equal("test-pass", restored.Words[0].RecognitionPassId);
        Assert.Equal(OcrOrientationMode.Vertical, restored.TextBlockSources[0].OrientationMode);
        Assert.Equal(new BoundingBox(0, 0, 20, 20), restored.TextBlockSources[0].CandidateRegionBounds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecognizeAsync_DoesNotRequireCallingDispatcher(bool border)
    {
        var context = new NonPumpingContext();
        var previous = SynchronizationContext.Current;
        Task<OcrResult> pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            pending = new OcrService(new AsyncOcrEngine()).RecognizeAsync(new OcrRequest(CreateFrame(FirstRegion), "en",
                preprocessingSettings: new OcrPreprocessingSettings { IsEnabled = border, AddWhiteBorder = border }));
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, context.PostCount);
    }

    private sealed class NonPumpingContext : SynchronizationContext
    {
        public int PostCount;
        public override void Post(SendOrPostCallback callback, object? state) => Interlocked.Increment(ref PostCount);
    }

    private sealed class AsyncOcrEngine : IOcrEngine
    {
        public string EngineId => OcrSettings.WindowsEngineId;
        public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(30, cancellationToken).ConfigureAwait(false);
            return new OcrResult(request, Array.Empty<OcrTextBlock>(), FrameTime);
        }
    }

    [Fact]
    public async Task AdaptiveMode_IgnoresManualFiltersAndPreservesLanguageAndDirection()
    {
        var frame = CreateFrame(FirstRegion);
        var engine = new FakeOcrEngine();
        var service = new OcrService(engine);
        var aggressive = new OcrPreprocessingSettings { Mode = OcrPreprocessingMode.Adaptive,
            IsEnabled = true, Contrast = 3, Brightness = 100, Sharpness = 2, Scale = 3,
            InvertColors = true, AdaptiveThresholding = true, ThresholdingEnabled = true,
            NoiseReductionEnabled = true, AddWhiteBorder = true };
        await service.RecognizeAsync(new OcrRequest(frame, "ja", preprocessingSettings: aggressive,
            orientationMode: OcrOrientationMode.Vertical));
        await service.RecognizeAsync(new OcrRequest(frame, "ja", preprocessingSettings: new() { Mode = OcrPreprocessingMode.Adaptive },
            orientationMode: OcrOrientationMode.Vertical));
        Assert.Equal(engine.Requests[0].Frame.PixelData.ToArray(), engine.Requests[1].Frame.PixelData.ToArray());
        Assert.Equal(frame.Width, engine.Requests[0].Frame.Width);
        Assert.Equal("ja", engine.Requests[0].Language);
        Assert.Equal(OcrOrientationMode.Vertical, engine.Requests[0].OrientationMode);
    }

    [Fact]
    public async Task ExplicitNone_IgnoresAllRetainedManualOptions()
    {
        var frame = CreateFrame(FirstRegion);
        var engine = new FakeOcrEngine();
        await new OcrService(engine).RecognizeAsync(new OcrRequest(frame, "en", preprocessingSettings: new()
            { Mode = OcrPreprocessingMode.None, IsEnabled = true, Scale = 3, InvertColors = true, AddWhiteBorder = true }));
        Assert.Same(frame, Assert.Single(engine.Requests).Frame);
    }

    [Fact]
    public async Task AdaptiveMode_DetectsLightTextOnDarkBackgroundAndAddsCropBorder()
    {
        var pixels = new byte[100 * 40 * 4];
        for (var y = 0; y < 40; y++)
        for (var x = 0; x < 100; x++)
        {
            var offset = (y * 100 + x) * 4;
            var value = x is >= 20 and < 80 && y is >= 10 and < 30 ? (byte)255 : (byte)0;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
        var frame = new CapturedFrame(FirstRegion, 100, 40, 400, "Bgra32", pixels, FrameTime);
        var engine = new FakeOcrEngine();
        await new OcrService(engine).RecognizeAsync(new OcrRequest(frame, "en",
            preprocessingSettings: new() { Mode = OcrPreprocessingMode.Adaptive })
            { DetectorLineBounds = new[] { new BoundingBox(20, 10, 60, 20) } });
        var actual = Assert.Single(engine.Requests);
        Assert.True(actual.PreprocessingSettings.InvertColors);
        Assert.False(actual.PreprocessingSettings.NoiseReductionEnabled);
        Assert.Equal(120, actual.Frame.Width);
        Assert.Equal(255, actual.Frame.PixelData.Span[(10 * 120 + 10) * 4]);
        Assert.Equal(0, actual.Frame.PixelData.Span[(25 * 120 + 35) * 4]);
        Assert.Equal(0, frame.PixelData.Span[0]);
    }

    [Theory]
    [InlineData(50, true, true, 1, OcrPreprocessingMode.Adaptive, true)]
    [InlineData(100, true, true, 1, OcrPreprocessingMode.Adaptive, true)]
    [InlineData(200, true, true, 1, OcrPreprocessingMode.Adaptive, true)]
    [InlineData(300, true, true, 1, OcrPreprocessingMode.Adaptive, true)]
    [InlineData(100, false, true, 1, OcrPreprocessingMode.Adaptive, false)]
    [InlineData(100, true, false, 1, OcrPreprocessingMode.Adaptive, false)]
    [InlineData(100, true, true, 2, OcrPreprocessingMode.Adaptive, false)]
    [InlineData(100, true, true, 1, OcrPreprocessingMode.None, false)]
    [InlineData(100, true, true, 1, OcrPreprocessingMode.Manual, false)]
    public async Task AdaptiveMenu_BrightCoreSelectionIsRelativeAndScoped(
        int width, bool menu, bool colored, int lineCount, OcrPreprocessingMode mode, bool selected)
    {
        var height = width * 2 / 5;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            var white = x >= width * .2 && x < width * .5 && y >= height * .25 && y < height * .75;
            pixels[i] = white ? (byte)255 : colored ? (byte)200 : (byte)50;
            pixels[i + 1] = white ? (byte)255 : (byte)50;
            pixels[i + 2] = white ? (byte)255 : colored ? (byte)10 : (byte)50;
            pixels[i + 3] = 255;
        }
        var frame = new CapturedFrame(new(0, 0, width, height), width, height, width * 4, "Bgra32", pixels, FrameTime);
        var engine = new FakeOcrEngine();
        await new OcrService(engine).RecognizeAsync(new OcrRequest(frame, "en",
            preprocessingSettings: new() { Mode = mode }, orientationMode: OcrOrientationMode.Horizontal,
            contentLayoutMode: menu ? ContentLayoutMode.StaticMenu : ContentLayoutMode.DialogComic)
        {
            DetectorLineBounds = Enumerable.Range(0, lineCount)
                .Select(i => new BoundingBox(0, i * height / lineCount, width, height / lineCount)).ToArray(),
        });
        var actual = Assert.Single(engine.Requests);
        Assert.Equal(selected, actual.PreprocessingSettings.ThresholdingEnabled);
        if (selected)
        {
            Assert.True(actual.PreprocessingSettings.InvertColors);
            Assert.Equal(10, actual.PreprocessingSettings.Threshold);
            Assert.Equal(width + 20, actual.Frame.Width);
        }
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

        public Func<OcrRequest, OcrResult>? ResultFactory { get; init; }

        public Func<OcrRequest, IReadOnlyList<OcrTextBlock>>? BlocksFactory { get; init; }

        public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Failure is not null)
            {
                return Task.FromException<OcrResult>(Failure);
            }

            Requests.Add(request);

            if (ResultFactory is not null) return Task.FromResult(ResultFactory(request));
            var blocks = BlocksFactory?.Invoke(request) ?? Array.Empty<OcrTextBlock>();
            return Task.FromResult(new OcrResult(request, blocks, FrameTime.AddMilliseconds(Requests.Count)));
        }
    }
}
