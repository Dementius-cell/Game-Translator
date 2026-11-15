using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class IncrementalRegionDetectorTests
{
    private static OcrRequest Request()
    {
        var frame = new CapturedFrame(new CaptureRegion(100, 200, 1024, 512), 1024, 512, 4096, "Bgra32",
            new byte[1024 * 512 * 4], DateTimeOffset.UnixEpoch,
            new CaptureFrameOrigin(Guid.NewGuid(), TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch));
        return new(frame, "en", "test", contentLayoutMode: ContentLayoutMode.StaticMenu);
    }
    private static RegionDetectionDiagnostics Scope(BoundingBox area) => new("local-change", false, 1, 128, [area], 1, 0, 0);

    [Fact]
    public async Task LocalDetectionRestoresZoneCoordinatesAndKeepsOutsideNeighbors()
    {
        var requests = new List<TextCandidateDetectionRequest>();
        var detector = new Detector(r => { requests.Add(r); return TextCandidateDetectionResult.Available("test",
            [new TextCandidate(new(20, 20, 80, 24), .99)]); });
        var service = new TextCandidateRegionOcrService(detector, new OcrService(new UnusedOcr()));
        var area = new BoundingBox(64, 64, 192, 128);
        var request = Request();
        var result = await service.DetectAreasAsync(request, [area], [new TextCandidate(new(700, 100, 100, 24), .99)], Scope(area), default);
        Assert.Equal(2, result.Regions.Count);
        Assert.Contains(result.Regions, r => r.Candidate.Bounds == new BoundingBox(84, 84, 80, 24));
        Assert.Contains(result.Regions, r => r.Candidate.Bounds.X == 700);
        var sent = Assert.Single(requests);
        Assert.Equal(new CaptureRegion(164, 264, 192, 128), sent.Frame.Region);
        Assert.Same(request.Frame.Origin, sent.Frame.Origin);
        Assert.All(result.Regions, r => Assert.Same(request.Frame.Origin, r.Frame.Origin));
        Assert.Equal(192L * 128, result.RegionRefresh!.ProcessedPixels);
    }

    [Fact]
    public async Task EmptyChangedAreaRemovesOnlyItsCandidates()
    {
        var service = new TextCandidateRegionOcrService(new Detector(_ => TextCandidateDetectionResult.Available("test", [])),
            new OcrService(new UnusedOcr()));
        var area = new BoundingBox(64, 64, 192, 128);
        var result = await service.DetectAreasAsync(Request(), [area], [new TextCandidate(new(700, 100, 100, 24), .99)], Scope(area), default);
        Assert.Equal(700, Assert.Single(result.Regions).Candidate.Bounds.X);
    }

    [Fact]
    public async Task EdgeClippingFallsBackToFullZoneAndReportsActualWork()
    {
        var service = new TextCandidateRegionOcrService(new Detector(r => TextCandidateDetectionResult.Available("test",
            [new TextCandidate(r.Frame.Width < 1024 ? new(0, 20, 80, 24) : new(60, 84, 160, 24), .99)])),
            new OcrService(new UnusedOcr()));
        var area = new BoundingBox(64, 64, 192, 128);
        var result = await service.DetectAreasAsync(Request(), [area], [], Scope(area), default);
        Assert.Equal(new BoundingBox(60, 84, 160, 24), Assert.Single(result.Regions).Candidate.Bounds);
        Assert.True(result.RegionRefresh!.FullScan);
        Assert.Equal("boundary-fallback", result.RegionRefresh.Reason);
        Assert.Equal(2, result.RegionRefresh.DetectorCalls);
        Assert.Equal(192L * 128 + 1024L * 512, result.RegionRefresh.ProcessedPixels);
    }

    [Fact]
    public async Task LocalFailureDoesNotCommitPartialCandidates()
    {
        var service = new TextCandidateRegionOcrService(new Detector(_ => TextCandidateDetectionResult.Unavailable("test", "synthetic")),
            new OcrService(new UnusedOcr()));
        var area = new BoundingBox(64, 64, 192, 128);
        var result = await service.DetectAreasAsync(Request(), [area], [new TextCandidate(new(700, 100, 100, 24), .99)], Scope(area), default);
        Assert.Equal(TextCandidateDetectorAvailability.Unavailable, result.Availability);
        Assert.Empty(result.Regions);
    }

    [Fact]
    public async Task CancellationAfterNonCooperativeDetectorReturnsDoesNotApplyItsResult()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new TextCandidateRegionOcrService(new Detector(_ =>
        {
            cancellation.Cancel();
            return TextCandidateDetectionResult.Available("test", []);
        }), new OcrService(new UnusedOcr()));
        var area = new BoundingBox(64, 64, 192, 128);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.DetectAreasAsync(Request(), [area], [], Scope(area), cancellation.Token));
    }

    private sealed class Detector(Func<TextCandidateDetectionRequest, TextCandidateDetectionResult> detect) : ITextCandidateDetector
    {
        public Task<TextCandidateDetectionResult> DetectAsync(TextCandidateDetectionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(detect(request));
    }
    private sealed class UnusedOcr : IOcrEngine
    {
        public string EngineId => "test";
        public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Detection must not invoke OCR.");
    }
}
