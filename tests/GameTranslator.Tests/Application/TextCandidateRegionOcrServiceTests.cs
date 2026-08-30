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
    public async Task DetectAsync_PassesTheZoneDetectorPresetToTheTransientDetectorRequest()
    {
        var detector = new FakeCandidateDetector(TextCandidateDetectionResult.Available(
            "test-detector",
            Array.Empty<TextCandidate>()));
        var service = new TextCandidateRegionOcrService(detector, new OcrService(new FakeOcrEngine()));
        var request = CreateRequest(
            language: "chi_sim",
            detectorPreset: TextCandidateDetectorPreset.ChineseExperimental);

        await service.DetectAsync(request);

        Assert.Equal(
            TextCandidateDetectorPreset.ChineseExperimental,
            Assert.Single(detector.Requests).DetectorPreset);
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

    [Theory]
    [InlineData("ja", "\u65e5\u672c\u8a9e")]
    [InlineData("zh-CN", "\u4e2d\u6587\u6d4b\u8bd5")]
    public async Task RecognizeAsync_WhenCjkTargetPostFilterIsEnabled_AppliesVerticalGeometryOnly(
        string language,
        string recognizedText)
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
            new[] { new OcrTextBlock(recognizedText, new BoundingBox(0, 0, 10, 10)) },
            CapturedAt)));
        var service = new TextCandidateRegionOcrService(
            detector,
            new OcrService(engine),
            new TextCandidateRegionOcrOptions
            {
                EnableCjkTargetPostFilter = true,
            });

        var results = await CollectAsync(service.RecognizeAsync(
            CreateRequest(language: language, orientationMode: OcrOrientationMode.Vertical)));

        var result = Assert.Single(results);
        Assert.Equal(new BoundingBox(10, 15, 20, 30), result.Candidate.Bounds);
    }

    [Theory]
    [MemberData(nameof(CompactVerticalChineseBubbleCases))]
    public async Task RecognizeAsync_WhenCjkTargetPostFilterIsEnabled_AcceptsCompactMultiColumnChineseBubble(
        BoundingBox[] sourceColumns,
        BoundingBox expectedBounds)
    {
        var detector = new FakeCandidateDetector(TextCandidateDetectionResult.Available(
            "test-detector",
            sourceColumns.Select(bounds => new TextCandidate(bounds, 0.90))));
        var engine = new FakeOcrEngine(request => Task.FromResult(new OcrResult(
            request,
            new[] { new OcrTextBlock("\u6885\u4e3d\u628a\u6025\u6551\u7bb1\u62ff\u6765", new BoundingBox(0, 0, 20, 20)) },
            CapturedAt)));
        var service = new TextCandidateRegionOcrService(
            detector,
            new OcrService(engine),
            new TextCandidateRegionOcrOptions
            {
                EnableCjkTargetPostFilter = true,
            });

        var results = await CollectAsync(service.RecognizeAsync(
            CreateRequest(
                width: 1200,
                height: 600,
                language: "zh-CN",
                orientationMode: OcrOrientationMode.Vertical)));

        var result = Assert.Single(results);
        Assert.Equal(expectedBounds, result.Candidate.Bounds);
        Assert.Equal(sourceColumns.Length, result.Candidate.SourceCandidateCount);
    }

    [Fact]
    public async Task RecognizeAsync_WhenCjkTargetPostFilterIsEnabled_KeepsAdjacentCompactChineseBubblesSeparate()
    {
        var detector = new FakeCandidateDetector(TextCandidateDetectionResult.Available(
            "test-detector",
            new[]
            {
                new TextCandidate(new BoundingBox(300, 100, 30, 80), 0.90),
                new TextCandidate(new BoundingBox(268, 100, 30, 80), 0.90),
                new TextCandidate(new BoundingBox(236, 100, 30, 80), 0.90),
                new TextCandidate(new BoundingBox(196, 100, 30, 80), 0.90),
                new TextCandidate(new BoundingBox(164, 100, 30, 80), 0.90),
                new TextCandidate(new BoundingBox(132, 100, 30, 80), 0.90),
            }));
        var engine = new FakeOcrEngine(request => Task.FromResult(new OcrResult(
            request,
            new[] { new OcrTextBlock("\u4e2d\u6587\u6d4b\u8bd5", new BoundingBox(0, 0, 20, 20)) },
            CapturedAt)));
        var service = new TextCandidateRegionOcrService(
            detector,
            new OcrService(engine),
            new TextCandidateRegionOcrOptions
            {
                EnableCjkTargetPostFilter = true,
            });

        var results = await CollectAsync(service.RecognizeAsync(
            CreateRequest(
                width: 400,
                height: 300,
                language: "zh-CN",
                orientationMode: OcrOrientationMode.Vertical)));

        Assert.Equal(
            new[]
            {
                new BoundingBox(132, 100, 94, 80),
                new BoundingBox(236, 100, 94, 80),
            },
            results.Select(result => result.Candidate.Bounds));
        Assert.All(results, result => Assert.Equal(3, result.Candidate.SourceCandidateCount));
    }

    [Fact]
    public async Task RecognizeAsync_WhenWideCjkGroupContainsHorizontalMember_RejectsCandidate()
    {
        var candidate = new TextCandidate(new BoundingBox(10, 10, 80, 40), 0.90)
        {
            SourceCandidateBounds = new[]
            {
                new BoundingBox(10, 10, 20, 40),
                new BoundingBox(30, 10, 60, 20),
            },
        };
        var detector = new FakeCandidateDetector(TextCandidateDetectionResult.Available(
            "test-detector",
            new[] { candidate }));
        var engine = new FakeOcrEngine(request => Task.FromResult(new OcrResult(
            request,
            new[] { new OcrTextBlock("\u4e2d\u6587\u6d4b\u8bd5", new BoundingBox(0, 0, 20, 20)) },
            CapturedAt)));
        var service = new TextCandidateRegionOcrService(
            detector,
            new OcrService(engine),
            new TextCandidateRegionOcrOptions
            {
                EnableCjkTargetPostFilter = true,
            });

        var results = await CollectAsync(service.RecognizeAsync(
            CreateRequest(language: "zh-CN", orientationMode: OcrOrientationMode.Vertical)));

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("ja", "\u65e5\u672c\u8a9e")]
    [InlineData("zh-CN", "\u4e2d\u6587\u6d4b\u8bd5")]
    public async Task RecognizeAsync_WhenCjkTargetPostFilterIsEnabled_DoesNotApplyVerticalGeometryToHorizontalCjk(
        string language,
        string recognizedText)
    {
        var detector = new FakeCandidateDetector(TextCandidateDetectionResult.Available(
            "test-detector",
            new[]
            {
                new TextCandidate(new BoundingBox(10, 15, 60, 12), 0.90),
            }));
        var engine = new FakeOcrEngine(request => Task.FromResult(new OcrResult(
            request,
            new[] { new OcrTextBlock(recognizedText, new BoundingBox(0, 0, 10, 10)) },
            CapturedAt)));
        var service = new TextCandidateRegionOcrService(
            detector,
            new OcrService(engine),
            new TextCandidateRegionOcrOptions
            {
                EnableCjkTargetPostFilter = true,
            });

        var results = await CollectAsync(service.RecognizeAsync(
            CreateRequest(language: language, orientationMode: OcrOrientationMode.Horizontal)));

        var result = Assert.Single(results);
        Assert.Equal(new BoundingBox(10, 15, 60, 12), result.Candidate.Bounds);
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

    [Fact]
    public async Task DetectAsync_UsesComplexSouthEastAsianProfileForThaiCandidates()
    {
        var detector = new FakeCandidateDetector(TextCandidateDetectionResult.Available(
            "test-detector",
            Enumerable.Range(0, 8)
                .Select(index => new TextCandidate(
                    new BoundingBox(100 + (index % 2) * 4, 100 + index * 22, 50, 16),
                    0.90))));
        var service = new TextCandidateRegionOcrService(detector, new OcrService(new FakeOcrEngine()));

        var result = await service.DetectAsync(
            CreateRequest(width: 400, height: 400, language: "th", orientationMode: OcrOrientationMode.Horizontal));

        var region = Assert.Single(result.Regions);
        Assert.Equal(new BoundingBox(100, 100, 54, 170), region.Candidate.Bounds);
        Assert.Equal(8, region.Candidate.SourceCandidateCount);
        Assert.Equal(
            region.Candidate.SourceCandidateBounds,
            new TextCandidateRegionOcrResult(
                region.Candidate,
                "Text",
                CapturedAt,
                OcrOrientationMode.Horizontal)
                .CreateSourceGeometry()
                .MemberBounds);
    }

    private static OcrRequest CreateRequest(
        int width = 100,
        int height = 80,
        string language = "ja",
        OcrOrientationMode orientationMode = OcrOrientationMode.Vertical,
        TextCandidateDetectorPreset detectorPreset = TextCandidateDetectorPreset.Standard)
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
            language,
            "manual-zone",
            engineId: OcrSettings.WindowsEngineId,
            orientationMode: orientationMode,
            layoutMode: OcrLayoutMode.Comic)
        {
            DetectorPreset = detectorPreset,
        };
    }

    public static TheoryData<BoundingBox[], BoundingBox> CompactVerticalChineseBubbleCases => new()
    {
        {
            new[]
            {
                new BoundingBox(1070, 248, 37, 84),
                new BoundingBox(1040, 251, 33, 80),
                new BoundingBox(1004, 247, 41, 86),
            },
            new BoundingBox(1004, 247, 103, 86)
        },
        {
            new[]
            {
                new BoundingBox(441, 148, 29, 95),
                new BoundingBox(413, 147, 29, 96),
                new BoundingBox(388, 147, 26, 76),
                new BoundingBox(358, 147, 28, 95),
            },
            new BoundingBox(358, 147, 112, 96)
        },
    };

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

        public List<TextCandidateDetectionRequest> Requests { get; } = new();

        public Task<TextCandidateDetectionResult> DetectAsync(
            TextCandidateDetectionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
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
