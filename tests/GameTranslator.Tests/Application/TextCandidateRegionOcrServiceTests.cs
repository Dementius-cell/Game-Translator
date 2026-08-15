using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class TextCandidateRegionOcrServiceTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecognizeAsync_UsesTesseractForEachAcceptedTransientCandidate()
    {
        var detector = new FakeCandidateDetector(TextCandidateDetectionResult.Available(
            "test-detector",
            new[]
            {
                new TextCandidate(new BoundingBox(10, 15, 20, 25), 0.90),
                new TextCandidate(new BoundingBox(50, 35, 30, 20), 0.80),
            }));
        var engine = new FakeOcrEngine();
        var service = new TextCandidateRegionOcrService(detector, new OcrService(engine));

        var results = await CollectAsync(service.RecognizeAsync(CreateRequest()));

        Assert.Equal(2, results.Count);
        Assert.Equal(
            new[]
            {
                new BoundingBox(10, 15, 20, 25),
                new BoundingBox(50, 35, 30, 20),
            },
            results.Select(result => result.Candidate.Bounds));
        Assert.All(engine.Requests, request => Assert.Equal(OcrSettings.TesseractEngineId, request.EngineId));
        Assert.All(engine.Requests, request => Assert.Equal(OcrLayoutMode.Dialog, request.LayoutMode));
        Assert.Equal(
            new[]
            {
                new CaptureRegion(110, 215, 20, 25),
                new CaptureRegion(150, 235, 30, 20),
            },
            engine.Requests.Select(request => request.Frame.Region));
        Assert.Equal(
            new[] { "Text 10", "Text 50" },
            results.Select(result => result.RecognizedText));
        Assert.Equal(new BoundingBox(10, 15, 20, 25), results[0].CreateSourceGeometry().SemanticBounds);
    }

    [Fact]
    public async Task RecognizeAsync_WhenDetectorIsUnavailable_DoesNotFallbackToFullZoneOcr()
    {
        var detector = new FakeCandidateDetector(TextCandidateDetectionResult.Unavailable(
            "test-detector",
            "GPU runtime is unavailable."));
        var engine = new FakeOcrEngine();
        var service = new TextCandidateRegionOcrService(detector, new OcrService(engine));

        var results = await CollectAsync(service.RecognizeAsync(CreateRequest()));

        Assert.Empty(results);
        Assert.Empty(engine.Requests);
    }

    [Fact]
    public async Task RecognizeAsync_RejectsInvalidAndOverlappingCandidatesWithoutMergingThem()
    {
        var detector = new FakeCandidateDetector(TextCandidateDetectionResult.Available(
            "test-detector",
            new[]
            {
                new TextCandidate(new BoundingBox(95, 0, 10, 10), 0.99),
                new TextCandidate(new BoundingBox(10, 10, 20, 20), 0.80),
                new TextCandidate(new BoundingBox(15, 15, 20, 20), 0.90),
                new TextCandidate(new BoundingBox(60, 10, 20, 20), 0.49),
            }));
        var engine = new FakeOcrEngine();
        var service = new TextCandidateRegionOcrService(detector, new OcrService(engine));

        var results = await CollectAsync(service.RecognizeAsync(CreateRequest()));

        var result = Assert.Single(results);
        Assert.Equal(new BoundingBox(15, 15, 20, 20), result.Candidate.Bounds);
        Assert.Equal(new CaptureRegion(115, 215, 20, 20), Assert.Single(engine.Requests).Frame.Region);
    }

    [Fact]
    public async Task RecognizeAsync_YieldsReadyCandidateBeforeSlowCandidate()
    {
        var detector = new FakeCandidateDetector(TextCandidateDetectionResult.Available(
            "test-detector",
            new[]
            {
                new TextCandidate(new BoundingBox(10, 15, 20, 25), 0.90),
                new TextCandidate(new BoundingBox(50, 35, 30, 20), 0.80),
            }));
        var engine = new FakeOcrEngine(async request =>
        {
            if (request.Frame.Region.X == 110)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150));
            }

            return new OcrResult(
                request,
                new[] { new OcrTextBlock($"Text {request.Frame.Region.X}", new BoundingBox(0, 0, 10, 10)) },
                CapturedAt);
        });
        var service = new TextCandidateRegionOcrService(detector, new OcrService(engine));

        var results = await CollectAsync(service.RecognizeAsync(CreateRequest()));

        Assert.Equal(new[] { "Text 150", "Text 110" }, results.Select(result => result.RecognizedText));
    }

    [Fact]
    public async Task RecognizeAsync_WhenCjkTargetPostFilterIsEnabled_RejectsNonTargetGeometry()
    {
        var detector = new FakeCandidateDetector(TextCandidateDetectionResult.Available(
            "test-detector",
            new[]
            {
                new TextCandidate(new BoundingBox(10, 15, 20, 30), 0.90),
                new TextCandidate(new BoundingBox(50, 15, 40, 10), 0.90),
            }));
        var engine = new FakeOcrEngine(request => Task.FromResult(new OcrResult(
            request,
            new[] { new OcrTextBlock("\u65e5\u672c", new BoundingBox(0, 0, 10, 10)) },
            CapturedAt)));
        var service = new TextCandidateRegionOcrService(
            detector,
            new OcrService(engine),
            new TextCandidateRegionOcrOptions
            {
                EnableCjkTargetPostFilter = true,
            });

        var results = await CollectAsync(service.RecognizeAsync(CreateRequest()));

        var result = Assert.Single(results);
        Assert.Equal(new BoundingBox(10, 15, 20, 30), result.Candidate.Bounds);
    }

    [Fact]
    public async Task DetectAsync_GroupsTouchingVerticalFragmentsBeforeSuppressingDuplicates()
    {
        var detector = new FakeCandidateDetector(TextCandidateDetectionResult.Available(
            "test-detector",
            new[]
            {
                new TextCandidate(new BoundingBox(186, 82, 33, 144), 0.90),
                new TextCandidate(new BoundingBox(215, 80, 32, 79), 0.85),
            }));
        var service = new TextCandidateRegionOcrService(detector, new OcrService(new FakeOcrEngine()));

        var result = await service.DetectAsync(CreateRequest(width: 300, height: 300));

        var region = Assert.Single(result.Regions);
        Assert.Equal(new BoundingBox(186, 80, 61, 146), region.Candidate.Bounds);
    }

    [Fact]
    public async Task DetectAsync_GroupsBoundedHorizontalTextLinesIntoOneCandidateCrop()
    {
        var detector = new FakeCandidateDetector(TextCandidateDetectionResult.Available(
            "test-detector",
            new[]
            {
                new TextCandidate(new BoundingBox(612, 670, 87, 16), 0.90),
                new TextCandidate(new BoundingBox(616, 691, 83, 16), 0.90),
                new TextCandidate(new BoundingBox(630, 710, 56, 19), 0.90),
                new TextCandidate(new BoundingBox(612, 731, 90, 20), 0.90),
                new TextCandidate(new BoundingBox(625, 754, 65, 16), 0.90),
                new TextCandidate(new BoundingBox(117, 680, 68, 20), 0.90),
            }));
        var service = new TextCandidateRegionOcrService(detector, new OcrService(new FakeOcrEngine()));

        var result = await service.DetectAsync(CreateRequest(width: 900, height: 900));

        Assert.Equal(
            new[]
            {
                new BoundingBox(612, 670, 90, 100),
                new BoundingBox(117, 680, 68, 20),
            },
            result.Regions.Select(region => region.Candidate.Bounds));
        Assert.Equal(
            new CaptureRegion(712, 870, 90, 100),
            result.Regions[0].Frame.Region);
    }

    private static OcrRequest CreateRequest(int width = 100, int height = 80)
    {
        var stride = width * 4;
        return new OcrRequest(
            new CapturedFrame(
                new CaptureRegion(100, 200, width, height),
                width,
                height,
                stride,
                "Bgra32",
                new byte[stride * height],
                CapturedAt),
            "ja",
            "manual-zone",
            engineId: OcrSettings.WindowsEngineId,
            orientationMode: OcrOrientationMode.Vertical,
            layoutMode: OcrLayoutMode.Comic);
    }

    private static async Task<List<TextCandidateRegionOcrResult>> CollectAsync(
        IAsyncEnumerable<TextCandidateRegionOcrResult> results)
    {
        var collected = new List<TextCandidateRegionOcrResult>();
        await foreach (var result in results)
        {
            collected.Add(result);
        }

        return collected;
    }

    private sealed class FakeCandidateDetector : ITextCandidateDetector
    {
        private readonly TextCandidateDetectionResult result;

        public FakeCandidateDetector(TextCandidateDetectionResult result)
        {
            this.result = result;
        }

        public Task<TextCandidateDetectionResult> DetectAsync(
            TextCandidateDetectionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class FakeOcrEngine : IOcrEngine
    {
        private readonly Func<OcrRequest, Task<OcrResult>>? recognizeAsync;

        public FakeOcrEngine(Func<OcrRequest, Task<OcrResult>>? recognizeAsync = null)
        {
            this.recognizeAsync = recognizeAsync;
        }

        public string EngineId => OcrSettings.TesseractEngineId;

        public List<OcrRequest> Requests { get; } = new();

        public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (recognizeAsync is not null)
            {
                return recognizeAsync(request);
            }

            return Task.FromResult(new OcrResult(
                request,
                new[] { new OcrTextBlock($"Text {request.Frame.Region.X - 100}", new BoundingBox(0, 0, 10, 10)) },
                CapturedAt));
        }
    }
}
