using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class MenuRecognitionContextTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task ClippedLongSingleLine_UsesSourceContextAndPreservesCoordinates(int scale)
    {
        var (request, candidate) = SingleFixture(scale);
        var region = TextCandidateRegionOcrService.CreateRegion(request, candidate, [candidate]);
        var context = Assert.IsType<CapturedFrame>(region.RecognitionContextFrame);
        var pad = (int)Math.Ceiling(candidate.Bounds.Height * .15);
        Assert.Equal(candidate.Bounds.Width + pad * 2, context.Width);
        Assert.Equal(candidate.Bounds.Height + pad * 2, context.Height);
        Assert.Equal(candidate.Bounds, region.Candidate.Bounds);
        Assert.Equal(candidate.Bounds, Assert.Single(region.Candidate.SourceCandidateBounds));
        Assert.Same(request.Frame.Origin, context.Origin);
        Assert.Equal(request.Frame.IsCachedReuse, context.IsCachedReuse);
        var engine = new RecordingEngine();
        var result = await new OcrService(engine).RecognizeAsync(region.CreateOcrRequest(request));
        Assert.Single(engine.Requests);
        Assert.Equal(new BoundingBox(pad, pad, candidate.Bounds.Width, candidate.Bounds.Height),
            Assert.Single(engine.Requests[0].DetectorLineBounds));
        Assert.Equal(region.Frame.Region, result.Region);
        Assert.Equal(region.Frame.Width, result.InputWidth);
        Assert.Equal("pass", Assert.Single(result.Words).RecognitionPassId);
        Assert.All(result.TextBlocks, b => Assert.True(b.Bounds.IsWithin(result.InputWidth, result.InputHeight)));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("inside-only")]
    [InlineData("outside-only")]
    [InlineData("texture")]
    [InlineData("bright")]
    [InlineData("neighbor")]
    [InlineData("outer-strip")]
    public void SingleLine_RequiresCrossingInkQuietBorderAndFreeContext(string condition)
    {
        var (request, candidate) = SingleFixture(1, condition);
        var neighbor = new TextCandidate(new(candidate.Bounds.X, candidate.Bounds.Bottom + 1, 40, 20), .9);
        Assert.Null(MenuRecognitionContext.Create(request, candidate,
            condition == "neighbor" ? [candidate, neighbor] : [candidate], default));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task SingleLine_SelectsSmallerCleanMarginBeforeOneRecognitionPass(int scale)
    {
        var (request, candidate) = SingleFixture(scale, "outer-edge-ink");
        var region = TextCandidateRegionOcrService.CreateRegion(request, candidate, [candidate]);
        var context = Assert.IsType<CapturedFrame>(region.RecognitionContextFrame);
        var pad = (int)Math.Ceiling(candidate.Bounds.Height * .15) - 1;
        Assert.Equal(candidate.Bounds.Width + 2 * pad, context.Width);
        Assert.Equal(candidate.Bounds.Height + 2 * pad, context.Height);
        Assert.Equal(candidate.Bounds, region.Candidate.Bounds);
        Assert.Same(request.Frame.Origin, context.Origin);
        var engine = new RecordingEngine();
        var result = await new OcrService(engine).RecognizeAsync(region.CreateOcrRequest(request));
        Assert.Single(engine.Requests);
        Assert.Equal(context.Width, engine.Requests[0].Frame.Width);
        Assert.Equal(region.Frame.Region, result.Region);
        Assert.All(result.TextBlocks, b => Assert.True(b.Bounds.IsWithin(result.InputWidth, result.InputHeight)));
    }

    private static (OcrRequest, TextCandidate) SingleFixture(int scale, string condition = "")
    {
        int width = 420 * scale, height = 80 * scale;
        var bounds = new BoundingBox(20 * scale, 20 * scale, (condition == "short" ? 180 : 360) * scale, 24 * scale);
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            byte value = condition == "bright" ? (byte)220 : condition == "texture" ? (byte)(5 + (y / scale) % 2 * 25) : (byte)5;
            if (x >= 60 * scale && x < 65 * scale && y >= bounds.Bottom - 2 * scale && y < bounds.Bottom + 2 * scale
                && (condition != "inside-only" || y < bounds.Bottom)
                && (condition != "outside-only" || y >= bounds.Bottom)) value = 110;
            if (condition == "outer-edge-ink" && x == bounds.X - (int)Math.Ceiling(bounds.Height * .15)
                && y == bounds.Y + 5 * scale) value = 110;
            if (condition == "outer-strip" && x >= bounds.X - (int)Math.Ceiling(bounds.Height * .15)
                && x < bounds.X && y == bounds.Y + 5 * scale) value = 110;
            var offset = (y * width + x) * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
        var time = DateTimeOffset.UnixEpoch;
        var frame = new CapturedFrame(new(1000, 2000, width, height), width, height, width * 4, "Bgra32", pixels, time,
            new CaptureFrameOrigin(Guid.NewGuid(), TimeSpan.FromSeconds(42), time), true);
        return (new OcrRequest(frame, "eng", engineId: "Tesseract", orientationMode: OcrOrientationMode.Horizontal,
            contentLayoutMode: ContentLayoutMode.StaticMenu), new TextCandidate(bounds, .9));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task DarkDescription_RecognizesOriginalNeighborPixelsWithoutChangingDetectorBounds(int scale)
    {
        var (request, candidate) = Fixture(scale);
        var engine = new RecordingEngine();
        var service = new TextCandidateRegionOcrService(new Detector(candidate), new OcrService(engine));
        var region = Assert.Single((await service.DetectAsync(request)).Regions);
        Assert.Equal(candidate.Bounds, region.Candidate.Bounds);
        Assert.Equal(candidate.Bounds.Width, region.Frame.Width);
        var context = Assert.IsType<CapturedFrame>(region.RecognitionContextFrame);
        var pad = (int)Math.Ceiling(24 * scale * .15);
        Assert.Equal(candidate.Bounds.Width + pad * 2, context.Width);
        Assert.Equal(candidate.Bounds.Height + pad * 2, context.Height);
        Assert.Same(request.Frame.Origin, context.Origin);
        Assert.True(context.IsCachedReuse);
        Assert.Equal(request.Frame.CapturedAt, context.CapturedAt);
        var offset = (pad + 8 * scale) * context.Stride + (pad + candidate.Bounds.Width) * 4;
        Assert.Equal(110, context.PixelData.Span[offset]);
        var result = await new OcrService(engine).RecognizeAsync(region.CreateOcrRequest(request));
        Assert.Equal(context.Width, Assert.Single(engine.Requests).Frame.Width);
        Assert.Equal(new BoundingBox(pad, pad, 180 * scale, 24 * scale), engine.Requests[0].DetectorLineBounds[0]);
        Assert.Equal(region.Frame.Region, result.Region);
        Assert.Equal(region.Frame.Width, result.InputWidth);
        Assert.Equal("Option 7", Assert.Single(result.TextBlocks).Text);
        Assert.Equal(0, result.TextBlocks[0].Bounds.X);
        Assert.Equal(region.Frame.Width, result.TextBlocks[0].Bounds.Width);
        Assert.Equal("pass", Assert.Single(result.Words).RecognitionPassId);
        Assert.Equal(.9, result.Words[0].Confidence);
        Assert.Equal(OcrOrientationMode.Horizontal, result.TextBlockSources[0].OrientationMode);
        Assert.All(result.TextBlockSources[0].MemberBounds, b => Assert.True(b.IsWithin(result.InputWidth, result.InputHeight)));
    }

    [Theory]
    [InlineData("comic")]
    [InlineData("vertical")]
    [InlineData("auto")]
    [InlineData("manual")]
    [InlineData("adaptive")]
    [InlineData("single")]
    [InlineData("texture")]
    [InlineData("bright")]
    [InlineData("no-edge-ink")]
    [InlineData("neighbor")]
    [InlineData("overlapping-lines")]
    public void UnsupportedOrAmbiguousContext_KeepsOriginalCrop(string condition)
    {
        var (request, candidate) = Fixture(1, condition);
        var neighbor = new TextCandidate(new BoundingBox(candidate.Bounds.Right + 2, 20, 20, 24), .9);
        var context = MenuRecognitionContext.Create(request, candidate,
            condition == "neighbor" ? [candidate, neighbor] : [candidate], default);
        Assert.Null(context);
    }

    [Fact]
    public async Task ZoneEdge_ClipsContextAndReturnsOriginalCoordinates()
    {
        var (request, candidate) = Fixture(1, atEdge: true);
        var region = TextCandidateRegionOcrService.CreateRegion(request, candidate, [candidate]);
        var context = Assert.IsType<CapturedFrame>(region.RecognitionContextFrame);
        Assert.Equal(request.Frame.Region.X, context.Region.X);
        Assert.Equal(request.Frame.Region.Y, context.Region.Y);
        Assert.Equal(184, context.Width);
        var engine = new RecordingEngine();
        var result = await new OcrService(engine).RecognizeAsync(region.CreateOcrRequest(request));
        Assert.Equal(0, engine.Requests[0].DetectorLineBounds[0].X);
        Assert.Equal(180, result.InputWidth);
        Assert.Equal(region.Frame.Region, result.Region);
    }

    [Fact]
    public void ContextAnalysis_HonorsCancellation()
    {
        var (request, candidate) = Fixture(1);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => MenuRecognitionContext.Create(request, candidate, [candidate], cancellation.Token));
    }

    private static (OcrRequest, TextCandidate) Fixture(int scale, string condition = "", bool atEdge = false)
    {
        int width = 240 * scale, height = 120 * scale, x = atEdge ? 0 : 20 * scale, y = atEdge ? 0 : 20 * scale;
        var bounds = new BoundingBox(x, y, 180 * scale, 56 * scale);
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        for (var col = 0; col < width; col++)
        {
            byte value = condition == "bright" ? (byte)220 : condition == "texture" ? (byte)(5 + row % 2 * 25) : (byte)5;
            if (condition != "no-edge-ink" && col >= bounds.Right - scale && col < bounds.Right + 2 * scale
                && row >= y + 5 * scale && row < y + 20 * scale) value = 110;
            var offset = (row * width + col) * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
        var time = DateTimeOffset.UtcNow;
        var frame = new CapturedFrame(new(1000, 2000, width, height), width, height, width * 4, "Bgra32", pixels, time,
            new CaptureFrameOrigin(Guid.NewGuid(), TimeSpan.FromSeconds(42), time), true);
        var candidate = new TextCandidate(bounds, .9) { SourceCandidateBounds = condition == "single" ? [bounds] :
            [new(x,y,180*scale,24*scale), new(x, y+(condition == "overlapping-lines" ? 20 : 32)*scale,100*scale,24*scale)] };
        var prep = condition switch {
            "manual" => new OcrPreprocessingSettings { Mode=OcrPreprocessingMode.Manual },
            "adaptive" => new OcrPreprocessingSettings { Mode=OcrPreprocessingMode.Adaptive },
            _ => OcrPreprocessingSettings.Default };
        return (new OcrRequest(frame,"eng",preprocessingSettings:prep,engineId:"Tesseract",
            orientationMode:condition == "vertical" ? OcrOrientationMode.Vertical : condition == "auto" ? OcrOrientationMode.Auto : OcrOrientationMode.Horizontal,
            contentLayoutMode:condition == "comic" ? ContentLayoutMode.DialogComic : ContentLayoutMode.StaticMenu), candidate);
    }

    private sealed class Detector(TextCandidate candidate) : ITextCandidateDetector
    {
        public Task<TextCandidateDetectionResult> DetectAsync(TextCandidateDetectionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(TextCandidateDetectionResult.Available("test", [candidate]));
    }

    private sealed class RecordingEngine : IOcrEngine
    {
        public string EngineId => "Tesseract";
        public List<OcrRequest> Requests { get; } = [];
        public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var bounds = new BoundingBox(0, 0, request.Frame.Width, request.Frame.Height);
            return Task.FromResult(new OcrResult(request, [new OcrTextBlock("Option 7", bounds)], DateTimeOffset.UtcNow,
                [new OcrTextBlockSource(bounds, [bounds], OcrOrientationMode.Horizontal)],
                [new OcrWord("7", bounds, .9, "pass")]));
        }
    }
}
