using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class TextCandidateRegionOcrServiceTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CandidateCrop_PreservesSourceFrameOriginAndCachedReuse()
    {
        var origin = new CaptureFrameOrigin(Guid.NewGuid(), TimeSpan.FromSeconds(42), CapturedAt);
        var frame = new CapturedFrame(new CaptureRegion(0, 0, 200, 100), 200, 100, 800, "Bgra32",
            new byte[80000], CapturedAt, origin, isCachedReuse: true);
        var service = new TextCandidateRegionOcrService(
            new FakeCandidateDetector(TextCandidateDetectionResult.Available("test",
                new[] { new TextCandidate(new BoundingBox(10, 10, 120, 24), .95) })),
            new OcrService(new FakeOcrEngine()));
        var result = await service.DetectAsync(new OcrRequest(frame, "eng", "origin",
            contentLayoutMode: ContentLayoutMode.StaticMenu));
        var crop = Assert.Single(result.Regions).Frame;
        Assert.Equal(120, crop.Width);
        Assert.Same(origin, crop.Origin);
        Assert.True(crop.IsCachedReuse);
        Assert.Equal(CapturedAt, crop.CapturedAt);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 17)]
    [InlineData(3, 45)]
    public async Task DetectAsync_MenuRetainsAdjacentPaddedBoxesAcrossScale(int scale, int offset)
    {
        var boxes = new[] {
            new BoundingBox(offset + 10 * scale, 10 * scale, 200 * scale, 30 * scale),
            new BoundingBox(offset + 200 * scale, 12 * scale, 120 * scale, 28 * scale) };
        var service = new TextCandidateRegionOcrService(
            new FakeCandidateDetector(TextCandidateDetectionResult.Available("test", boxes.Select(b => new TextCandidate(b, .9)))),
            new OcrService(new FakeOcrEngine()));
        var result = await service.DetectAsync(CreateRequest(400 * scale, 100 * scale, "eng", OcrOrientationMode.Horizontal,
            contentLayoutMode: ContentLayoutMode.StaticMenu));
        Assert.Equal(boxes, result.Regions.Select(r => r.Candidate.Bounds));
    }

    [Theory]
    [InlineData(170, 12)]
    [InlineData(200, 30)]
    [InlineData(20, 12)]
    public async Task DetectAsync_MenuStillRejectsSubstantialNestedOrStackedOverlap(int x, int y)
    {
        var boxes = new[] { new BoundingBox(10, 10, 200, 30), new BoundingBox(x, y, 120, 28) };
        var service = new TextCandidateRegionOcrService(
            new FakeCandidateDetector(TextCandidateDetectionResult.Available("test", boxes.Select(b => new TextCandidate(b, .9)))),
            new OcrService(new FakeOcrEngine()));
        var result = await service.DetectAsync(CreateRequest(400, 100, "eng", OcrOrientationMode.Horizontal,
            contentLayoutMode: ContentLayoutMode.StaticMenu));
        Assert.Single(result.Regions);
    }

    [Fact]
    public async Task DetectAsync_MenuAutoGroupsDescriptionButExplicitOneLineKeepsItSeparate()
    {
        var candidates = new[]
        {
            new TextCandidate(new BoundingBox(10, 10, 400, 20), .9),
            new TextCandidate(new BoundingBox(10, 40, 300, 20), .9),
            new TextCandidate(new BoundingBox(450, 10, 70, 20), .9),
        };
        var service = new TextCandidateRegionOcrService(
            new FakeCandidateDetector(TextCandidateDetectionResult.Available("test", candidates)),
            new OcrService(new FakeOcrEngine()));
        var automatic = await service.DetectAsync(CreateRequest(600, 100, "eng", OcrOrientationMode.Horizontal,
            contentLayoutMode: ContentLayoutMode.StaticMenu));
        var explicitLimit = await service.DetectAsync(CreateRequest(600, 100, "eng", OcrOrientationMode.Horizontal,
            contentLayoutMode: ContentLayoutMode.StaticMenu, grouping: new OcrCandidateGroupingSettings { MaximumHorizontalLines = 1 }));
        Assert.Equal(2, automatic.Regions.Count);
        Assert.Equal(3, explicitLimit.Regions.Count);
        Assert.Equal(2, automatic.Regions[0].Candidate.SourceCandidateCount);
    }

    [Theory]
    [InlineData("en", OcrOrientationMode.Horizontal)]
    [InlineData("ja", OcrOrientationMode.Auto)]
    [InlineData("ko", OcrOrientationMode.Horizontal)]
    public async Task DetectAsync_StaticMenu_KeepsTightlyStackedButtonsAndColumnsSeparate(
        string language, OcrOrientationMode orientation)
    {
        var candidates = new[]
        {
            new TextCandidate(new BoundingBox(10, 10, 80, 20), 0.9),
            new TextCandidate(new BoundingBox(10, 32, 80, 20), 0.9),
            new TextCandidate(new BoundingBox(100, 10, 70, 20), 0.9),
        };
        var service = new TextCandidateRegionOcrService(
            new FakeCandidateDetector(TextCandidateDetectionResult.Available("test", candidates)),
            new OcrService(new FakeOcrEngine()));
        var menu = await service.DetectAsync(CreateRequest(200, 100, language, orientation,
            contentLayoutMode: ContentLayoutMode.StaticMenu));
        var dialogue = await service.DetectAsync(CreateRequest(200, 100, language, orientation));

        Assert.Equal(3, menu.Regions.Count);
        Assert.All(menu.Regions, region => Assert.Equal(1, region.Candidate.SourceCandidateCount));
        Assert.Equal(2, dialogue.Regions.Count);
    }

    [Fact]
    public async Task DetectAsync_StaticMenu_ExplicitLineLimitGroupsDescriptionOnlyWithinLimit()
    {
        var candidates = Enumerable.Range(0, 3)
            .Select(i => new TextCandidate(new BoundingBox(10, 10 + i * 22, 80, 20), 0.9));
        var service = new TextCandidateRegionOcrService(
            new FakeCandidateDetector(TextCandidateDetectionResult.Available("test", candidates)),
            new OcrService(new FakeOcrEngine()));
        var request = CreateRequest(200, 100, "en", OcrOrientationMode.Horizontal,
            contentLayoutMode: ContentLayoutMode.StaticMenu,
            grouping: new OcrCandidateGroupingSettings { MaximumHorizontalLines = 2 });

        var result = await service.DetectAsync(request);

        Assert.Equal(new[] { 2, 1 }, result.Regions.Select(r => r.Candidate.SourceCandidateCount));
    }

    [Fact]
    public async Task RecognizeAsync_StaticMenu_PreservesShortVerticalCjkLabelsAndRequestMode()
    {
        var engine = new FakeOcrEngine(request => Task.FromResult(new OcrResult(request,
            new[] { new OcrTextBlock("戻る", new BoundingBox(0, 0, 10, 20)) }, CapturedAt)));
        var service = new TextCandidateRegionOcrService(
            new FakeCandidateDetector(TextCandidateDetectionResult.Available("test", new[]
            {
                new TextCandidate(new BoundingBox(10, 10, 12, 24), 0.9),
                new TextCandidate(new BoundingBox(24, 10, 12, 24), 0.9),
            })), new OcrService(engine),
            new TextCandidateRegionOcrOptions { EnableCjkTargetPostFilter = true });

        var results = await CollectAsync(service.RecognizeAsync(CreateRequest(200, 500,
            contentLayoutMode: ContentLayoutMode.StaticMenu)));

        Assert.Equal(2, results.Count);
        Assert.All(engine.Requests, request => Assert.Equal(ContentLayoutMode.StaticMenu, request.ContentLayoutMode));
        Assert.All(results, result => Assert.Equal("戻る", result.RecognizedText));
    }

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
        TextCandidateDetectorPreset detectorPreset = TextCandidateDetectorPreset.Standard,
        ContentLayoutMode contentLayoutMode = ContentLayoutMode.DialogComic,
        OcrCandidateGroupingSettings? grouping = null)
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
            layoutMode: OcrLayoutMode.Comic,
            contentLayoutMode: contentLayoutMode,
            candidateGroupingSettings: grouping)
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
