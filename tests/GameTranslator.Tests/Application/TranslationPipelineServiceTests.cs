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
    private static readonly DateTimeOffset TranslatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

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

        var result = await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

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
    public async Task RunAsync_WhenZoneHasExplicitOcrLanguage_PassesItToOcrAndKeepsTranslatorSource()
    {
        var zone = CreateZone() with
        {
            OcrLanguage = "jpn_vert",
        };
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
                SourceLanguage = "ja",
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
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            new FakeCaptureFrameSource(),
            ocrEngine,
            translator,
            new FakeOverlayService());

        await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        var request = Assert.Single(ocrEngine.Requests);
        Assert.Equal(OcrSettings.TesseractEngineId, request.EngineId);
        Assert.Equal("jpn_vert", request.Language);
        Assert.Equal(OcrOrientationMode.Vertical, request.OrientationMode);
        Assert.Equal("ja", translator.Request?.SourceLanguage);
    }

    [Fact]
    public async Task RunAsync_WhenProfileUsesTesseractLanguageTagForTranslation_NormalizesItBeforeCacheAndProvider()
    {
        var zone = CreateZone() with { OcrLanguage = "tha" };
        var profile = CreateProfile(zone) with
        {
            OcrSettings = new OcrSettings { Engine = OcrSettings.TesseractEngineId },
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "tha",
                TargetLanguage = "ru",
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[] { new OcrTextBlock("สวัสดี", new BoundingBox(0, 0, 20, 10)) },
        };
        var translator = new FakeTranslatorProvider("Google", new[] { "Привет" });
        var service = CreateService(new FakeCaptureFrameSource(), ocrEngine, translator, new FakeOverlayService());

        await service.RunAsync(profile, zone, runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal("tha", Assert.Single(ocrEngine.Requests).Language);
        Assert.Equal("th", translator.Request?.SourceLanguage);
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

        var result = await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

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

        var result = await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

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

        var first = await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);
        var second = await service.RunAsync(
            profile,
            zone,
            first.OverlaySnapshot,
            TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal(1, translator.CallCount);
        Assert.Equal(1, second.CacheResult?.MemoryHitCount);
        Assert.Equal(0, second.CacheResult?.MissCount);
        Assert.Equal(new[] { "Привет" }, second.TranslateResponse?.TranslatedTexts);
    }

    [Fact]
    public async Task RunAsync_WhenZoneUsesWholeZoneGrouping_TranslatesJoinedTextAndShowsSingleOverlayItem()
    {
        var zone = CreateZone("zone-a", "Dialog", new AbsoluteRectangle(10, 20, 200, 100)) with
        {
            TranslationGroupingMode = TranslationGroupingMode.WholeZone,
        };
        var profile = CreateProfile(zone) with
        {
            OcrSettings = new OcrSettings
            {
                OrientationMode = OcrOrientationMode.Horizontal,
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("that I ate was very", new BoundingBox(0, 14, 120, 10)),
                new OcrTextBlock("apple", new BoundingBox(0, 0, 40, 10)),
                new OcrTextBlock("tasty", new BoundingBox(0, 28, 50, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google", new[] { "apple that I ate was very tasty translated" });
        var overlay = new FakeOverlayService();
        var service = CreateService(new FakeCaptureFrameSource(), ocrEngine, translator, overlay);

        var first = await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);
        var second = await service.RunAsync(
            profile,
            zone,
            first.OverlaySnapshot,
            TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal(new[] { "apple that I ate was very tasty" }, translator.Request?.Texts);
        Assert.Equal(1, translator.CallCount);
        Assert.Equal(3, first.RecognizedBlockCount);
        Assert.Equal(1, first.TranslatedBlockCount);
        Assert.Equal(1, second.CacheResult?.MemoryHitCount);
        Assert.Equal(0, second.CacheResult?.MissCount);

        var item = Assert.Single(first.OverlaySnapshot.TextItems);
        Assert.Equal("apple that I ate was very tasty translated", item.Text);
        Assert.Equal(10, item.X);
        Assert.Equal(20, item.Y);
        Assert.Equal(120, item.Width);
        Assert.Equal(38, item.Height);
        Assert.Same(second.OverlaySnapshot, overlay.CurrentSnapshot);
    }

    [Fact]
    public async Task RunAsync_WhenZoneUsesNearbyGrouping_TranslatesNearbyClustersAndShowsSeparateOverlayItems()
    {
        var zone = CreateZone("zone-a", "Comic page", new AbsoluteRectangle(10, 20, 400, 400)) with
        {
            TranslationGroupingMode = TranslationGroupingMode.NearbyBlocks,
            TextGrouping = new OcrZoneTextGroupingSettings
            {
                MergeDistancePercent = 5,
            },
        };
        var profile = CreateProfile(zone) with
        {
            OcrSettings = new OcrSettings
            {
                OrientationMode = OcrOrientationMode.Horizontal,
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("NO SEE.", new BoundingBox(120, 114, 70, 10)),
                new OcrTextBlock("YO!", new BoundingBox(300, 20, 32, 14)),
                new OcrTextBlock("LONG TIME", new BoundingBox(100, 100, 90, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google", new[] { "Они!", "Давно не виделись." });
        var overlay = new FakeOverlayService();
        var service = CreateService(new FakeCaptureFrameSource(), ocrEngine, translator, overlay);

        var result = await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal(new[] { "YO!", "LONG TIME NO SEE." }, translator.Request?.Texts);
        Assert.Equal(3, result.RecognizedBlockCount);
        Assert.Equal(2, result.TranslatedBlockCount);
        Assert.Equal(new[] { "Они!", "Давно не виделись." }, result.OverlaySnapshot.TextItems.Select(item => item.Text));
        Assert.Equal(new[] { 310, 110 }, result.OverlaySnapshot.TextItems.Select(item => item.X));
        Assert.Equal(new[] { 40, 112 }, result.OverlaySnapshot.TextItems.Select(item => item.Y));
        Assert.Equal(new[] { 32, 90 }, result.OverlaySnapshot.TextItems.Select(item => item.Width));
        Assert.Equal(new[] { 14, 24 }, result.OverlaySnapshot.TextItems.Select(item => item.Height));
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
    }

    [Fact]
    public async Task RunAsync_WhenNearbyGroupingHasStaggeredWords_TranslatesRowsInReadingOrder()
    {
        var zone = CreateZone("zone-a", "Comic bubble", new AbsoluteRectangle(10, 20, 400, 200)) with
        {
            TranslationGroupingMode = TranslationGroupingMode.NearbyBlocks,
            TextGrouping = new OcrZoneTextGroupingSettings
            {
                MergeDistancePercent = 5,
            },
        };
        var profile = CreateProfile(zone) with
        {
            OcrSettings = new OcrSettings
            {
                OrientationMode = OcrOrientationMode.Horizontal,
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("ripe", new BoundingBox(80, 2, 40, 12)),
                new OcrTextBlock("Apple", new BoundingBox(10, 6, 60, 12)),
                new OcrTextBlock("juicy", new BoundingBox(70, 22, 50, 12)),
                new OcrTextBlock("tasty", new BoundingBox(10, 26, 50, 12)),
                new OcrTextBlock("picked", new BoundingBox(80, 42, 60, 12)),
                new OcrTextBlock("yesterday", new BoundingBox(10, 46, 60, 12)),
            },
        };
        var translator = new FakeTranslatorProvider("Google", new[] { "Translated grouped bubble" });
        var service = CreateService(new FakeCaptureFrameSource(), ocrEngine, translator, new FakeOverlayService());

        var result = await service.RunAsync(
            profile,
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal(new[] { "Apple ripe tasty juicy yesterday picked" }, translator.Request?.Texts);
        Assert.Equal(6, result.RecognizedBlockCount);
        Assert.Equal(1, result.TranslatedBlockCount);
        Assert.Equal("Translated grouped bubble", Assert.Single(result.OverlaySnapshot.TextItems).Text);
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

        var first = await service.RunAsync(
            CreateProfile(zone),
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);
        var second = await service.RunAsync(
            CreateProfile(zone),
            zone,
            first.OverlaySnapshot,
            TranslationPipelineRunOptions.LegacyFullPage);

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

        var first = await service.RunAsync(
            CreateProfile(zone),
            zone,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);
        var second = await service.RunAsync(
            CreateProfile(zone),
            zone,
            first.OverlaySnapshot,
            TranslationPipelineRunOptions.LegacyFullPage);

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
            stableTextInterval: TimeSpan.FromSeconds(1),
            enableCandidateDetectorPilot: false);

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
            preservePreviousOverlayWhileWaitingForStableText: true,
            enableCandidateDetectorPilot: false);

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
            preservePreviousOverlayWhileWaitingForStableText: true,
            enableCandidateDetectorPilot: false);

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
            preservePreviousOverlayWhileWaitingForStableText: true,
            enableCandidateDetectorPilot: false);

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
            stableTextInterval: TimeSpan.FromMilliseconds(300),
            enableCandidateDetectorPilot: false);

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
            restorePreviousOverlayAfterCapture: true,
            enableCandidateDetectorPilot: false);

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
            restorePreviousOverlayAfterCapture: true,
            enableCandidateDetectorPilot: false);

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

        var result = await service.RunAllZonesAsync(
            profile,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

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
    public async Task RunAllZonesAsync_WhenLaterZoneCompletesFirst_PublishesReadyZoneBeforeSlowZoneCompletes()
    {
        var slowZone = CreateZone("zone-slow", "Complex", new AbsoluteRectangle(10, 20, 100, 40));
        var readyZone = CreateZone("zone-ready", "Simple", new AbsoluteRectangle(200, 120, 80, 30));
        var profile = CreateProfile(slowZone, readyZone);
        var slowTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlowTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    string.Equals(request.ZoneId, slowZone.Id, StringComparison.Ordinal)
                        ? "Complex text"
                        : "Simple text",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Complex text", StringComparison.Ordinal))
                {
                    slowTranslationStarted.TrySetResult(true);
                    await releaseSlowTranslation.Task.WaitAsync(cancellationToken);
                    return new TranslateResponse(new[] { "Translated Complex text" }, TranslatedAt);
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt.AddMilliseconds(10));
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(new FakeCaptureFrameSource(), ocrEngine, translator, overlay);

        var runTask = service.RunAllZonesAsync(
            profile,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);
        await slowTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await WaitForConditionAsync(
            () => overlay.CurrentSnapshot?.TextItems.Count == 1
                && string.Equals(overlay.CurrentSnapshot.TextItems[0].Text, "Translated Simple text", StringComparison.Ordinal));

        Assert.False(runTask.IsCompleted);
        Assert.Equal(new[] { "Show:1" }, overlay.Events);
        Assert.Equal("Translated Simple text", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);

        releaseSlowTranslation.SetResult(true);
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Empty(result.ZoneFailures);
        Assert.Equal(new[] { "Show:1", "Show:2" }, overlay.Events);
        Assert.Equal(
            new[] { "Translated Complex text", "Translated Simple text" },
            result.OverlaySnapshot.TextItems.Select(item => item.Text));
        Assert.Same(result.OverlaySnapshot, overlay.CurrentSnapshot);
    }

    [Fact]
    public async Task RunAllZonesAsync_DefaultCandidatePipeline_PublishesReadyCandidateBeforeSlowSibling()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var slowCandidateBounds = new BoundingBox(8, 8, 30, 12);
        var readyCandidateBounds = new BoundingBox(55, 8, 30, 12);
        var slowTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlowTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(
                    zone,
                    frameMarker: 1,
                    (slowCandidateBounds, (byte)10),
                    (readyCandidateBounds, (byte)20)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 10 ? "Slow candidate" : "Ready candidate",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[]
            {
                new TextCandidate(slowCandidateBounds, 0.95),
                new TextCandidate(readyCandidateBounds, 0.90),
            }));
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Slow candidate", StringComparison.Ordinal))
                {
                    slowTranslationStarted.TrySetResult(true);
                    await releaseSlowTranslation.Task.WaitAsync(cancellationToken);
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);

        var runTask = service.RunAllZonesAsync(profile);
        await slowTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await WaitForConditionAsync(
            () => overlay.CurrentSnapshot?.TextItems.Count == 1
                && string.Equals(overlay.CurrentSnapshot.TextItems[0].Text, "Translated Ready candidate", StringComparison.Ordinal));

        Assert.False(runTask.IsCompleted);
        Assert.Equal(new[] { "Show:1" }, overlay.Events);

        releaseSlowTranslation.SetResult(true);
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Empty(result.ZoneFailures);
        Assert.Equal(new[] { "Show:1", "Show:2" }, overlay.Events);
        Assert.Equal(
            new[] { "Translated Slow candidate", "Translated Ready candidate" },
            result.OverlaySnapshot.TextItems.Select(item => item.Text));
    }

    [Fact]
    public async Task RunAllZonesAsync_DefaultCandidatePipeline_WhenDetectorIsUnavailable_ReportsDegradedOcrFailure()
    {
        var zone = CreateZone();
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Legacy full-zone text", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var service = CreateService(
            new FakeCaptureFrameSource(),
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            new FakeOverlayService());

        var result = await service.RunAllZonesAsync(CreateProfile(zone));

        Assert.Empty(ocrEngine.Requests);
        var failure = Assert.Single(result.ZoneFailures);
        Assert.Equal(zone.Id, failure.ZoneId);
        Assert.Equal(TranslationPipelineStage.Ocr, failure.Stage);
        Assert.Contains("Candidate-region pipeline is degraded", failure.Message, StringComparison.Ordinal);
        Assert.Empty(result.OverlaySnapshot.TextItems);
    }

    [Fact]
    public async Task LiveSession_WhenAdr028ReadinessIsRequired_DiscardsPrewarmFrameThenPublishesCandidateAfterReady()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "GoogleWeb",
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var detectorCallCount = 0;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, 2, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, 3, (candidateBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[] { new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)) },
        };
        var detector = new FakeCandidateDetector(_ =>
        {
            Interlocked.Increment(ref detectorCallCount);
            return TextCandidateDetectionResult.Available(
                "test-detector",
                new[] { new TextCandidate(candidateBounds, 0.95) });
        });
        var translator = new FakeTranslatorProvider("GoogleWeb");
        var overlay = new FakeOverlayService();
        var service = CreateService(frameSource, ocrEngine, translator, overlay, candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true,
                requireCandidateReadinessBarrier: true));

        var prewarming = await session.RefreshAsync();
        Assert.Equal(CandidatePipelineReadinessStatus.Prewarming, prewarming.CandidateReadiness.Status);
        Assert.Empty(prewarming.BatchResult.ZoneResults);
        Assert.Equal(1, detectorCallCount);
        Assert.Equal(1, translator.CallCount);

        var readyAfterDiscardingPrewarmFrame = await session.RefreshAsync();
        Assert.True(
            readyAfterDiscardingPrewarmFrame.CandidateReadiness.Status == CandidatePipelineReadinessStatus.Ready,
            readyAfterDiscardingPrewarmFrame.CandidateReadiness.UnavailableReason);
        Assert.Equal(1, readyAfterDiscardingPrewarmFrame.CandidateReadiness.Generation);
        Assert.Empty(readyAfterDiscardingPrewarmFrame.BatchResult.ZoneResults);
        Assert.Equal(1, detectorCallCount);
        Assert.Equal(1, translator.CallCount);

        var live = await session.RefreshAsync();
        Assert.Equal(CandidatePipelineReadinessStatus.Ready, live.CandidateReadiness.Status);
        Assert.Equal(2, detectorCallCount);
        Assert.Equal(2, translator.CallCount);
        Assert.Equal("Translated Candidate", Assert.Single(live.BatchResult.OverlaySnapshot.TextItems).Text);
        Assert.Equal(new[] { "Show:1" }, overlay.Events);
    }

    [Fact]
    public async Task LiveSession_WhenAdr028PrewarmFails_UsesBoundedRecoveryWithoutStartingCandidateWork()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "GoogleWeb",
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var frameSource = new FakeCaptureFrameSource();
        var ocrEngine = new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(new BoundingBox(8, 8, 30, 12), 0.95) }));
        var translator = new FakeTranslatorProvider(
            "GoogleWeb",
            translateAsync: (request, _) => Task.FromResult(new TranslateResponse(
                request.Texts.Select(text => $"Translated {text}"),
                TranslatedAt,
                providerId: "UnexpectedProvider")));
        var service = CreateService(frameSource, ocrEngine, translator, new FakeOverlayService(), candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true,
                requireCandidateReadinessBarrier: true,
                candidatePrewarmMaximumAttempts: 2,
                candidatePrewarmInitialRetryDelay: TimeSpan.Zero));

        var firstPrewarm = await session.RefreshAsync();
        Assert.Equal(CandidatePipelineReadinessStatus.Prewarming, firstPrewarm.CandidateReadiness.Status);
        Assert.Equal(1, translator.CallCount);

        var firstFailure = await session.RefreshAsync();
        Assert.Equal(CandidatePipelineReadinessStatus.Degraded, firstFailure.CandidateReadiness.Status);
        Assert.Equal(1, firstFailure.CandidateReadiness.RestartCount);
        Assert.NotNull(firstFailure.CandidateReadiness.NextRetryAt);

        var secondPrewarm = await session.RefreshAsync();
        Assert.Equal(CandidatePipelineReadinessStatus.Prewarming, secondPrewarm.CandidateReadiness.Status);
        Assert.Equal(2, translator.CallCount);

        var exhausted = await session.RefreshAsync();
        Assert.Equal(CandidatePipelineReadinessStatus.Degraded, exhausted.CandidateReadiness.Status);
        Assert.Equal(2, exhausted.CandidateReadiness.RestartCount);
        Assert.Null(exhausted.CandidateReadiness.NextRetryAt);

        var remainsDegraded = await session.RefreshAsync();
        Assert.Equal(CandidatePipelineReadinessStatus.Degraded, remainsDegraded.CandidateReadiness.Status);
        Assert.Equal(2, translator.CallCount);
        Assert.Empty(remainsDegraded.BatchResult.ZoneResults);
    }

    [Fact]
    public async Task LiveSession_WhenDirectGoogleWebPrewarmHasProviderFailure_ReportsSafeProviderDetail()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "GoogleWeb",
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(new BoundingBox(8, 8, 30, 12), 0.95) }));
        var translator = new FakeTranslatorProvider(
            "GoogleWeb",
            translateAsync: (_, _) => Task.FromException<TranslateResponse>(
                new TranslatorProviderException(
                    "GoogleWeb",
                    System.Net.HttpStatusCode.TooManyRequests,
                    "provider response is intentionally not included in readiness diagnostics")));
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine { EngineId = OcrSettings.TesseractEngineId },
            translator,
            new FakeOverlayService(),
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true,
                requireCandidateReadinessBarrier: true,
                candidatePrewarmMaximumAttempts: 1));

        await session.RefreshAsync();
        var degraded = await session.RefreshAsync();

        Assert.Equal(CandidatePipelineReadinessStatus.Degraded, degraded.CandidateReadiness.Status);
        Assert.Equal(
            "Direct GoogleWeb provider prewarm was unavailable (GoogleWeb; Throttled; HTTP 429).",
            degraded.CandidateReadiness.UnavailableReason);
    }

    [Fact]
    public async Task LiveSession_WhenCandidateCaptureIsLost_RemovesPublishedOverlayAndInvalidatesReadiness()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "GoogleWeb",
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, 2, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, 3, (candidateBounds, (byte)10)),
            },
            FailureFactory = (_, captureIndex) => captureIndex == 3
                ? new CaptureFrameSourceException("Capture source unavailable.")
                : null,
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[] { new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)) },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("GoogleWeb"),
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true,
                requireCandidateReadinessBarrier: true));

        await session.RefreshAsync();
        await session.RefreshAsync();
        var published = await session.RefreshAsync();
        Assert.Equal(CandidatePipelineReadinessStatus.Ready, published.CandidateReadiness.Status);
        Assert.Single(published.BatchResult.OverlaySnapshot.TextItems);

        var afterCaptureLoss = await session.RefreshAsync();

        Assert.Equal(CandidatePipelineReadinessStatus.Degraded, afterCaptureLoss.CandidateReadiness.Status);
        Assert.Empty(afterCaptureLoss.BatchResult.OverlaySnapshot.TextItems);
        Assert.Equal(new[] { "Show:1", "Show:0" }, overlay.Events);
    }

    [Fact]
    public async Task LiveSession_WhenDetectorRecoveryInvalidatesOldCandidate_DoesNotPublishLateOldResult()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "GoogleWeb",
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = Enumerable.Range(1, 6)
                .Select(frameMarker => CreateCandidatePilotPixels(
                    zone,
                    (byte)frameMarker,
                    (candidateBounds, (byte)10)))
                .ToArray(),
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[] { new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)) },
        };
        var detectorCallCount = 0;
        var detector = new FakeCandidateDetector(_ => Interlocked.Increment(ref detectorCallCount) == 3
            ? TextCandidateDetectionResult.Unavailable("test-detector", "Worker lost")
            : TextCandidateDetectionResult.Available(
                "test-detector",
                new[] { new TextCandidate(candidateBounds, 0.95) }));
        var oldCandidateTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldCandidateTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var translator = new FakeTranslatorProvider(
            "GoogleWeb",
            translateAsync: async (request, _) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Candidate", StringComparison.Ordinal))
                {
                    oldCandidateTranslationStarted.TrySetResult(true);
                    await releaseOldCandidateTranslation.Task;
                }

                return new TranslateResponse(
                    new[] { $"Translated {text}" },
                    TranslatedAt,
                    providerId: "GoogleWeb");
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(frameSource, ocrEngine, translator, overlay, candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true,
                requireCandidateReadinessBarrier: true,
                candidatePrewarmInitialRetryDelay: TimeSpan.Zero));

        await session.RefreshAsync();
        await session.RefreshAsync();
        await session.RefreshAsync();
        await oldCandidateTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var degraded = await session.RefreshAsync();
        Assert.Equal(CandidatePipelineReadinessStatus.Degraded, degraded.CandidateReadiness.Status);
        Assert.Contains(degraded.CancelledZoneIds, id => id.Contains(":candidate:", StringComparison.Ordinal));

        releaseOldCandidateTranslation.TrySetResult(true);
        var rewarming = await session.RefreshAsync();
        Assert.Equal(CandidatePipelineReadinessStatus.Prewarming, rewarming.CandidateReadiness.Status);

        var recovered = await session.RefreshAsync();
        Assert.Equal(CandidatePipelineReadinessStatus.Ready, recovered.CandidateReadiness.Status);
        Assert.Empty(recovered.BatchResult.OverlaySnapshot.TextItems);
        Assert.DoesNotContain(overlay.Events, eventName => eventName == "Show:1");
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenCandidatePilotUsesMultipleRegions_BoundsCacheMissTranslationsToThree()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var bounds = new[]
        {
            new BoundingBox(4, 4, 18, 12),
            new BoundingBox(28, 4, 18, 12),
            new BoundingBox(52, 4, 18, 12),
            new BoundingBox(76, 4, 18, 12),
        };
        var releaseTranslations = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var threeTranslationsStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeTranslations = 0;
        var maxActiveTranslations = 0;
        var startedTranslations = 0;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(
                    zone,
                    frameMarker: 1,
                    (bounds[0], (byte)10),
                    (bounds[1], (byte)20),
                    (bounds[2], (byte)30),
                    (bounds[3], (byte)40)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock($"Candidate {request.Frame.PixelData.Span[0]}", new BoundingBox(0, 0, 12, 10)),
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            bounds.Select(candidateBounds => new TextCandidate(candidateBounds, 0.95))));
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var active = Interlocked.Increment(ref activeTranslations);
                int observed;
                do
                {
                    observed = maxActiveTranslations;
                    if (observed >= active)
                    {
                        break;
                    }
                }
                while (Interlocked.CompareExchange(ref maxActiveTranslations, active, observed) != observed);

                if (Interlocked.Increment(ref startedTranslations) == 3)
                {
                    threeTranslationsStarted.TrySetResult(true);
                }

                try
                {
                    await releaseTranslations.Task.WaitAsync(cancellationToken);
                    return new TranslateResponse(
                        request.Texts.Select(text => $"Translated {text}"),
                        TranslatedAt);
                }
                finally
                {
                    Interlocked.Decrement(ref activeTranslations);
                }
            });
        var service = CreateService(frameSource, ocrEngine, translator, new FakeOverlayService(), candidateDetector: detector);

        var runTask = service.RunAllZonesAsync(
            profile,
            runOptions: new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true));
        await threeTranslationsStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(3, Volatile.Read(ref maxActiveTranslations));
        Assert.Equal(3, translator.CallCount);

        releaseTranslations.TrySetResult(true);
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(4, translator.CallCount);
        Assert.Equal(4, result.TranslatedBlockCount);
        Assert.Equal(3, Volatile.Read(ref maxActiveTranslations));
        Assert.Empty(result.ZoneFailures);
    }

    [Fact]
    public async Task RunAllZonesAsync_WhenAdr028ReadinessIsRequired_RequiresPersistentLiveSession()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine(),
            new FakeTranslatorProvider("GoogleWeb"),
            new FakeOverlayService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAllZonesAsync(
            CreateProfile(zone),
            runOptions: new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true,
                requireCandidateReadinessBarrier: true)));

        Assert.Contains("LiveTranslationSession", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveSession_WhenOneZoneIdentityChanges_CancelsOnlyThatZoneAndKeepsReadySiblingOverlay()
    {
        var slowZone = CreateZone("zone-slow", "Complex", new AbsoluteRectangle(10, 20, 100, 40));
        var readyZone = CreateZone("zone-ready", "Simple", new AbsoluteRectangle(200, 120, 80, 30));
        var profile = CreateProfile(slowZone, readyZone);
        var oldSlowTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldSlowTranslationCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreatePixels(slowZone, 10),
                CreatePixels(readyZone, 20),
                CreatePixels(slowZone, 30),
                CreatePixels(readyZone, 20),
            },
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime,
                FrameTime.AddMilliseconds(250),
                FrameTime.AddMilliseconds(250),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = request =>
            {
                var marker = request.Frame.PixelData.Span[0];
                var text = string.Equals(request.ZoneId, slowZone.Id, StringComparison.Ordinal)
                    ? marker == 10 ? "Slow old" : "Slow new"
                    : "Ready";
                return new[] { new OcrTextBlock(text, new BoundingBox(0, 0, 20, 10)) };
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Slow old", StringComparison.Ordinal))
                {
                    oldSlowTranslationStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        oldSlowTranslationCancelled.TrySetResult(true);
                        throw;
                    }
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(frameSource, ocrEngine, translator, overlay);

        using var session = service.CreateLiveSession(
            profile,
            TranslationPipelineRunOptions.LegacyFullPage);
        var firstUpdate = await session.RefreshAsync();
        await oldSlowTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(firstUpdate.OverlayChanged);
        Assert.Equal(new[] { "Translated Ready" }, overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));

        var secondUpdate = await session.RefreshAsync();
        await oldSlowTranslationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(new[] { slowZone.Id }, secondUpdate.CancelledZoneIds);
        Assert.Equal(
            new[] { "Translated Slow new", "Translated Ready" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
        Assert.DoesNotContain(
            overlay.CurrentSnapshot.TextItems,
            item => string.Equals(item.Text, "Translated Slow old", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LiveSession_WhenOneZoneTextDisappears_CancelsThatZoneAndRetainsReadySiblingOverlay()
    {
        var slowZone = CreateZone("zone-slow", "Complex", new AbsoluteRectangle(10, 20, 100, 40));
        var readyZone = CreateZone("zone-ready", "Simple", new AbsoluteRectangle(200, 120, 80, 30));
        var profile = CreateProfile(slowZone, readyZone);
        var oldSlowTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldSlowTranslationCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreatePixels(slowZone, 10),
                CreatePixels(readyZone, 20),
                CreatePixels(slowZone, 0),
                CreatePixels(readyZone, 20),
            },
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime,
                FrameTime.AddMilliseconds(250),
                FrameTime.AddMilliseconds(250),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = request =>
            {
                var marker = request.Frame.PixelData.Span[0];
                if (string.Equals(request.ZoneId, slowZone.Id, StringComparison.Ordinal) && marker == 0)
                {
                    return Array.Empty<OcrTextBlock>();
                }

                var text = string.Equals(request.ZoneId, slowZone.Id, StringComparison.Ordinal)
                    ? "Slow old"
                    : "Ready";
                return new[] { new OcrTextBlock(text, new BoundingBox(0, 0, 20, 10)) };
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Slow old", StringComparison.Ordinal))
                {
                    oldSlowTranslationStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        oldSlowTranslationCancelled.TrySetResult(true);
                        throw;
                    }
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(frameSource, ocrEngine, translator, overlay);

        using var session = service.CreateLiveSession(
            profile,
            TranslationPipelineRunOptions.LegacyFullPage);
        await session.RefreshAsync();
        await oldSlowTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var secondUpdate = await session.RefreshAsync();
        await oldSlowTranslationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(new[] { slowZone.Id }, secondUpdate.CancelledZoneIds);
        Assert.Equal(new[] { "Translated Ready" }, overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
    }

    [Fact]
    public async Task LiveSession_WhenOneOfTwoPendingZonesChanges_CancelsOnlyChangedZone()
    {
        var changedZone = CreateZone("zone-changed", "Changing", new AbsoluteRectangle(10, 20, 100, 40));
        var unchangedZone = CreateZone("zone-unchanged", "Stable", new AbsoluteRectangle(200, 120, 80, 30));
        var profile = CreateProfile(changedZone, unchangedZone);
        var oldChangedTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unchangedTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldChangedTranslationCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unchangedTranslationCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUnchangedTranslation = new TaskCompletionSource<bool>();
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreatePixels(changedZone, 10),
                CreatePixels(unchangedZone, 20),
                CreatePixels(changedZone, 30),
                CreatePixels(unchangedZone, 20),
            },
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime,
                FrameTime.AddMilliseconds(250),
                FrameTime.AddMilliseconds(250),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = request =>
            {
                var marker = request.Frame.PixelData.Span[0];
                var text = string.Equals(request.ZoneId, changedZone.Id, StringComparison.Ordinal)
                    ? marker == 10 ? "Changed old" : "Changed new"
                    : "Unchanged";
                return new[] { new OcrTextBlock(text, new BoundingBox(0, 0, 20, 10)) };
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Changed old", StringComparison.Ordinal))
                {
                    oldChangedTranslationStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        oldChangedTranslationCancelled.TrySetResult(true);
                        throw;
                    }
                }

                if (string.Equals(text, "Unchanged", StringComparison.Ordinal))
                {
                    unchangedTranslationStarted.TrySetResult(true);
                    try
                    {
                        await releaseUnchangedTranslation.Task.WaitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        unchangedTranslationCancelled.TrySetResult(true);
                        throw;
                    }
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(frameSource, ocrEngine, translator, overlay);

        using var session = service.CreateLiveSession(
            profile,
            TranslationPipelineRunOptions.LegacyFullPage);
        await session.RefreshAsync();
        await Task.WhenAll(
            oldChangedTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1)),
            unchangedTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1)));

        var secondUpdate = await session.RefreshAsync();
        await oldChangedTranslationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(new[] { changedZone.Id }, secondUpdate.CancelledZoneIds);
        Assert.False(unchangedTranslationCancelled.Task.IsCompleted);
        Assert.Equal(new[] { "Translated Changed new" }, overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
    }

    [Fact]
    public async Task LiveSession_WhenCandidateDisappears_CancelsOnlyThatCandidateAndKeepsReadySiblingOverlay()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var slowCandidateBounds = new BoundingBox(8, 8, 30, 12);
        var readyCandidateBounds = new BoundingBox(55, 8, 30, 12);
        var slowTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowTranslationCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(
                    zone,
                    frameMarker: 1,
                    (slowCandidateBounds, (byte)10),
                    (readyCandidateBounds, (byte)20)),
                CreateCandidatePilotPixels(
                    zone,
                    frameMarker: 2,
                    (readyCandidateBounds, (byte)20)),
            },
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime.AddMilliseconds(250),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request =>
            {
                var text = request.Frame.PixelData.Span[0] == 10 ? "Slow candidate" : "Ready candidate";
                return new[] { new OcrTextBlock(text, new BoundingBox(0, 0, 20, 10)) };
            },
        };
        var detector = new FakeCandidateDetector(request =>
        {
            var candidates = request.Frame.PixelData.Span[0] == 1
                ? new[]
                {
                    new TextCandidate(slowCandidateBounds, 0.95),
                    new TextCandidate(readyCandidateBounds, 0.90),
                }
                : new[] { new TextCandidate(readyCandidateBounds, 0.90) };
            return TextCandidateDetectionResult.Available("test-detector", candidates);
        });
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Slow candidate", StringComparison.Ordinal))
                {
                    slowTranslationStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        slowTranslationCancelled.TrySetResult(true);
                        throw;
                    }
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        var firstUpdate = await session.RefreshAsync();
        await slowTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(firstUpdate.OverlayChanged);
        Assert.Equal(
            new[] { "Translated Ready candidate" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));

        var secondUpdate = await session.RefreshAsync();
        await slowTranslationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            new[] { $"{zone.Id}:candidate:{slowCandidateBounds.X}:{slowCandidateBounds.Y}:{slowCandidateBounds.Width}:{slowCandidateBounds.Height}" },
            secondUpdate.CancelledZoneIds);
        Assert.Equal(
            new[] { "Translated Ready candidate" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
        Assert.DoesNotContain(
            overlay.CurrentSnapshot.TextItems,
            item => string.Equals(item.Text, "Translated Slow candidate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LiveSession_WhenCandidateIdentityChanges_CancelsOnlyChangedCandidateAndPublishesReplacement()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var changingCandidateBounds = new BoundingBox(8, 8, 30, 12);
        var readyCandidateBounds = new BoundingBox(55, 8, 30, 12);
        var oldTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldTranslationCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(
                    zone,
                    frameMarker: 1,
                    (changingCandidateBounds, (byte)10),
                    (readyCandidateBounds, (byte)20)),
                CreateCandidatePilotPixels(
                    zone,
                    frameMarker: 2,
                    (changingCandidateBounds, (byte)30),
                    (readyCandidateBounds, (byte)20)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request =>
            {
                var text = request.Frame.PixelData.Span[0] switch
                {
                    10 => "Old candidate",
                    30 => "Replacement candidate",
                    _ => "Ready candidate",
                };
                return new[] { new OcrTextBlock(text, new BoundingBox(0, 0, 20, 10)) };
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[]
            {
                new TextCandidate(changingCandidateBounds, 0.95),
                new TextCandidate(readyCandidateBounds, 0.90),
            }));
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Old candidate", StringComparison.Ordinal))
                {
                    oldTranslationStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        oldTranslationCancelled.TrySetResult(true);
                        throw;
                    }
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        await oldTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var secondUpdate = await session.RefreshAsync();
        await oldTranslationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            new[] { $"{zone.Id}:candidate:{changingCandidateBounds.X}:{changingCandidateBounds.Y}:{changingCandidateBounds.Width}:{changingCandidateBounds.Height}" },
            secondUpdate.CancelledZoneIds);
        Assert.Equal(
            new[] { "Translated Replacement candidate", "Translated Ready candidate" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
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

        var result = await service.RunAllZonesAsync(
            profile,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

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

        var result = await service.RunAllZonesAsync(
            profile,
            runOptions: TranslationPipelineRunOptions.LegacyFullPage);

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
            () => service.RunAsync(
                CreateProfile(zone),
                zone,
                runOptions: TranslationPipelineRunOptions.LegacyFullPage));

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
            () => service.RunAsync(
                CreateProfile(zone),
                zone,
                runOptions: TranslationPipelineRunOptions.LegacyFullPage));

        Assert.Equal(TranslationPipelineStage.Cache, exception.Stage);
        Assert.Contains("Cache", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_DefaultCandidatePipeline_WhenDetectorIsUnavailable_DoesNotFallbackToFullZoneOcr()
    {
        var zone = CreateZone();
        var ocrEngine = new FakeOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Legacy full-zone text", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var service = CreateService(
            new FakeCaptureFrameSource(),
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            new FakeOverlayService());

        var result = await service.RunAsync(CreateProfile(zone), zone);

        Assert.Empty(ocrEngine.Requests);
        Assert.Equal(0, result.RecognizedBlockCount);
        Assert.Equal(0, result.TranslatedBlockCount);
        Assert.Empty(result.OverlaySnapshot.TextItems);
    }

    [Fact]
    public async Task RunAsync_DefaultCandidatePipeline_UsesTesseractForTheBoundedCandidateCrop()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var candidateBounds = new BoundingBox(18, 7, 30, 18);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, 1, (candidateBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Candidate text", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            new FakeOverlayService(),
            candidateDetector: detector);

        var result = await service.RunAsync(profile, zone);

        var request = Assert.Single(ocrEngine.Requests);
        Assert.Equal(OcrSettings.TesseractEngineId, request.EngineId);
        Assert.Equal(
            new CaptureRegion(28, 27, candidateBounds.Width, candidateBounds.Height),
            request.Frame.Region);
        Assert.Equal("Translated Candidate text", Assert.Single(result.OverlaySnapshot.TextItems).Text);
    }

    private static TranslationPipelineService CreateService(
        FakeCaptureFrameSource frameSource,
        FakeOcrEngine ocrEngine,
        FakeTranslatorProvider translator,
        FakeOverlayService overlay,
        FakeCredentialStorage? credentialStorage = null,
        TranslationPipelineOptimizationOptions? optimizationOptions = null,
        ITextCandidateDetector? candidateDetector = null)
    {
        var ocrService = new OcrService(ocrEngine);
        return new TranslationPipelineService(
            new CaptureService(frameSource),
            ocrService,
            new TranslatorManager(new ITranslatorProvider[] { translator }),
            new TranslatorCredentialService(credentialStorage ?? FakeCredentialStorage.WithGoogleCredentials()),
            new TranslationCacheService(new FakeTranslationCacheRepository(), new TranslationCacheOptions()),
            new OverlayPositioningService(),
            overlay,
            optimizationOptions ?? TranslationPipelineOptimizationOptions.Disabled,
            candidateDetector is null
                ? null
                : new TextCandidateRegionOcrService(candidateDetector, ocrService));
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

    private static byte[] CreateCandidatePilotPixels(
        OcrZone zone,
        byte frameMarker,
        params (BoundingBox Bounds, byte Value)[] candidateMarkers)
    {
        var width = zone.AbsoluteBounds.Width;
        var height = zone.AbsoluteBounds.Height;
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        pixels[0] = frameMarker;

        foreach (var (bounds, value) in candidateMarkers)
        {
            for (var row = bounds.Y; row < bounds.Bottom; row++)
            {
                var offset = checked(row * stride + bounds.X * 4);
                pixels.AsSpan(offset, checked(bounds.Width * 4)).Fill(value);
            }
        }

        return pixels;
    }

    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= timeoutAt)
            {
                throw new TimeoutException("Condition was not met before the test timeout.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }

    private sealed class FakeCaptureFrameSource : ICaptureFrameSource
    {
        private int captureCount;
        private readonly object syncRoot = new();

        public List<CaptureRegion> CapturedRegions { get; } = new();

        public Exception? Failure { get; init; }

        public Func<CaptureRegion, int, Exception?>? FailureFactory { get; init; }

        public IReadOnlyList<byte[]> PixelFrames { get; init; } = Array.Empty<byte[]>();

        public IReadOnlyList<DateTimeOffset> CapturedAtFrames { get; init; } = Array.Empty<DateTimeOffset>();

        public Action? OnCapture { get; init; }

        public Task<CapturedFrame> CaptureAsync(CaptureRegion region, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int captureIndex;
            lock (syncRoot)
            {
                captureIndex = captureCount++;
                CapturedRegions.Add(region);
            }

            var failure = Failure ?? FailureFactory?.Invoke(region, captureIndex);
            if (failure is not null)
            {
                return Task.FromException<CapturedFrame>(failure);
            }

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
        private readonly object syncRoot = new();

        public string EngineId { get; init; } = OcrSettings.WindowsEngineId;

        public List<OcrRequest> Requests { get; } = new();

        public Func<OcrRequest, IReadOnlyList<OcrTextBlock>>? BlocksFactory { get; init; }

        public Func<OcrRequest, Exception?>? FailureFactory { get; init; }

        public IReadOnlyList<DateTimeOffset> RecognizedAtFrames { get; init; } = Array.Empty<DateTimeOffset>();

        public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int recognitionIndex;
            lock (syncRoot)
            {
                recognitionIndex = recognitionCount++;
                Requests.Add(request);
            }

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

    private sealed class FakeCandidateDetector : ITextCandidateDetector
    {
        private readonly Func<TextCandidateDetectionRequest, TextCandidateDetectionResult> detect;

        public FakeCandidateDetector(Func<TextCandidateDetectionRequest, TextCandidateDetectionResult> detect)
        {
            this.detect = detect;
        }

        public Task<TextCandidateDetectionResult> DetectAsync(
            TextCandidateDetectionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(detect(request));
        }
    }

    private sealed class FakeTranslatorProvider : ITranslatorProvider
    {
        private readonly IReadOnlyList<string>? translatedTexts;
        private readonly Func<TranslateRequest, CancellationToken, Task<TranslateResponse>>? translateAsync;
        private int callCount;

        public FakeTranslatorProvider(
            string providerId,
            IReadOnlyList<string>? translatedTexts = null,
            Func<TranslateRequest, CancellationToken, Task<TranslateResponse>>? translateAsync = null)
        {
            ProviderId = providerId;
            this.translatedTexts = translatedTexts;
            this.translateAsync = translateAsync;
        }

        public string ProviderId { get; }

        public int CallCount => callCount;

        public TranslateRequest? Request { get; private set; }

        public Task<TranslateResponse> TranslateAsync(
            TranslateRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref callCount);
            Request = request;
            if (translateAsync is not null)
            {
                return translateAsync(request, cancellationToken);
            }

            return Task.FromResult(
                new TranslateResponse(
                    translatedTexts ?? request.Texts.Select(text => $"Translated {text}"),
                    TranslatedAt,
                    ProviderId));
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
        private readonly object syncRoot = new();

        public Task<TranslationCacheEntry?> GetAsync(
            TranslationCacheKey key,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            lock (syncRoot)
            {
                entries.TryGetValue(key, out var entry);
                if (entry?.IsExpired(now) == true)
                {
                    return Task.FromResult<TranslationCacheEntry?>(null);
                }

                return Task.FromResult(entry);
            }
        }

        public Task SaveAsync(
            TranslationCacheEntry entry,
            CancellationToken cancellationToken = default)
        {
            lock (syncRoot)
            {
                entries[entry.Key] = entry;
            }

            return Task.CompletedTask;
        }

        public Task<int> DeleteExpiredAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            lock (syncRoot)
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
}
