using GameTranslator.Application.Cache;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Credentials;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Pipeline;
using GameTranslator.Application.Translation;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class TranslationPipelineServiceTests
{
    private static readonly DateTimeOffset FrameTime = new(2026, 6, 19, 12, 0, 1, TimeSpan.Zero);
    private static readonly DateTimeOffset OcrTime = new(2026, 6, 19, 12, 0, 2, TimeSpan.Zero);
    private static readonly DateTimeOffset TranslatedAt = new(2026, 6, 19, 12, 0, 3, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_WhenTextIsRecognized_TranslatesAndShowsOverlay()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var frameSource = new FakeCaptureFrameSource();
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google", new[] { "Привет" });
        var overlay = new FakeOverlayService();
        var service = CreateService(frameSource, ocrEngine, translator, overlay);

        var result = await service.RunAsync(profile, zone);

        Assert.Equal(new[] { new CaptureRegion(10, 20, 100, 40) }, frameSource.CapturedRegions);
        var request = Assert.Single(ocrEngine.Requests);
        Assert.Equal("en", request.Language);
        Assert.Equal(zone.Id, request.ZoneId);
        Assert.Equal(profile.OcrSettings.OrientationMode, request.OrientationMode);
        Assert.Equal(new[] { "Hello" }, translator.Request?.Texts);
        Assert.Equal("en", translator.Request?.SourceLanguage);
        Assert.Equal("ru", translator.Request?.TargetLanguage);
        Assert.Equal("SECRET_ACCESS_TOKEN", translator.Request?.Credentials.AccessToken);
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
        Assert.True(overlay.IsVisible);
        Assert.Equal(profile.OverlaySettings, result.OverlaySnapshot.OverlaySettings);

        var item = Assert.Single(result.OverlaySnapshot.TextItems);
        Assert.Equal("Привет", item.Text);
        Assert.Equal(14, item.X);
        Assert.Equal(25, item.Y);
        Assert.Equal(24, item.Width);
        Assert.Equal(10, item.Height);
        Assert.Equal(1, result.RecognizedBlockCount);
        Assert.Equal(1, result.TranslatedBlockCount);
    }

    [Fact]
    public async Task RunAsync_WhenChineseVerticalProfileUsesTesseract_PassesLanguageAndOrientationToOcr()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone) with
        {
            OcrSettings = new OcrSettings
            {
                Engine = OcrSettings.TesseractEngineId,
                OrientationMode = OcrOrientationMode.Vertical,
            },
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "zh-CN",
                TargetLanguage = "en",
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Column text", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var service = CreateService(
            new FakeCaptureFrameSource(),
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            new FakeOverlayService());

        await service.RunAsync(profile, zone);

        var request = Assert.Single(ocrEngine.Requests);
        Assert.Equal(OcrSettings.TesseractEngineId, request.EngineId);
        Assert.Equal("zh-CN", request.Language);
        Assert.Equal(OcrOrientationMode.Vertical, request.OrientationMode);
    }

    [Fact]
    public async Task RunAsync_WhenOcrFindsNoText_SkipsTranslationAndShowsEmptyOverlay()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var translator = new FakeTranslatorProvider("Google", new[] { "unused" });
        var overlay = new FakeOverlayService();
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine(),
            translator,
            overlay,
            credentialStorage: new FakeCredentialStorage());

        var result = await service.RunAsync(profile, zone);

        Assert.Null(translator.Request);
        Assert.Null(result.TranslateResponse);
        Assert.Empty(result.OverlaySnapshot.TextItems);
        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public async Task RunAsync_WhenExperimentalWebProviderIsSelected_DoesNotRequireStoredCredentials()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "GoogleWeb",
                SourceLanguage = "en",
                TargetLanguage = "ru",
            },
        };
        var translator = new FakeTranslatorProvider("GoogleWeb", new[] { "Привет" });
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
                },
            },
            translator,
            new FakeOverlayService(),
            credentialStorage: new FakeCredentialStorage());

        var result = await service.RunAsync(profile, zone);

        Assert.Equal("experimental-web-provider", translator.Request?.Credentials.AccessToken);
        Assert.Equal("GoogleWeb", translator.Request?.Credentials.ProjectId);
        Assert.Equal(new Uri("https://translate.googleapis.com"), translator.Request?.Credentials.Endpoint);
        Assert.Equal("Привет", Assert.Single(result.OverlaySnapshot.TextItems).Text);
    }

    [Fact]
    public async Task RunAsync_WhenTranslationIsCached_SkipsTranslatorProviderOnRepeatedRun()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var translator = new FakeTranslatorProvider("Google", new[] { "Привет" });
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
                },
            },
            translator,
            new FakeOverlayService());

        var first = await service.RunAsync(profile, zone);
        var second = await service.RunAsync(profile, zone, first.OverlaySnapshot);

        Assert.Equal(1, translator.CallCount);
        Assert.Equal(1, second.CacheResult?.MemoryHitCount);
        Assert.Equal(0, second.CacheResult?.MissCount);
        Assert.Equal(new[] { "Привет" }, second.TranslateResponse?.TranslatedTexts);
    }

    [Fact]
    public async Task RunAsync_WhenFrameIsEffectivelyUnchanged_ReusesPreviousPipelineResult()
    {
        var zone = CreateZone();
        var firstPixels = CreatePixels(zone, 42);
        var secondPixels = firstPixels.ToArray();
        secondPixels[0] = 43;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[] { firstPixels, secondPixels },
            CapturedAtFrames = new[] { FrameTime, FrameTime.AddMilliseconds(20) },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google", new[] { "Salut" });
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            optimizationOptions: new TranslationPipelineOptimizationOptions(
                frameDifferenceThreshold: 0.001d,
                debounceInterval: TimeSpan.FromMilliseconds(250)));

        var first = await service.RunAsync(CreateProfile(zone), zone);
        var second = await service.RunAsync(CreateProfile(zone), zone, first.OverlaySnapshot);

        Assert.Equal(2, frameSource.CapturedRegions.Count);
        Assert.Single(ocrEngine.Requests);
        Assert.Equal(1, translator.CallCount);
        Assert.True(second.Optimization.OcrSkipped);
        Assert.True(second.Optimization.TranslationSkipped);
        Assert.True(second.Optimization.Debounced);
        Assert.True(second.Optimization.FrameDifferenceRatio <= 0.001d);
        Assert.Equal(TimeSpan.Zero, second.Timings.OcrElapsed);
        Assert.Equal(TimeSpan.Zero, second.Timings.TranslationElapsed);
        Assert.Equal(new[] { "Salut" }, second.TranslateResponse?.TranslatedTexts);
    }

    [Fact]
    public async Task RunAsync_WhenFrameChangesBeyondThreshold_RunsOcrAndTranslationAgain()
    {
        var zone = CreateZone();
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreatePixels(zone, 42),
                CreatePixels(zone, 200),
            },
            CapturedAtFrames = new[] { FrameTime, FrameTime.AddMilliseconds(20) },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 42 ? "Hello" : "World",
                    new BoundingBox(4, 5, 24, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            optimizationOptions: new TranslationPipelineOptimizationOptions(
                frameDifferenceThreshold: 0.001d,
                debounceInterval: TimeSpan.FromMilliseconds(250)));

        var first = await service.RunAsync(CreateProfile(zone), zone);
        var second = await service.RunAsync(CreateProfile(zone), zone, first.OverlaySnapshot);

        Assert.Equal(2, ocrEngine.Requests.Count);
        Assert.Equal(2, translator.CallCount);
        Assert.False(second.Optimization.OcrSkipped);
        Assert.False(second.Optimization.TranslationSkipped);
        Assert.NotNull(second.Optimization.FrameDifferenceRatio);
        Assert.True(second.Optimization.FrameDifferenceRatio > 0.001d);
        Assert.Equal(new[] { "Translated World" }, second.TranslateResponse?.TranslatedTexts);
    }

    [Fact]
    public async Task RunAsync_WhenStableTextIsRequired_WaitsBeforeTranslation()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var frameSource = new FakeCaptureFrameSource
        {
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime.AddMilliseconds(500),
                FrameTime.AddMilliseconds(1100),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            RecognizedAtFrames = new[]
            {
                OcrTime,
                OcrTime.AddMilliseconds(500),
                OcrTime.AddMilliseconds(1100),
            },
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            optimizationOptions: new TranslationPipelineOptimizationOptions());
        var runOptions = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.FromSeconds(1));

        var first = await service.RunAsync(profile, zone, runOptions: runOptions);
        var second = await service.RunAsync(profile, zone, first.OverlaySnapshot, runOptions);
        var third = await service.RunAsync(profile, zone, second.OverlaySnapshot, runOptions);

        Assert.Equal(3, frameSource.CapturedRegions.Count);
        Assert.Equal(3, ocrEngine.Requests.Count);
        Assert.Null(first.TranslateResponse);
        Assert.Null(second.TranslateResponse);
        Assert.True(first.Optimization.TranslationSkipped);
        Assert.True(second.Optimization.TranslationSkipped);
        Assert.Empty(first.OverlaySnapshot.TextItems);
        Assert.Empty(second.OverlaySnapshot.TextItems);
        Assert.Equal(1, translator.CallCount);
        Assert.Equal(new[] { "Translated Hello" }, third.TranslateResponse?.TranslatedTexts);
        Assert.Equal("Translated Hello", Assert.Single(third.OverlaySnapshot.TextItems).Text);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenStableTextIsPending_CanPreservePreviousOverlay()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var previousSnapshot = new OverlaySnapshot(
            new[] { new OverlayTextItem("Previous translation", 1, 2, 30, 12) },
            FrameTime);
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google");
        var overlay = new FakeOverlayService();
        var service = CreateService(
            new FakeCaptureFrameSource(),
            ocrEngine,
            translator,
            overlay,
            optimizationOptions: new TranslationPipelineOptimizationOptions());
        var runOptions = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.FromSeconds(1),
            preservePreviousOverlayWhileWaitingForStableText: true);

        var result = await service.RunAllZonesAsync(profile, previousSnapshot, runOptions);

        Assert.Same(previousSnapshot, result.OverlaySnapshot);
        Assert.Same(previousSnapshot, overlay.CurrentSnapshot);
        Assert.Equal(1, result.RecognizedBlockCount);
        Assert.Equal(0, result.TranslatedBlockCount);
        Assert.Equal(1, result.SkippedTranslationCount);
        Assert.Equal(0, translator.CallCount);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenLiveOcrFindsNoText_PreservesPreviousOverlay()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var previousSnapshot = new OverlaySnapshot(
            new[] { new OverlayTextItem("Previous translation", 1, 2, 30, 12) },
            FrameTime);
        var translator = new FakeTranslatorProvider("Google");
        var overlay = new FakeOverlayService();
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine(),
            translator,
            overlay,
            optimizationOptions: new TranslationPipelineOptimizationOptions());
        var runOptions = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.FromMilliseconds(300),
            preservePreviousOverlayWhileWaitingForStableText: true);

        var result = await service.RunAllZonesAsync(profile, previousSnapshot, runOptions);

        Assert.Same(previousSnapshot, result.OverlaySnapshot);
        Assert.Same(previousSnapshot, overlay.CurrentSnapshot);
        Assert.Equal(1, result.SucceededZoneCount);
        Assert.Equal(0, result.RecognizedBlockCount);
        Assert.Equal(0, result.TranslatedBlockCount);
        Assert.Equal(0, translator.CallCount);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenAllZonesFailInLiveMode_PreservesPreviousOverlay()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var previousSnapshot = new OverlaySnapshot(
            new[] { new OverlayTextItem("Previous translation", 1, 2, 30, 12) },
            FrameTime);
        var overlay = new FakeOverlayService();
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
                },
            },
            new FakeTranslatorProvider("Google"),
            overlay,
            new FakeCredentialStorage(),
            new TranslationPipelineOptimizationOptions());
        var runOptions = new TranslationPipelineRunOptions(
            preservePreviousOverlayWhileWaitingForStableText: true);

        var result = await service.RunAllZonesAsync(profile, previousSnapshot, runOptions);

        Assert.Same(previousSnapshot, result.OverlaySnapshot);
        Assert.Same(previousSnapshot, overlay.CurrentSnapshot);
        Assert.Empty(result.ZoneResults);
        Assert.Single(result.ZoneFailures);
    }

    [Fact]
    public async Task RunAsync_WhenCjkOcrWhitespaceChanges_TreatsTextAsStable()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "zh-CN",
                TargetLanguage = "ru",
            },
        };
        var recognizedTexts = new[] { "你 好", "你好" };
        var recognizedTextIndex = 0;
        var ocrEngine = new FakeOcrEngine
        {
            RecognizedAtFrames = new[]
            {
                OcrTime,
                OcrTime.AddMilliseconds(500),
            },
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock(recognizedTexts[recognizedTextIndex++], new BoundingBox(4, 5, 24, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            new FakeCaptureFrameSource(),
            ocrEngine,
            translator,
            new FakeOverlayService(),
            optimizationOptions: new TranslationPipelineOptimizationOptions());
        var runOptions = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.FromMilliseconds(300));

        var first = await service.RunAsync(profile, zone, runOptions: runOptions);
        var second = await service.RunAsync(profile, zone, first.OverlaySnapshot, runOptions);

        Assert.Null(first.TranslateResponse);
        Assert.True(first.Optimization.TranslationSkipped);
        Assert.Equal(1, translator.CallCount);
        Assert.Equal(new[] { "你好" }, translator.Request?.Texts);
        Assert.Equal("Translated 你好", Assert.Single(second.OverlaySnapshot.TextItems).Text);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenRestorePreviousOverlayAfterCapture_RestoresOverlayDuringOcrAndTranslation()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var previousSnapshot = new OverlaySnapshot(
            new[] { new OverlayTextItem("Previous translation", 1, 2, 30, 12) },
            FrameTime);
        var overlay = new FakeOverlayService();
        overlay.Show(previousSnapshot);
        var frameSource = new FakeCaptureFrameSource
        {
            OnCapture = () => Assert.False(overlay.IsVisible),
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ =>
            {
                Assert.True(overlay.IsVisible);
                Assert.Same(previousSnapshot, overlay.CurrentSnapshot);

                return new[]
                {
                    new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
                };
            },
        };
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google", new[] { "New translation" }),
            overlay);
        var runOptions = new TranslationPipelineRunOptions(
            restorePreviousOverlayAfterCapture: true);

        var result = await service.RunAllZonesAsync(profile, previousSnapshot, runOptions);

        Assert.Equal(new[] { "Show:1", "Hide", "Show:1", "Show:1" }, overlay.Events);
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
        Assert.Equal("New translation", Assert.Single(result.OverlaySnapshot.TextItems).Text);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenOverlayIsExcludedFromCapture_KeepsOverlayVisibleDuringCapture()
    {
        var zone = CreateZone();
        var profile = CreateProfile(zone);
        var previousSnapshot = new OverlaySnapshot(
            new[] { new OverlayTextItem("Previous translation", 1, 2, 30, 12) },
            FrameTime);
        var overlay = new FakeOverlayService
        {
            IsExcludedFromCapture = true,
        };
        overlay.Show(previousSnapshot);
        var frameSource = new FakeCaptureFrameSource
        {
            OnCapture = () =>
            {
                Assert.True(overlay.IsVisible);
                Assert.Same(previousSnapshot, overlay.CurrentSnapshot);
            },
        };
        var service = CreateService(
            frameSource,
            new FakeOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Hello", new BoundingBox(4, 5, 24, 10)),
                },
            },
            new FakeTranslatorProvider("Google", new[] { "New translation" }),
            overlay);
        var runOptions = new TranslationPipelineRunOptions(
            restorePreviousOverlayAfterCapture: true);

        var result = await service.RunAllZonesAsync(profile, previousSnapshot, runOptions);

        Assert.Equal(new[] { "Show:1", "Show:1" }, overlay.Events);
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
        Assert.Equal("New translation", Assert.Single(result.OverlaySnapshot.TextItems).Text);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenProfileHasMultipleZones_ProcessesEachZoneAndShowsCombinedOverlay()
    {
        var firstZone = CreateZone("zone-a", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var secondZone = CreateZone("zone-b", "Choice", new AbsoluteRectangle(200, 120, 80, 30));
        var profile = CreateProfile(firstZone, secondZone);
        var frameSource = new FakeCaptureFrameSource();
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = request => new[]
            {
                new OcrTextBlock($"Text {request.ZoneId}", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            overlay);

        var result = await service.RunAllZonesAsync(profile);

        Assert.Equal(
            new[]
            {
                new CaptureRegion(10, 20, 100, 40),
                new CaptureRegion(200, 120, 80, 30),
            },
            frameSource.CapturedRegions);
        Assert.Equal(new[] { "zone-a", "zone-b" }, ocrEngine.Requests.Select(request => request.ZoneId));
        Assert.Equal(2, result.ZoneResults.Count);
        Assert.Empty(result.ZoneFailures);
        Assert.Equal(2, result.RecognizedBlockCount);
        Assert.Equal(2, result.TranslatedBlockCount);
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
        Assert.True(overlay.IsVisible);
        Assert.Equal(profile.OverlaySettings, result.OverlaySnapshot.OverlaySettings);
        Assert.Equal(
            new[] { "Translated Text zone-a", "Translated Text zone-b" },
            result.OverlaySnapshot.TextItems.Select(item => item.Text));
        Assert.Equal(new[] { 10, 200 }, result.OverlaySnapshot.TextItems.Select(item => item.X));
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenOneZoneFails_ContinuesWithSuccessfulZonesAndReportsFailure()
    {
        var firstZone = CreateZone("zone-a", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var secondZone = CreateZone("zone-b", "Choice", new AbsoluteRectangle(200, 120, 80, 30));
        var profile = CreateProfile(firstZone, secondZone);
        var ocrFailure = new OcrEngineException("OCR unavailable for this zone.");
        var ocrEngine = new FakeOcrEngine
        {
            FailureFactory = request => string.Equals(request.ZoneId, secondZone.Id, StringComparison.Ordinal)
                ? ocrFailure
                : null,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Hello", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var overlay = new FakeOverlayService();
        var service = CreateService(
            new FakeCaptureFrameSource(),
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            overlay);

        var result = await service.RunAllZonesAsync(profile);

        var zoneResult = Assert.Single(result.ZoneResults);
        Assert.Equal(firstZone.Id, zoneResult.ZoneId);
        var failure = Assert.Single(result.ZoneFailures);
        Assert.Equal(secondZone.Id, failure.ZoneId);
        Assert.Equal(secondZone.Name, failure.ZoneName);
        Assert.Equal(TranslationPipelineStage.Ocr, failure.Stage);
        Assert.Same(ocrFailure, failure.Exception.InnerException);
        Assert.Equal("Translated Hello", Assert.Single(result.OverlaySnapshot.TextItems).Text);
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
        Assert.True(result.HasFailures);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenCredentialsFailAfterOcr_ReportsPartialOcrForEachZone()
    {
        var firstZone = CreateZone("zone-a", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var secondZone = CreateZone("zone-b", "Choice", new AbsoluteRectangle(200, 120, 80, 30));
        var profile = CreateProfile(firstZone, secondZone);
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = request => new[]
            {
                new OcrTextBlock($"Text {request.ZoneId}", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var overlay = new FakeOverlayService();
        var service = CreateService(
            new FakeCaptureFrameSource(),
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            overlay,
            new FakeCredentialStorage());

        var result = await service.RunAllZonesAsync(profile);

        Assert.Empty(result.ZoneResults);
        Assert.Equal(new[] { "zone-a", "zone-b" }, ocrEngine.Requests.Select(request => request.ZoneId));
        Assert.Equal(new[] { "zone-a", "zone-b" }, result.ZoneFailures.Select(failure => failure.ZoneId));
        Assert.All(result.ZoneFailures, failure =>
        {
            Assert.Equal(TranslationPipelineStage.Credentials, failure.Stage);
            Assert.NotNull(failure.CapturedFrame);
            var sourceResult = Assert.IsType<OcrResult>(failure.SourceOcrResult);
            Assert.Single(sourceResult.TextBlocks);
            Assert.Equal(1, failure.RecognizedBlockCount);
        });
        Assert.Equal(2, result.RecognizedBlockCount);
        Assert.Empty(result.OverlaySnapshot.TextItems);
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public async Task RunAsync_WhenCaptureFails_WrapsStageFailure()
    {
        var zone = CreateZone();
        var expected = new CaptureFrameSourceException("Capture source unavailable.");
        var service = CreateService(
            new FakeCaptureFrameSource { Failure = expected },
            new FakeOcrEngine(),
            new FakeTranslatorProvider("Google"),
            new FakeOverlayService());

        var exception = await Assert.ThrowsAsync<TranslationPipelineException>(
            () => service.RunAsync(CreateProfile(zone), zone));

        Assert.Equal(TranslationPipelineStage.Capture, exception.Stage);
        Assert.Same(expected, exception.InnerException);
        Assert.Equal("Translation pipeline failed during Capture.", exception.Message);
    }

    [Fact]
    public async Task RunAsync_WhenTranslatorReturnsWrongItemCount_FailsTranslationStage()
    {
        var zone = CreateZone();
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("First", new BoundingBox(0, 0, 20, 10)),
                    new OcrTextBlock("Second", new BoundingBox(24, 0, 20, 10)),
                },
            },
            new FakeTranslatorProvider("Google", new[] { "Only one" }),
            new FakeOverlayService());

        var exception = await Assert.ThrowsAsync<TranslationPipelineException>(
            () => service.RunAsync(CreateProfile(zone), zone));

        Assert.Equal(TranslationPipelineStage.Cache, exception.Stage);
        Assert.Contains("Cache", exception.Message, StringComparison.Ordinal);
    }

    private static TranslationPipelineService CreateService(
        FakeCaptureFrameSource frameSource,
        FakeOcrEngine ocrEngine,
        FakeTranslatorProvider translator,
        FakeOverlayService overlay,
        FakeCredentialStorage? credentialStorage = null,
        TranslationPipelineOptimizationOptions? optimizationOptions = null)
    {
        return new TranslationPipelineService(
            new CaptureService(frameSource),
            new OcrService(ocrEngine),
            new TranslatorManager(new ITranslatorProvider[] { translator }),
            new TranslatorCredentialService(credentialStorage ?? FakeCredentialStorage.WithGoogleCredentials()),
            new TranslationCacheService(new FakeTranslationCacheRepository(), new TranslationCacheOptions()),
            new OverlayPositioningService(),
            overlay,
            optimizationOptions ?? TranslationPipelineOptimizationOptions.Disabled);
    }

    private static GameProfile CreateProfile(params OcrZone[] zones)
    {
        return new GameProfile
        {
            Id = "profile-a",
            Name = "Pipeline profile",
            OcrZones = zones,
            OcrSettings = new OcrSettings
            {
                OrientationMode = OcrOrientationMode.Vertical,
            },
            OverlaySettings = new OverlaySettings
            {
                MaskMode = OverlayMaskMode.Darken,
                MaskColor = "#101010",
                Opacity = 0.75,
                Padding = 6,
            },
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "en",
                TargetLanguage = "ru",
            },
        };
    }

    private static OcrZone CreateZone()
    {
        return CreateZone("zone-a", "Subtitles", new AbsoluteRectangle(10, 20, 100, 40));
    }

    private static OcrZone CreateZone(string id, string name, AbsoluteRectangle absoluteBounds)
    {
        return new OcrZone
        {
            Id = id,
            Name = name,
            AbsoluteBounds = absoluteBounds,
            RelativeBounds = new RelativeRectangle(0.1, 0.2, 0.3, 0.1),
        };
    }

    private static byte[] CreatePixels(OcrZone zone, byte value)
    {
        var stride = checked(zone.AbsoluteBounds.Width * 4);
        return Enumerable.Repeat(value, checked(stride * zone.AbsoluteBounds.Height)).ToArray();
    }
    private sealed class FakeCaptureFrameSource : ICaptureFrameSource
    {
        private int captureCount;

        public List<CaptureRegion> CapturedRegions { get; } = new();

        public Exception? Failure { get; init; }

        public IReadOnlyList<byte[]> PixelFrames { get; init; } = Array.Empty<byte[]>();

        public IReadOnlyList<DateTimeOffset> CapturedAtFrames { get; init; } = Array.Empty<DateTimeOffset>();

        public Action? OnCapture { get; init; }

        public Task<CapturedFrame> CaptureAsync(CaptureRegion region, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                return Task.FromException<CapturedFrame>(Failure);
            }

            var captureIndex = captureCount++;
            CapturedRegions.Add(region);
            OnCapture?.Invoke();
            var stride = checked(region.Width * 4);
            var byteCount = checked(stride * region.Height);
            var pixels = captureIndex < PixelFrames.Count
                ? PixelFrames[captureIndex].ToArray()
                : Enumerable.Repeat((byte)42, byteCount).ToArray();
            if (pixels.Length != byteCount)
            {
                throw new InvalidOperationException("Fake capture frame pixel data length does not match the requested region.");
            }

            var capturedAt = captureIndex < CapturedAtFrames.Count
                ? CapturedAtFrames[captureIndex]
                : FrameTime.AddMilliseconds(captureIndex * 16);

            return Task.FromResult(
                new CapturedFrame(
                    region,
                    region.Width,
                    region.Height,
                    stride,
                    "Bgra32",
                    pixels,
                    capturedAt));
        }
    }

    private sealed class FakeOcrEngine : IOcrEngine
    {
        private int recognitionCount;

        public string EngineId { get; init; } = OcrSettings.WindowsEngineId;

        public List<OcrRequest> Requests { get; } = new();

        public Func<OcrRequest, IReadOnlyList<OcrTextBlock>>? BlocksFactory { get; init; }

        public Func<OcrRequest, Exception?>? FailureFactory { get; init; }

        public IReadOnlyList<DateTimeOffset> RecognizedAtFrames { get; init; } = Array.Empty<DateTimeOffset>();

        public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recognitionIndex = recognitionCount++;
            Requests.Add(request);

            var failure = FailureFactory?.Invoke(request);
            if (failure is not null)
            {
                return Task.FromException<OcrResult>(failure);
            }

            var recognizedAt = recognitionIndex < RecognizedAtFrames.Count
                ? RecognizedAtFrames[recognitionIndex]
                : OcrTime;

            return Task.FromResult(
                new OcrResult(
                    request,
                    BlocksFactory?.Invoke(request) ?? Array.Empty<OcrTextBlock>(),
                    recognizedAt));
        }
    }

    private sealed class FakeTranslatorProvider : ITranslatorProvider
    {
        private readonly IReadOnlyList<string>? translatedTexts;

        public FakeTranslatorProvider(string providerId, IReadOnlyList<string>? translatedTexts = null)
        {
            ProviderId = providerId;
            this.translatedTexts = translatedTexts;
        }

        public string ProviderId { get; }

        public int CallCount { get; private set; }

        public TranslateRequest? Request { get; private set; }

        public Task<TranslateResponse> TranslateAsync(
            TranslateRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Request = request;

            return Task.FromResult(
                new TranslateResponse(
                    translatedTexts ?? request.Texts.Select(text => $"Translated {text}"),
                    TranslatedAt));
        }
    }

    private sealed class FakeCredentialStorage : ICredentialStorage
    {
        private readonly Dictionary<string, TranslatorCredentialRecord> records = new(StringComparer.OrdinalIgnoreCase);

        public static FakeCredentialStorage WithGoogleCredentials()
        {
            var storage = new FakeCredentialStorage();
            storage.records["Google"] = new TranslatorCredentialRecord(
                "Google",
                "SECRET_ACCESS_TOKEN",
                "project-a",
                "global",
                new Uri("https://translation.test"));

            return storage;
        }

        public Task SaveAsync(
            TranslatorCredentialRecord credential,
            CancellationToken cancellationToken = default)
        {
            records[credential.Provider] = credential;
            return Task.CompletedTask;
        }

        public Task<TranslatorCredentialRecord?> ReadAsync(
            string provider,
            CancellationToken cancellationToken = default)
        {
            records.TryGetValue(provider, out var credential);
            return Task.FromResult(credential);
        }

        public Task DeleteAsync(string provider, CancellationToken cancellationToken = default)
        {
            records.Remove(provider);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOverlayService : IOverlayService
    {
        public bool IsVisible { get; private set; }

        public bool IsExcludedFromCapture { get; set; }

        public OverlaySnapshot? CurrentSnapshot { get; private set; }

        public List<string> Events { get; } = new();

        public void Show(OverlaySnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            IsVisible = true;
            Events.Add($"Show:{snapshot.TextItems.Count}");
        }

        public void Hide()
        {
            IsVisible = false;
            Events.Add("Hide");
        }
    }

    private sealed class FakeTranslationCacheRepository : ITranslationCacheRepository
    {
        private readonly Dictionary<TranslationCacheKey, TranslationCacheEntry> entries = new();

        public Task<TranslationCacheEntry?> GetAsync(
            TranslationCacheKey key,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            entries.TryGetValue(key, out var entry);
            if (entry?.IsExpired(now) == true)
            {
                return Task.FromResult<TranslationCacheEntry?>(null);
            }

            return Task.FromResult(entry);
        }

        public Task SaveAsync(
            TranslationCacheEntry entry,
            CancellationToken cancellationToken = default)
        {
            entries[entry.Key] = entry;
            return Task.CompletedTask;
        }

        public Task<int> DeleteExpiredAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var expiredKeys = entries
                .Where(pair => pair.Value.IsExpired(now))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var key in expiredKeys)
            {
                entries.Remove(key);
            }

            return Task.FromResult(expiredKeys.Length);
        }
    }
}
