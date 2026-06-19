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

        Assert.Equal(TranslationPipelineStage.Translation, exception.Stage);
        Assert.Contains("Translation", exception.Message, StringComparison.Ordinal);
    }

    private static TranslationPipelineService CreateService(
        FakeCaptureFrameSource frameSource,
        FakeOcrEngine ocrEngine,
        FakeTranslatorProvider translator,
        FakeOverlayService overlay,
        FakeCredentialStorage? credentialStorage = null)
    {
        return new TranslationPipelineService(
            new CaptureService(frameSource),
            new OcrService(ocrEngine),
            new TranslatorManager(new ITranslatorProvider[] { translator }),
            new TranslatorCredentialService(credentialStorage ?? FakeCredentialStorage.WithGoogleCredentials()),
            new OverlayPositioningService(),
            overlay);
    }

    private static GameProfile CreateProfile(OcrZone zone)
    {
        return new GameProfile
        {
            Id = "profile-a",
            Name = "Pipeline profile",
            OcrZones = new[] { zone },
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
        return new OcrZone
        {
            Id = "zone-a",
            Name = "Subtitles",
            AbsoluteBounds = new AbsoluteRectangle(10, 20, 100, 40),
            RelativeBounds = new RelativeRectangle(0.1, 0.2, 0.3, 0.1),
        };
    }

    private sealed class FakeCaptureFrameSource : ICaptureFrameSource
    {
        public List<CaptureRegion> CapturedRegions { get; } = new();

        public Exception? Failure { get; init; }

        public Task<CapturedFrame> CaptureAsync(CaptureRegion region, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                return Task.FromException<CapturedFrame>(Failure);
            }

            CapturedRegions.Add(region);
            var stride = checked(region.Width * 4);
            var pixels = Enumerable.Repeat((byte)42, checked(stride * region.Height)).ToArray();

            return Task.FromResult(
                new CapturedFrame(
                    region,
                    region.Width,
                    region.Height,
                    stride,
                    "Bgra32",
                    pixels,
                    FrameTime));
        }
    }

    private sealed class FakeOcrEngine : IOcrEngine
    {
        public List<OcrRequest> Requests { get; } = new();

        public Func<OcrRequest, IReadOnlyList<OcrTextBlock>>? BlocksFactory { get; init; }

        public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);

            return Task.FromResult(
                new OcrResult(
                    request,
                    BlocksFactory?.Invoke(request) ?? Array.Empty<OcrTextBlock>(),
                    OcrTime));
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

        public TranslateRequest? Request { get; private set; }

        public Task<TranslateResponse> TranslateAsync(
            TranslateRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

        public OverlaySnapshot? CurrentSnapshot { get; private set; }

        public void Show(OverlaySnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            IsVisible = true;
        }

        public void Hide()
        {
            IsVisible = false;
        }
    }
}
