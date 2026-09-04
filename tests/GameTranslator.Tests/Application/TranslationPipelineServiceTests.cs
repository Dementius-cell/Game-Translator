using System.IO;
using System.Net;
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
    public void RunOptions_WhenMinimumCandidateGroupingDurationIsNegative_RejectsIt()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TranslationPipelineRunOptions
            {
                MinimumCandidateGroupingDuration = TimeSpan.FromMilliseconds(-1),
            });

        Assert.Equal("MinimumCandidateGroupingDuration", exception.ParamName);
    }

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
    public async Task RunAsync_WhenLegacyProfileUsesGenericChineseTranslatorTag_NormalizesItForProvider()
    {
        var zone = CreateZone() with { OcrLanguage = "chi_sim" };
        var profile = CreateProfile(zone) with
        {
            OcrSettings = new OcrSettings { Engine = OcrSettings.TesseractEngineId },
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "zh",
                TargetLanguage = "ru",
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[] { new OcrTextBlock("你好", new BoundingBox(0, 0, 20, 10)) },
        };
        var translator = new FakeTranslatorProvider("Google", new[] { "Привет" });
        var service = CreateService(new FakeCaptureFrameSource(), ocrEngine, translator, new FakeOverlayService());

        await service.RunAsync(profile, zone, runOptions: TranslationPipelineRunOptions.LegacyFullPage);

        Assert.Equal("chi_sim", Assert.Single(ocrEngine.Requests).Language);
        Assert.Equal("zh-CN", translator.Request?.SourceLanguage);
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
        Assert.Equal(1, first.TranslationInputBlockCount);
        Assert.True(first.TextStability.IsRequired);
        Assert.False(first.TextStability.IsStable);
        Assert.Equal(1, first.TextStability.ObservationCount);
        Assert.Equal(1, first.TextStability.RequiredObservationCount);
        Assert.Equal(OcrTime, first.TextStability.FirstObservedAt);
        Assert.Equal(OcrTime.AddMilliseconds(500), second.TextStability.LastObservedAt);
        Assert.Equal(TimeSpan.FromMilliseconds(500), second.TextStability.ObservedDuration);
        Assert.Empty(first.OverlaySnapshot.TextItems);
        Assert.Empty(second.OverlaySnapshot.TextItems);
        Assert.Equal(1, translator.CallCount);
        Assert.Equal(1, third.TranslationInputBlockCount);
        Assert.True(third.TextStability.IsStable);
        Assert.Equal(3, third.TextStability.ObservationCount);
        Assert.Equal(TimeSpan.FromMilliseconds(1100), third.TextStability.ObservedDuration);
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
        Assert.Contains("Candidate-region detector is unavailable", failure.Message, StringComparison.Ordinal);
        Assert.Empty(result.OverlaySnapshot.TextItems);
    }

    [Theory]
    [InlineData("YandexWeb")]
    [InlineData("BingWeb")]
    public async Task LiveSession_WhenExplicitWebProviderIsConfigured_UsesItForRealCandidateWorkWithoutPrewarm(
        string providerId)
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = providerId,
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
        var translator = new FakeTranslatorProvider(providerId);
        var overlay = new FakeOverlayService();
        var service = CreateService(frameSource, ocrEngine, translator, overlay, candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true));

        var live = await session.RefreshAsync();
        await WaitForConditionAsync(
            () => overlay.CurrentSnapshot?.TextItems.SingleOrDefault()?.Text == "Translated Candidate");

        Assert.Equal(CandidatePipelineReadinessStatus.Ready, live.CandidateReadiness.Status);
        Assert.Equal(1, detectorCallCount);
        Assert.Equal(1, translator.CallCount);
        Assert.Equal(new[] { "Show:1" }, overlay.Events);
    }

    [Fact]
    public async Task LiveSession_WhenCandidateOcrFails_RecordsImmediateAndRootCauseDiagnostics()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = Enumerable.Range(1, 8)
                .Select(frameMarker => CreateCandidatePilotPixels(
                    zone,
                    (byte)frameMarker,
                    (candidateBounds, (byte)10)))
                .ToArray(),
        };
        var rootCause = new DllNotFoundException("x64 native\tload failed");
        var ocrFailure = new OcrEngineException("Tesseract failed\nfor the candidate crop", rootCause);
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            FailureFactory = _ => ocrFailure,
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        var lifecycleEvents = new List<LiveCandidateLifecycleEvent>();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var update = await session.RefreshAsync();
            lifecycleEvents.AddRange(update.CandidateLifecycleEvents);
            if (lifecycleEvents.Any(entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkFailed))
            {
                break;
            }

            await Task.Yield();
        }

        var failure = Assert.Single(
            lifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkFailed);
        Assert.Equal(TranslationPipelineStage.Ocr, failure.FailureStage);
        Assert.Equal(typeof(OcrEngineException).FullName, failure.FailureExceptionType);
        Assert.Equal("Tesseract failed for the candidate crop", failure.FailureExceptionMessage);
        Assert.Equal(typeof(DllNotFoundException).FullName, failure.FailureRootCauseType);
        Assert.Equal("x64 native load failed", failure.FailureRootCauseMessage);
        Assert.Equal(0, translator.CallCount);
    }

    [Fact]
    public async Task LiveSession_WhenCandidateCaptureIsLost_RemovesPublishedOverlayWithoutBlockingFutureRefreshes()
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
                enableCandidateDetectorPilot: true));

        await session.RefreshAsync();
        await session.RefreshAsync();
        var published = await session.RefreshAsync();
        Assert.Equal(CandidatePipelineReadinessStatus.Ready, published.CandidateReadiness.Status);
        Assert.Single(published.BatchResult.OverlaySnapshot.TextItems);

        var afterCaptureLoss = await session.RefreshAsync();

        Assert.Equal(CandidatePipelineReadinessStatus.Ready, afterCaptureLoss.CandidateReadiness.Status);
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
                enableCandidateDetectorPilot: true));

        await session.RefreshAsync();
        await session.RefreshAsync();
        var detectorUnavailable = await session.RefreshAsync();
        await oldCandidateTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(CandidatePipelineReadinessStatus.Ready, detectorUnavailable.CandidateReadiness.Status);
        Assert.Contains(detectorUnavailable.CancelledZoneIds, id => id.Contains(":candidate:", StringComparison.Ordinal));
        Assert.Empty(detectorUnavailable.BatchResult.OverlaySnapshot.TextItems);

        releaseOldCandidateTranslation.TrySetResult(true);
        var recovered = await session.RefreshAsync();
        Assert.Equal(CandidatePipelineReadinessStatus.Ready, recovered.CandidateReadiness.Status);
        for (var attempt = 0; attempt < 20 && overlay.CurrentSnapshot?.TextItems.Count != 1; attempt++)
        {
            await Task.Delay(10);
            await session.RefreshAsync();
        }

        Assert.Single(overlay.CurrentSnapshot!.TextItems);
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
    public async Task RunAllZonesAsync_CandidatePipelineDoesNotRequireLivePreflight()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var service = CreateService(
            new FakeCaptureFrameSource(),
            new FakeOcrEngine(),
            new FakeTranslatorProvider("GoogleWeb"),
            new FakeOverlayService());

        var result = await service.RunAllZonesAsync(
            CreateProfile(zone),
            runOptions: new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true));

        var failure = Assert.Single(result.ZoneFailures);
        Assert.Equal(TranslationPipelineStage.Ocr, failure.Stage);
    }

    [Fact]
    public async Task RunAllZonesAsync_VerticalJapaneseCandidatePipeline_RejectsWideRepeatedOcrResult()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 8, 80, 10);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
            },
        };
        var repeatedBlocks = Enumerable.Range(0, 10)
            .Select(index => new OcrTextBlock("字幕", new BoundingBox(index * 2, 0, 2, 8)))
            .ToArray();
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => repeatedBlocks,
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            candidateDetector: detector,
            candidateRegionOcrOptions: new TextCandidateRegionOcrOptions
            {
                EnableCjkTargetPostFilter = true,
            });

        var result = await service.RunAllZonesAsync(
            profile,
            runOptions: new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));

        Assert.Single(ocrEngine.Requests);
        Assert.Equal(OcrOrientationMode.Vertical, ocrEngine.Requests[0].OrientationMode);
        Assert.Equal(0, translator.CallCount);
        Assert.Equal(0, result.RecognizedBlockCount);
        Assert.Equal(0, result.TranslatedBlockCount);
        Assert.Empty(result.OverlaySnapshot.TextItems);
        Assert.Empty(result.ZoneFailures);
    }

    [Fact]
    public async Task LiveSession_VerticalJapaneseCandidatePipeline_RejectsWideRepeatedOcrResult()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 8, 80, 10);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
            },
        };
        var repeatedBlocks = Enumerable.Range(0, 10)
            .Select(index => new OcrTextBlock("字幕", new BoundingBox(index * 2, 0, 2, 8)))
            .ToArray();
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => repeatedBlocks,
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            candidateDetector: detector,
            candidateRegionOcrOptions: new TextCandidateRegionOcrOptions
            {
                EnableCjkTargetPostFilter = true,
            });

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        var update = await session.RefreshAsync();
        if (!update.CandidateLifecycleEvents.Any(
                entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCompleted))
        {
            await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
            update = await session.PublishCompletedWorkAsync();
        }

        Assert.Single(ocrEngine.Requests);
        Assert.Equal(0, translator.CallCount);
        Assert.Empty(update.BatchResult.OverlaySnapshot.TextItems);
        Assert.Contains(
            update.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCompleted
                && entry.RecognizedBlockCount == 0
                && entry.TranslationInputBlockCount == 0
                && entry.TranslatedBlockCount == 0);
    }

    [Fact]
    public async Task LiveSession_VerticalJapaneseCandidate_WhenYandexRepeatsTranslation_PublishesCollapsedText()
    {
        const string sourceText = "\u3053\u306e\u30b2\u30fc\u30e0\u304c\u5927\u597d\u304d";
        const string repeatedTranslation = "I love this game. I love this game. I love this game. I love this game. I love this game.";
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "YandexWeb",
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 5, 12, 30);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock(sourceText, new BoundingBox(0, 0, 12, 30)),
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var translator = new FakeTranslatorProvider(
            "YandexWeb",
            translatedTexts: new[] { repeatedTranslation });
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector,
            candidateRegionOcrOptions: new TextCandidateRegionOcrOptions
            {
                EnableCjkTargetPostFilter = true,
            });

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        var update = await session.RefreshAsync();
        if (!update.CandidateLifecycleEvents.Any(
                entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCompleted))
        {
            await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
            update = await session.PublishCompletedWorkAsync();
        }

        Assert.Equal(1, translator.CallCount);
        Assert.Equal(
            new[] { "I love this game." },
            update.BatchResult.OverlaySnapshot.TextItems.Select(item => item.Text));
        Assert.Contains(
            update.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCompleted
                && entry.TranslationOutputSanitizedCount == 1);
    }

    [Fact]
    public async Task LiveSession_VerticalJapaneseCandidate_OrdersOcrColumnsRightToLeftBeforeTranslation()
    {
        const string expectedSourceText = "\u3053\u306e\u30b2\u30fc\u30e0\u304c\u5927\u597d\u304d";
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 80));
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = "ja",
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 5, 36, 60);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("\u5927\u597d\u304d", new BoundingBox(2, 4, 8, 30)),
                new OcrTextBlock("\u3053\u306e", new BoundingBox(26, 4, 8, 30)),
                new OcrTextBlock("\u30b2\u30fc\u30e0\u304c", new BoundingBox(14, 4, 8, 30)),
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) },
            new TextCandidateDetectionDiagnostics(
                TextCandidateDetectorPreset.Standard,
                TextCandidateDetectorPreset.Standard,
                0.30,
                0.60,
                1.20,
                1,
                0.95,
                0.95,
                0.95)));
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            candidateDetector: detector,
            candidateRegionOcrOptions: new TextCandidateRegionOcrOptions
            {
                EnableCjkTargetPostFilter = true,
            });

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        var update = await session.RefreshAsync();
        if (!update.CandidateLifecycleEvents.Any(
                entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCompleted))
        {
            await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
            update = await session.PublishCompletedWorkAsync();
        }

        Assert.NotNull(translator.Request);
        Assert.Equal(new[] { expectedSourceText }, translator.Request.Texts);
        Assert.Equal(
            new[] { $"Translated {expectedSourceText}" },
            update.BatchResult.OverlaySnapshot.TextItems.Select(item => item.Text));
        var completed = Assert.Single(
            update.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCompleted);
        Assert.Equal(3, completed.RecognizedBlockCount);
        Assert.Equal(1, completed.TranslationInputBlockCount);
        Assert.Equal(1, completed.TranslatedBlockCount);
        Assert.Equal(WritingSystemGroupingProfile.CjkVertical, completed.WritingSystemGroupingProfile);
        Assert.Equal(OcrOrientationMode.Vertical, completed.OcrOrientationMode);
        Assert.Equal(0.95, completed.CandidateConfidence);
        var detectionCompleted = Assert.Single(
            update.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateDetectionCompleted);
        Assert.Equal(TextCandidateDetectorPreset.Standard, detectionCompleted.RequestedDetectorPreset);
        Assert.Equal(TextCandidateDetectorPreset.Standard, detectionCompleted.EffectiveDetectorPreset);
        Assert.Equal(0.60, detectionCompleted.DetectorBoxThreshold);
        Assert.Equal(1, detectionCompleted.RawDetectorCandidateCount);
        Assert.Equal(0.95, detectionCompleted.AverageDetectorConfidence);
        Assert.Equal(
            new[]
            {
                new BoundingBox(2, 4, 8, 30),
                new BoundingBox(26, 4, 8, 30),
                new BoundingBox(14, 4, 8, 30),
            },
            completed.OrderedOcrBlockBounds);
        Assert.Equal(
            new[]
            {
                new BoundingBox(26, 4, 8, 30),
                new BoundingBox(14, 4, 8, 30),
                new BoundingBox(2, 4, 8, 30),
            },
            completed.OrderedGroupedMemberBounds);
        Assert.Equal(3, completed.OrderedOcrBlockBoundsCount);
        Assert.Equal(3, completed.OrderedGroupedMemberBoundsCount);
        Assert.NotNull(completed.OrderedOcrBlockBoundsFingerprint);
        Assert.NotNull(completed.OrderedGroupedMemberBoundsFingerprint);
        Assert.NotEqual(
            completed.OrderedOcrBlockBoundsFingerprint,
            completed.OrderedGroupedMemberBoundsFingerprint);
    }

    [Fact]
    public void LiveCandidateLifecycleEvent_OrderedGeometryDiagnosticsAreBoundedAndFingerprintTheFullSequence()
    {
        var bounds = Enumerable.Range(0, 130)
            .Select(index => new BoundingBox(index, index + 1, 8, 30))
            .ToArray();

        var lifecycleEvent = new LiveCandidateLifecycleEvent(
            sequence: 1,
            refreshSequence: 1,
            occurredAt: DateTimeOffset.UnixEpoch,
            kind: LiveCandidateLifecycleEventKind.CandidateWorkCompleted,
            orderedOcrBlockBounds: bounds,
            orderedGroupedMemberBounds: bounds.Reverse(),
            writingSystemGroupingProfile: WritingSystemGroupingProfile.CjkVertical,
            ocrOrientationMode: OcrOrientationMode.Vertical);

        Assert.Equal(130, lifecycleEvent.OrderedOcrBlockBoundsCount);
        Assert.Equal(128, lifecycleEvent.OrderedOcrBlockBounds.Count);
        Assert.Equal(130, lifecycleEvent.OrderedGroupedMemberBoundsCount);
        Assert.Equal(128, lifecycleEvent.OrderedGroupedMemberBounds.Count);
        Assert.NotEqual(
            lifecycleEvent.OrderedOcrBlockBoundsFingerprint,
            lifecycleEvent.OrderedGroupedMemberBoundsFingerprint);
    }

    [Theory]
    [InlineData("ja", "jpn_vert", true)]
    [InlineData("en", "eng", false)]
    public async Task LiveSession_TextThatGrowsByPrefix_AppliesAdditionalQuietWindowOnlyToVerticalCjk(
        string sourceLanguage,
        string ocrLanguage,
        bool expectGrowthGuard)
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 80)) with
        {
            OcrLanguage = ocrLanguage,
        };
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "Google",
                SourceLanguage = sourceLanguage,
                TargetLanguage = "ru",
            },
        };
        var candidateBounds = new BoundingBox(8, 5, 36, 60);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (candidateBounds, (byte)20)),
                CreateCandidatePilotPixels(zone, frameMarker: 3, (candidateBounds, (byte)20)),
                CreateCandidatePilotPixels(zone, frameMarker: 4, (candidateBounds, (byte)20)),
            },
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime.AddMilliseconds(100),
                FrameTime.AddMilliseconds(399),
                FrameTime.AddMilliseconds(400),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            RecognizedAtFrames = new[]
            {
                OcrTime,
                OcrTime.AddMilliseconds(100),
                OcrTime.AddMilliseconds(399),
                OcrTime.AddMilliseconds(400),
            },
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 10 ? "Partial" : "Partial complete",
                    new BoundingBox(0, 0, 12, 30)),
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            candidateDetector: detector);
        var options = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.Zero,
            enableCandidateDetectorPilot: true,
            minimumCandidateGroupingObservations: 1,
            minimumStableTextObservations: 1)
        {
            MinimumCandidateGroupingDuration = TimeSpan.Zero,
        };

        using var session = service.CreateLiveSession(profile, options);
        await RefreshAndPublishAsync();
        Assert.Equal(1, translator.CallCount);

        var growing = await RefreshAndPublishAsync();
        Assert.Equal(expectGrowthGuard ? 1 : 2, translator.CallCount);
        if (!expectGrowthGuard)
        {
            Assert.DoesNotContain(
                growing.CandidateLifecycleEvents,
                entry => entry.TextStability?.TypewriterGrowthGuardApplied == true);
            Assert.Equal(new[] { "Partial complete" }, translator.Request!.Texts);
            return;
        }

        Assert.Contains(
            growing.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkDeferredForStability
                && entry.TextStability?.TypewriterGrowthGuardApplied == true
                && entry.TextStability.RequiredDuration == TimeSpan.FromMilliseconds(300));

        await RefreshAndPublishAsync();
        Assert.Equal(1, translator.CallCount);

        await RefreshAndPublishAsync();
        Assert.Equal(2, translator.CallCount);
        Assert.Equal(new[] { "Partial complete" }, translator.Request!.Texts);

        async Task<LiveTranslationPipelineUpdate> RefreshAndPublishAsync()
        {
            var update = await session.RefreshAsync();
            if (update.CandidateLifecycleEvents.Any(entry =>
                    entry.Kind is LiveCandidateLifecycleEventKind.CandidateWorkCompleted
                        or LiveCandidateLifecycleEventKind.CandidateWorkDeferredForStability))
            {
                return update;
            }

            await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
            return await session.PublishCompletedWorkAsync();
        }
    }

    [Fact]
    public void LiveCandidateLifecycleEvent_LocalTextDiagnosticsAreBoundedAndSingleLine()
    {
        var texts = Enumerable.Range(0, 18)
            .Select(index => $"Entry {index}\r\n{new string('x', 600)}")
            .ToArray();
        texts[0] = $"{new string('x', 511)}\U0001F600tail";

        var lifecycleEvent = new LiveCandidateLifecycleEvent(
            sequence: 1,
            refreshSequence: 1,
            occurredAt: DateTimeOffset.UnixEpoch,
            kind: LiveCandidateLifecycleEventKind.CandidateWorkCompleted,
            ocrTexts: texts,
            translationInputTexts: texts,
            translatedTexts: texts);

        Assert.Equal(18, lifecycleEvent.OcrTextCount);
        Assert.Equal(16, lifecycleEvent.OcrTexts.Count);
        Assert.Equal(18, lifecycleEvent.TranslationInputTextCount);
        Assert.Equal(16, lifecycleEvent.TranslationInputTexts.Count);
        Assert.Equal(18, lifecycleEvent.TranslatedTextCount);
        Assert.Equal(16, lifecycleEvent.TranslatedTexts.Count);
        Assert.All(
            lifecycleEvent.OcrTexts
                .Concat(lifecycleEvent.TranslationInputTexts)
                .Concat(lifecycleEvent.TranslatedTexts),
            text =>
            {
                Assert.DoesNotContain('\r', text);
                Assert.DoesNotContain('\n', text);
                Assert.True(text.Length <= 512);
                Assert.False(char.IsHighSurrogate(text[^1]));
            });
    }

    [Fact]
    public async Task LiveSession_CandidateWorkStarted_IsRecordedBeforeOcrBegins()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        DateTimeOffset? ocrStartedAt = null;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            OnRecognize = () => ocrStartedAt = DateTimeOffset.UtcNow,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)),
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

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));

        var update = await session.RefreshAsync();

        var startedEvent = Assert.Single(
            update.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkStarted);
        Assert.NotNull(ocrStartedAt);
        Assert.True(startedEvent.OccurredAt <= ocrStartedAt.Value);
    }

    [Fact]
    public async Task LiveSession_WhenCandidateCompletesBetweenDetectorPolls_PublishesFromCompletionSignal()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var translationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                translationStarted.TrySetResult(true);
                await releaseTranslation.Task.WaitAsync(cancellationToken);
                return new TranslateResponse(new[] { $"Translated {Assert.Single(request.Texts)}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateCandidateLiveService(zone, candidateBounds, translator, overlay);

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        var refreshUpdate = await session.RefreshAsync();
        await translationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(refreshUpdate.OverlayChanged);

        releaseTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var completionUpdate = await session.PublishCompletedWorkAsync();

        Assert.True(completionUpdate.OverlayChanged);
        Assert.Equal(
            new[] { "Translated Candidate" },
            completionUpdate.BatchResult.OverlaySnapshot.TextItems.Select(item => item.Text));
        var completedEvent = Assert.Single(
            completionUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCompleted);
        Assert.Equal(new[] { "Candidate" }, completedEvent.OcrTexts);
        Assert.Equal(new[] { "Candidate" }, completedEvent.TranslationInputTexts);
        Assert.Equal(new[] { "Translated Candidate" }, completedEvent.TranslatedTexts);
        Assert.Contains(
            completionUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.OverlaySnapshotPublished);
    }

    [Fact]
    public async Task LiveSession_WhenRevisionChangesBeforeCompletedWorkIsPublished_DoesNotPublishStaleResult()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var oldTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseNewTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (candidateBounds, (byte)30)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 10 ? "Old candidate" : "New candidate",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Old candidate", StringComparison.Ordinal))
                {
                    oldTranslationStarted.TrySetResult(true);
                    await releaseOldTranslation.Task;
                }
                else
                {
                    await releaseNewTranslation.Task.WaitAsync(cancellationToken);
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);
        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        await oldTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var revisionUpdate = await session.RefreshAsync();
        Assert.Contains(
            revisionUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateSourceChanged
                && entry.CandidateRevision == 2);

        releaseOldTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var staleCompletionUpdate = await session.PublishCompletedWorkAsync();

        Assert.False(staleCompletionUpdate.OverlayChanged);
        Assert.Empty(staleCompletionUpdate.BatchResult.OverlaySnapshot.TextItems);
        Assert.DoesNotContain(
            overlay.Events,
            entry => string.Equals(entry, "Show:1", StringComparison.Ordinal));

        releaseNewTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var currentCompletionUpdate = await session.PublishCompletedWorkAsync();
        Assert.Equal(
            new[] { "Translated New candidate" },
            currentCompletionUpdate.BatchResult.OverlaySnapshot.TextItems.Select(item => item.Text));
    }

    [Fact]
    public async Task LiveSession_WhenPublishedCandidateSourceChanges_KeepsOverlayUntilReplacementCompletes()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var oldTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseNewTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (candidateBounds, (byte)30)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 10 ? "Old candidate" : "New candidate",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Old candidate", StringComparison.Ordinal))
                {
                    oldTranslationStarted.TrySetResult(true);
                    await releaseOldTranslation.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    newTranslationStarted.TrySetResult(true);
                    await releaseNewTranslation.Task.WaitAsync(cancellationToken);
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);
        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        await oldTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        releaseOldTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var oldCompletionUpdate = await session.PublishCompletedWorkAsync();
        Assert.Equal(
            new[] { "Translated Old candidate" },
            oldCompletionUpdate.BatchResult.OverlaySnapshot.TextItems.Select(item => item.Text));

        var revisionUpdate = await session.RefreshAsync();
        await newTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Contains(
            revisionUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateSourceChanged
                && entry.CandidateRevision == 2);
        Assert.Empty(revisionUpdate.BatchResult.OverlaySnapshot.TextItems);
        Assert.False(revisionUpdate.OverlayChanged);
        Assert.Equal(
            new[] { "Translated Old candidate" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
        Assert.DoesNotContain(overlay.Events, entry => string.Equals(entry, "Show:0", StringComparison.Ordinal));

        releaseNewTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var newCompletionUpdate = await session.PublishCompletedWorkAsync();

        Assert.True(newCompletionUpdate.OverlayChanged);
        Assert.Equal(
            new[] { "Translated New candidate" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
        Assert.Equal(new[] { "Show:1", "Show:1" }, overlay.Events);
    }

    [Fact]
    public async Task LiveSession_WhenBingReplacementTimesOut_KeepsPublishedOverlayAndReportsFailure()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var nextRetryAt = new DateTimeOffset(2026, 9, 2, 18, 30, 0, TimeSpan.Zero);
        var oldTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReplacementFailure = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (candidateBounds, (byte)30)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 10 ? "Old candidate" : "Replacement candidate",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider(
            "BingWeb",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                var diagnostics = Assert.IsType<TranslationProviderRequestDiagnostics>(request.Diagnostics);
                var requestStartedAt = DateTimeOffset.UtcNow;
                diagnostics.MarkProviderInvocationStarted("BingWeb", requestStartedAt);
                var networkAttemptId = diagnostics.MarkNetworkRequestStarted(
                    TranslationProviderNetworkRequestKind.Translation,
                    requestStartedAt);
                if (string.Equals(text, "Old candidate", StringComparison.Ordinal))
                {
                    oldTranslationStarted.TrySetResult(true);
                    await releaseOldTranslation.Task.WaitAsync(cancellationToken);
                    diagnostics.MarkNetworkRequestCompleted(
                        networkAttemptId,
                        TranslationProviderNetworkRequestOutcome.Succeeded,
                        DateTimeOffset.UtcNow,
                        HttpStatusCode.OK);
                    diagnostics.MarkProviderInvocationCompleted(
                        TranslationProviderInvocationOutcome.Succeeded,
                        DateTimeOffset.UtcNow);
                    return new TranslateResponse(new[] { "Translated Old candidate" }, TranslatedAt, "BingWeb");
                }

                replacementTranslationStarted.TrySetResult(true);
                await releaseReplacementFailure.Task.WaitAsync(cancellationToken);
                diagnostics.MarkNetworkRequestCompleted(
                    networkAttemptId,
                    TranslationProviderNetworkRequestOutcome.Timeout,
                    DateTimeOffset.UtcNow);
                diagnostics.MarkProviderInvocationCompleted(
                    TranslationProviderInvocationOutcome.Failed,
                    DateTimeOffset.UtcNow,
                    TranslatorProviderFailureKind.Timeout);
                throw new TranslatorProviderException(
                    "BingWeb",
                    TranslatorProviderFailureKind.Timeout,
                    "BingWeb did not respond within 15 seconds.",
                    retryAfter: TimeSpan.FromSeconds(60),
                    consecutiveFailureCount: 2,
                    nextRetryAt: nextRetryAt);
            });
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);
        var profile = CreateProfile(zone) with
        {
            TranslatorSettings = new TranslatorSettings
            {
                Provider = "BingWeb",
                SourceLanguage = "en",
                TargetLanguage = "ru",
            },
        };

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        await oldTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        releaseOldTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await session.PublishCompletedWorkAsync();
        Assert.Equal("Translated Old candidate", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);

        var replacementUpdate = await session.RefreshAsync();
        await replacementTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(replacementUpdate.OverlayChanged);
        releaseReplacementFailure.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var failureUpdate = await session.PublishCompletedWorkAsync();

        var failure = Assert.Single(failureUpdate.BatchResult.ZoneFailures);
        Assert.Equal(TranslationPipelineStage.Translation, failure.Stage);
        Assert.IsType<TranslatorProviderException>(failure.Exception.InnerException);
        Assert.False(failureUpdate.OverlayChanged);
        Assert.Equal("Translated Old candidate", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);
        Assert.DoesNotContain(overlay.Events, entry => string.Equals(entry, "Show:0", StringComparison.Ordinal));
        var lifecycleFailure = Assert.Single(
            failureUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkFailed);
        Assert.Equal("BingWeb", lifecycleFailure.FailureProviderId);
        Assert.Equal(TranslatorProviderFailureKind.Timeout, lifecycleFailure.FailureProviderKind);
        Assert.Null(lifecycleFailure.FailureProviderHttpStatusCode);
        Assert.True(lifecycleFailure.FailureProviderPaused);
        Assert.Equal(TimeSpan.FromSeconds(60), lifecycleFailure.FailureProviderRetryAfter);
        Assert.Equal(nextRetryAt, lifecycleFailure.FailureProviderNextRetryAt);
        Assert.Equal(2, lifecycleFailure.FailureProviderConsecutiveFailureCount);
        var providerAttempt = Assert.Single(
            failureUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateProviderRequestObserved);
        Assert.True(providerAttempt.ProviderNetworkRequestSent);
        Assert.Equal(TranslationProviderNetworkRequestKind.Translation, providerAttempt.ProviderNetworkRequestKind);
        Assert.Equal(TranslationProviderNetworkRequestOutcome.Timeout, providerAttempt.ProviderNetworkRequestOutcome);
        Assert.Null(providerAttempt.ProviderNetworkHttpStatusCode);
        Assert.Equal(new[] { "Replacement candidate" }, providerAttempt.TranslationInputTexts);
    }

    [Fact]
    public async Task LiveSession_WhenDeferredReplacementDisappears_PublishesConfirmedEmptyOverlay()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var oldTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementTranslationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementTranslationCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (candidateBounds, (byte)30)),
                CreateCandidatePilotPixels(zone, frameMarker: 3),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 10 ? "Old candidate" : "Replacement candidate",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "Old candidate", StringComparison.Ordinal))
                {
                    oldTranslationStarted.TrySetResult(true);
                    await releaseOldTranslation.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    replacementTranslationStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        replacementTranslationCancelled.TrySetResult(true);
                        throw;
                    }
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var detectorCallCount = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            Interlocked.Increment(ref detectorCallCount) <= 2
                ? new[] { new TextCandidate(candidateBounds, 0.95) }
                : Array.Empty<TextCandidate>()));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        await oldTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        releaseOldTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await session.PublishCompletedWorkAsync();

        var replacementUpdate = await session.RefreshAsync();
        await replacementTranslationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(replacementUpdate.OverlayChanged);
        Assert.Equal(
            new[] { "Translated Old candidate" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));

        var disappearanceUpdate = await session.RefreshAsync();
        await replacementTranslationCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(disappearanceUpdate.OverlayChanged);
        Assert.Empty(disappearanceUpdate.BatchResult.OverlaySnapshot.TextItems);
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
        Assert.Equal(new[] { "Show:1", "Show:0" }, overlay.Events);
    }

    [Fact]
    public async Task LiveSession_WhenPublishedCandidateHasOneEmptyDetectorFrame_KeepsOverlayUntilItReturns()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2),
                CreateCandidatePilotPixels(zone, frameMarker: 3, (candidateBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detectorCallCount = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            Interlocked.Increment(ref detectorCallCount) == 2
                ? Array.Empty<TextCandidate>()
                : new[] { new TextCandidate(candidateBounds, 0.95) }));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true,
                minimumCandidateOverlayVisibleDuration: TimeSpan.FromSeconds(2)));

        var publishedUpdate = await session.RefreshAsync();
        var dropoutUpdate = await session.RefreshAsync();
        var recoveredUpdate = await session.RefreshAsync();

        Assert.True(publishedUpdate.OverlayChanged);
        Assert.False(dropoutUpdate.OverlayChanged);
        Assert.Empty(dropoutUpdate.BatchResult.OverlaySnapshot.TextItems);
        Assert.Equal("Translated Candidate", Assert.Single(overlay.CurrentSnapshot!.TextItems).Text);
        Assert.True(recoveredUpdate.OverlayChanged);
        Assert.DoesNotContain(overlay.Events, entry => string.Equals(entry, "Show:0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LiveSession_WhenEmptyDetectorFramesOutlastReadabilityGrace_RemovesPublishedOverlay()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2),
                CreateCandidatePilotPixels(zone, frameMarker: 3),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detectorCallCount = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            Interlocked.Increment(ref detectorCallCount) == 1
                ? new[] { new TextCandidate(candidateBounds, 0.95) }
                : Array.Empty<TextCandidate>()));
        var overlay = new FakeOverlayService();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            overlay,
            candidateDetector: detector,
            timeProvider: timeProvider);

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(
                enableCandidateDetectorPilot: true,
                minimumCandidateOverlayVisibleDuration: TimeSpan.FromSeconds(2)));

        await session.RefreshAsync();
        var withinGrace = await session.RefreshAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var afterGrace = await session.RefreshAsync();

        Assert.False(withinGrace.OverlayChanged);
        Assert.True(afterGrace.OverlayChanged);
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
        Assert.Equal(new[] { "Show:1", "Show:0" }, overlay.Events);
    }

    [Fact]
    public async Task LiveSession_WhenCandidateReturnsWithDifferentSourceDuringReadabilityGrace_RemovesStaleOverlayImmediately()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 3),
                CreateCandidatePilotPixels(zone, frameMarker: 4, (candidateBounds, (byte)20)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detectorCallCount = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            Interlocked.Increment(ref detectorCallCount) == 3
                ? Array.Empty<TextCandidate>()
                : new[] { new TextCandidate(candidateBounds, 0.95) }));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(
                requireStableTextBeforeTranslation: true,
                stableTextInterval: TimeSpan.Zero,
                enableCandidateDetectorPilot: true,
                minimumCandidateOverlayVisibleDuration: TimeSpan.FromSeconds(2)));

        await session.RefreshAsync();
        var published = await session.RefreshAsync();
        var dropout = await session.RefreshAsync();
        var changedSource = await session.RefreshAsync();

        Assert.True(published.OverlayChanged);
        Assert.False(dropout.OverlayChanged);
        Assert.True(changedSource.OverlayChanged);
        Assert.Empty(overlay.CurrentSnapshot!.TextItems);
        Assert.Equal(new[] { "Show:1", "Show:0" }, overlay.Events);
    }

    [Fact]
    public async Task LiveSession_WhenCandidateDisappearsBeforeLateCompletion_DoesNotRepublishIt()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var translationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, _) =>
            {
                translationStarted.TrySetResult(true);
                await releaseTranslation.Task;
                return new TranslateResponse(new[] { $"Translated {Assert.Single(request.Texts)}" }, TranslatedAt);
            });
        var detectorCallCount = 0;
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            Interlocked.Increment(ref detectorCallCount) == 1
                ? new[] { new TextCandidate(candidateBounds, 0.95) }
                : Array.Empty<TextCandidate>()));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        await translationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var disappearanceUpdate = await session.RefreshAsync();
        Assert.Contains(
            disappearanceUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateRemoved
                && entry.CancellationReason == LiveCandidateCancellationReason.CandidateDisappeared);

        releaseTranslation.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var completionUpdate = await session.PublishCompletedWorkAsync();

        Assert.False(completionUpdate.OverlayChanged);
        Assert.Empty(completionUpdate.BatchResult.OverlaySnapshot.TextItems);
        Assert.DoesNotContain(overlay.Events, entry => string.Equals(entry, "Show:1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LiveSession_WhenStoppedWhileWaitingForCompletion_CancelsTheWaitAndPublishesNothing()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var candidateBounds = new BoundingBox(8, 8, 30, 12);
        var translationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTranslation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, _) =>
            {
                translationStarted.TrySetResult(true);
                await releaseTranslation.Task;
                return new TranslateResponse(new[] { $"Translated {Assert.Single(request.Texts)}" }, TranslatedAt);
            });
        var overlay = new FakeOverlayService();
        var service = CreateCandidateLiveService(zone, candidateBounds, translator, overlay);
        using var sessionCancellation = new CancellationTokenSource();
        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true),
            sessionCancellation.Token);
        await session.RefreshAsync();
        await translationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var completionWait = session.WaitForWorkCompletionAsync();
        sessionCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completionWait);
        Assert.Null(overlay.CurrentSnapshot);
        releaseTranslation.TrySetResult(true);
    }

    [Fact]
    public async Task LiveSession_WhenConcurrentCandidatesComplete_PublishesEachWithoutWaitingForItsSibling()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var firstBounds = new BoundingBox(8, 8, 30, 12);
        var secondBounds = new BoundingBox(55, 8, 30, 12);
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(
                    zone,
                    frameMarker: 1,
                    (firstBounds, (byte)10),
                    (secondBounds, (byte)20)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 10 ? "First" : "Second",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider(
            "Google",
            translateAsync: async (request, cancellationToken) =>
            {
                var text = Assert.Single(request.Texts);
                if (string.Equals(text, "First", StringComparison.Ordinal))
                {
                    firstStarted.TrySetResult(true);
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    secondStarted.TrySetResult(true);
                    await releaseSecond.Task.WaitAsync(cancellationToken);
                }

                return new TranslateResponse(new[] { $"Translated {text}" }, TranslatedAt);
            });
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[]
            {
                new TextCandidate(firstBounds, 0.95),
                new TextCandidate(secondBounds, 0.90),
            }));
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            CreateProfile(zone),
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        await Task.WhenAll(
            firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1)),
            secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(1)));

        releaseSecond.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var secondCompletion = await session.PublishCompletedWorkAsync();
        Assert.Equal(
            new[] { "Translated Second" },
            secondCompletion.BatchResult.OverlaySnapshot.TextItems.Select(item => item.Text));

        releaseFirst.TrySetResult(true);
        await session.WaitForWorkCompletionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var firstCompletion = await session.PublishCompletedWorkAsync();
        Assert.Equal(
            new[] { "Translated First", "Translated Second" },
            firstCompletion.BatchResult.OverlaySnapshot.TextItems.Select(item => item.Text));
    }

    [Fact]
    public void LiveSession_CandidateReconciliation_DoesNotCreateAFullZoneFingerprintCopy()
    {
        var source = File.ReadAllText(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.Application",
                "Pipeline",
                "TranslationPipelineService.cs"));
        var methodStart = source.IndexOf(
            "private async Task<bool> ReconcileCandidateRegionsAsync",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private static LiveCandidateState? FindMatchingCandidateState",
            methodStart,
            StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);
        var candidateReconciliation = source[methodStart..methodEnd];
        Assert.DoesNotContain(
            "FrameFingerprint.FromFrame(capturedZone.Frame)",
            candidateReconciliation,
            StringComparison.Ordinal);
        Assert.Contains(
            "FrameFingerprint.FromFrame(region.Frame)",
            candidateReconciliation,
            StringComparison.Ordinal);
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
        Assert.Contains(
            firstUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateDiscovered
                && entry.CandidateBounds == slowCandidateBounds
                && entry.SourceCandidateBounds.SequenceEqual(new[] { slowCandidateBounds }));
        Assert.Contains(
            secondUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCancelled
                && entry.CandidateBounds == slowCandidateBounds
                && entry.CancellationReason == LiveCandidateCancellationReason.CandidateDisappeared);
        Assert.Contains(
            secondUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateRemoved
                && entry.CandidateBounds == slowCandidateBounds
                && entry.CancellationReason == LiveCandidateCancellationReason.CandidateDisappeared);
        Assert.Contains(
            firstUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.OverlaySnapshotPublished
                && entry.OverlayTextItemCount == 1);
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
        var replacementMemberBounds = new[]
        {
            new BoundingBox(8, 8, 14, 12),
            new BoundingBox(24, 8, 14, 12),
        };
        var detectorRequestCount = 0;
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
        var detector = new FakeCandidateDetector(_ =>
        {
            var changingSourceMembers = Interlocked.Increment(ref detectorRequestCount) == 1
                ? new[] { changingCandidateBounds }
                : replacementMemberBounds;
            return TextCandidateDetectionResult.Available(
                "test-detector",
                new[]
                {
                    new TextCandidate(changingCandidateBounds, 0.95)
                    {
                        SourceCandidateBounds = changingSourceMembers,
                    },
                    new TextCandidate(readyCandidateBounds, 0.90),
                });
        });
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
        Assert.Contains(
            secondUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGroupingChanged
                && entry.CandidateBounds == changingCandidateBounds
                && entry.SourceCandidateBounds.SequenceEqual(replacementMemberBounds));
        Assert.Equal(
            new[] { "Translated Replacement candidate", "Translated Ready candidate" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
    }

    [Fact]
    public async Task LiveSession_WhenStableTextIsRequired_WaitsForASecondMatchingGroupingBeforeTranslation()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var partialBounds = new BoundingBox(8, 8, 20, 12);
        var finalBounds = new BoundingBox(8, 8, 50, 12);
        var detectorCallCount = 0;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (partialBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (finalBounds, (byte)20)),
                CreateCandidatePilotPixels(zone, frameMarker: 3, (finalBounds, (byte)20)),
            },
        };
        var detector = new FakeCandidateDetector(_ =>
        {
            var bounds = Interlocked.Increment(ref detectorCallCount) == 1
                ? partialBounds
                : finalBounds;
            return TextCandidateDetectionResult.Available(
                "test-detector",
                new[] { new TextCandidate(bounds, 0.95) });
        });
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = request => new[]
            {
                new OcrTextBlock(
                    request.Frame.PixelData.Span[0] == 20 ? "Final group" : "Partial group",
                    new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google");
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(
                requireStableTextBeforeTranslation: true,
                stableTextInterval: TimeSpan.Zero,
                enableCandidateDetectorPilot: true));

        var firstUpdate = await session.RefreshAsync();
        var secondUpdate = await session.RefreshAsync();

        Assert.Equal(0, translator.CallCount);
        Assert.Contains(
            firstUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGroupingAwaitingConfirmation
                && entry.CandidateBounds == partialBounds);
        Assert.Contains(
            secondUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGroupingAwaitingConfirmation
                && entry.CandidateBounds == finalBounds);

        await session.RefreshAsync();
        await WaitForConditionAsync(
            () => translator.CallCount == 1 && overlay.CurrentSnapshot?.TextItems.Count == 1);

        Assert.Equal(new[] { "Final group" }, translator.Request!.Texts);
        Assert.Equal(
            new[] { "Translated Final group" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
    }

    [Fact]
    public async Task LiveSession_WhenGroupingObservationsArriveFasterThanMinimumDuration_DelaysTranslation()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var candidateBounds = new BoundingBox(8, 8, 50, 12);
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = Enumerable.Range(1, 4)
                .Select(frameMarker => CreateCandidatePilotPixels(
                    zone,
                    (byte)frameMarker,
                    (candidateBounds, (byte)20)))
                .ToArray(),
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime.AddMilliseconds(50),
                FrameTime.AddMilliseconds(199),
                FrameTime.AddMilliseconds(200),
            },
        };
        var detector = new FakeCandidateDetector(_ =>
            TextCandidateDetectionResult.Available(
                "test-detector",
                new[] { new TextCandidate(candidateBounds, 0.95) }));
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Stable candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            new FakeOverlayService(),
            candidateDetector: detector);
        var options = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.Zero,
            enableCandidateDetectorPilot: true,
            minimumCandidateGroupingObservations: 2,
            minimumStableTextObservations: 1)
        {
            MinimumCandidateGroupingDuration = TimeSpan.FromMilliseconds(200),
        };

        using var session = service.CreateLiveSession(profile, options);
        await session.RefreshAsync();
        await session.RefreshAsync();
        var justBeforeMinimumDuration = await session.RefreshAsync();

        Assert.Equal(0, translator.CallCount);
        Assert.Contains(
            justBeforeMinimumDuration.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGroupingAwaitingConfirmation
                && entry.GroupingObservationCount == 3
                && entry.GroupingObservedDuration == TimeSpan.FromMilliseconds(199)
                && entry.RequiredGroupingDuration == TimeSpan.FromMilliseconds(200));

        var minimumDurationReached = await session.RefreshAsync();
        await WaitForConditionAsync(() => translator.CallCount == 1);

        Assert.Contains(
            minimumDurationReached.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkStarted
                && entry.GroupingObservationCount == 4
                && entry.GroupingObservedDuration == TimeSpan.FromMilliseconds(200)
                && entry.RequiredGroupingDuration == TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task LiveSession_WhenGroupingRevisionChanges_ResetsMinimumGroupingDuration()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var candidateBounds = new BoundingBox(8, 8, 50, 12);
        var revisedMembers = new[]
        {
            new BoundingBox(8, 8, 24, 12),
            new BoundingBox(34, 8, 24, 12),
        };
        var detectorCallCount = 0;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = Enumerable.Range(1, 5)
                .Select(frameMarker => CreateCandidatePilotPixels(
                    zone,
                    (byte)frameMarker,
                    (candidateBounds, (byte)20)))
                .ToArray(),
            CapturedAtFrames = new[]
            {
                FrameTime,
                FrameTime.AddMilliseconds(150),
                FrameTime.AddMilliseconds(200),
                FrameTime.AddMilliseconds(350),
                FrameTime.AddMilliseconds(400),
            },
        };
        var detector = new FakeCandidateDetector(_ =>
        {
            var candidate = new TextCandidate(candidateBounds, 0.95);
            if (Interlocked.Increment(ref detectorCallCount) >= 3)
            {
                candidate = new TextCandidate(candidateBounds, 0.95)
                {
                    SourceCandidateBounds = revisedMembers,
                };
            }

            return TextCandidateDetectionResult.Available("test-detector", new[] { candidate });
        });
        var translator = new FakeTranslatorProvider("Google");
        var service = CreateService(
            frameSource,
            new FakeOcrEngine
            {
                EngineId = OcrSettings.TesseractEngineId,
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Revised candidate", new BoundingBox(0, 0, 20, 10)),
                },
            },
            translator,
            new FakeOverlayService(),
            candidateDetector: detector);
        var options = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.Zero,
            enableCandidateDetectorPilot: true,
            minimumCandidateGroupingObservations: 2,
            minimumStableTextObservations: 1)
        {
            MinimumCandidateGroupingDuration = TimeSpan.FromMilliseconds(200),
        };

        using var session = service.CreateLiveSession(profile, options);
        await session.RefreshAsync();
        await session.RefreshAsync();
        var revisedGrouping = await session.RefreshAsync();
        var revisedBeforeMinimumDuration = await session.RefreshAsync();

        Assert.Equal(0, translator.CallCount);
        Assert.Contains(
            revisedGrouping.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGroupingChanged
                && entry.CandidateRevision == 2
                && entry.GroupingObservationCount == 1
                && entry.GroupingObservedDuration == TimeSpan.Zero);
        Assert.Contains(
            revisedBeforeMinimumDuration.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGroupingAwaitingConfirmation
                && entry.CandidateRevision == 2
                && entry.GroupingObservationCount == 2
                && entry.GroupingObservedDuration == TimeSpan.FromMilliseconds(150));

        var revisedMinimumDurationReached = await session.RefreshAsync();
        await WaitForConditionAsync(() => translator.CallCount == 1);

        Assert.Contains(
            revisedMinimumDurationReached.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkStarted
                && entry.CandidateRevision == 2
                && entry.GroupingObservationCount == 3
                && entry.GroupingObservedDuration == TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task LiveSession_WhenGroupingRevisionChanges_ResetsMatchingOcrObservationCount()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var candidateBounds = new BoundingBox(8, 8, 50, 12);
        var revisedMembers = new[]
        {
            new BoundingBox(8, 8, 24, 12),
            new BoundingBox(34, 8, 24, 12),
        };
        var detectorCallCount = 0;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = Enumerable.Range(1, 6)
                .Select(frameMarker => CreateCandidatePilotPixels(
                    zone,
                    (byte)frameMarker,
                    (candidateBounds, (byte)20)))
                .ToArray(),
        };
        var detector = new FakeCandidateDetector(_ =>
        {
            var candidate = new TextCandidate(candidateBounds, 0.95);
            if (Interlocked.Increment(ref detectorCallCount) >= 3)
            {
                candidate = new TextCandidate(candidateBounds, 0.95)
                {
                    SourceCandidateBounds = revisedMembers,
                };
            }

            return TextCandidateDetectionResult.Available("test-detector", new[] { candidate });
        });
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Same OCR text", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var translator = new FakeTranslatorProvider("Google");
        var overlay = new FakeOverlayService();
        var service = CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);
        var options = new TranslationPipelineRunOptions(
            requireStableTextBeforeTranslation: true,
            stableTextInterval: TimeSpan.Zero,
            enableCandidateDetectorPilot: true,
            minimumCandidateGroupingObservations: 2,
            minimumStableTextObservations: 2);

        using var session = service.CreateLiveSession(profile, options);
        await session.RefreshAsync();
        var firstOcrObservation = await session.RefreshAsync();
        var revisedGrouping = await session.RefreshAsync();
        var revisedFirstOcrObservation = await session.RefreshAsync();

        Assert.Equal(0, translator.CallCount);
        Assert.Contains(
            firstOcrObservation.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkDeferredForStability
                && entry.TextStability?.ObservationCount == 1);
        Assert.Contains(
            revisedGrouping.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGroupingChanged
                && entry.CandidateRevision == 2);
        Assert.Contains(
            revisedFirstOcrObservation.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkDeferredForStability
                && entry.CandidateRevision == 2
                && entry.TextStability?.ObservationCount == 1);

        await session.RefreshAsync();
        await WaitForConditionAsync(() => translator.CallCount == 1);
        Assert.Single(overlay.CurrentSnapshot!.TextItems);
    }

    [Fact]
    public async Task LiveSession_WhenCandidateBoundsJitterWithinBound_KeepsTheCandidateIdentityAndDoesNotRetranslate()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var originalBounds = new BoundingBox(5, 8, 90, 12);
        var jitteredBounds = new BoundingBox(7, 8, 90, 12);
        var detectorRequestCount = 0;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (originalBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (jitteredBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Stable candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detector = new FakeCandidateDetector(_ =>
        {
            var bounds = Interlocked.Increment(ref detectorRequestCount) == 1
                ? originalBounds
                : jitteredBounds;
            return TextCandidateDetectionResult.Available(
                "test-detector",
                new[] { new TextCandidate(bounds, 0.95) });
        });
        var translator = new FakeTranslatorProvider("Google");
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
        var secondUpdate = await session.RefreshAsync();

        var originalCandidateId = $"{zone.Id}:candidate:{originalBounds.X}:{originalBounds.Y}:{originalBounds.Width}:{originalBounds.Height}";
        Assert.True(firstUpdate.OverlayChanged);
        Assert.False(secondUpdate.OverlayChanged);
        Assert.Equal(1, translator.CallCount);
        Assert.Contains(
            secondUpdate.CandidateLifecycleEvents,
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGeometryJitterMatched
                && entry.CandidateId == originalCandidateId
                && entry.CandidateBounds == jitteredBounds);
        Assert.DoesNotContain(
            secondUpdate.CandidateLifecycleEvents.Where(entry => entry.RefreshSequence == 2),
            entry => entry.Kind is LiveCandidateLifecycleEventKind.CandidateDiscovered
                or LiveCandidateLifecycleEventKind.CandidateRemoved);
        Assert.Equal(
            new[] { "Translated Stable candidate" },
            overlay.CurrentSnapshot!.TextItems.Select(item => item.Text));
    }

    [Fact]
    public async Task LiveSession_WhenCandidateBoundsMoveBeyondJitterBound_TreatsTheCandidateAsNew()
    {
        var zone = CreateZone("zone-dialog", "Dialog", new AbsoluteRectangle(10, 20, 100, 40));
        var profile = CreateProfile(zone);
        var originalBounds = new BoundingBox(5, 8, 90, 12);
        var movedBounds = new BoundingBox(10, 8, 90, 12);
        var detectorRequestCount = 0;
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (originalBounds, (byte)10)),
                CreateCandidatePilotPixels(zone, frameMarker: 2, (movedBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Moved candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detector = new FakeCandidateDetector(_ =>
        {
            var bounds = Interlocked.Increment(ref detectorRequestCount) == 1
                ? originalBounds
                : movedBounds;
            return TextCandidateDetectionResult.Available(
                "test-detector",
                new[] { new TextCandidate(bounds, 0.95) });
        });
        var service = CreateService(
            frameSource,
            ocrEngine,
            new FakeTranslatorProvider("Google"),
            new FakeOverlayService(),
            candidateDetector: detector);

        using var session = service.CreateLiveSession(
            profile,
            new TranslationPipelineRunOptions(enableCandidateDetectorPilot: true));
        await session.RefreshAsync();
        var secondUpdate = await session.RefreshAsync();

        Assert.Contains(
            secondUpdate.CandidateLifecycleEvents.Where(entry => entry.RefreshSequence == 2),
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateDiscovered
                && entry.CandidateBounds == movedBounds);
        Assert.Contains(
            secondUpdate.CandidateLifecycleEvents.Where(entry => entry.RefreshSequence == 2),
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateRemoved
                && entry.CandidateBounds == originalBounds);
        Assert.DoesNotContain(
            secondUpdate.CandidateLifecycleEvents.Where(entry => entry.RefreshSequence == 2),
            entry => entry.Kind == LiveCandidateLifecycleEventKind.CandidateGeometryJitterMatched);
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
        Assert.Equal(ContentLayoutMode.DialogComic, request.ContentLayoutMode);
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
        ITextCandidateDetector? candidateDetector = null,
        TextCandidateRegionOcrOptions? candidateRegionOcrOptions = null,
        TimeProvider? timeProvider = null)
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
                : new TextCandidateRegionOcrService(candidateDetector, ocrService, candidateRegionOcrOptions),
            timeProvider);
    }

    private static TranslationPipelineService CreateCandidateLiveService(
        OcrZone zone,
        BoundingBox candidateBounds,
        FakeTranslatorProvider translator,
        FakeOverlayService overlay)
    {
        var frameSource = new FakeCaptureFrameSource
        {
            PixelFrames = new[]
            {
                CreateCandidatePilotPixels(zone, frameMarker: 1, (candidateBounds, (byte)10)),
            },
        };
        var ocrEngine = new FakeOcrEngine
        {
            EngineId = OcrSettings.TesseractEngineId,
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Candidate", new BoundingBox(0, 0, 20, 10)),
            },
        };
        var detector = new FakeCandidateDetector(_ => TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { new TextCandidate(candidateBounds, 0.95) }));

        return CreateService(
            frameSource,
            ocrEngine,
            translator,
            overlay,
            candidateDetector: detector);
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

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            utcNow += duration;
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

        public Action? OnRecognize { get; init; }

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

            OnRecognize?.Invoke();

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
