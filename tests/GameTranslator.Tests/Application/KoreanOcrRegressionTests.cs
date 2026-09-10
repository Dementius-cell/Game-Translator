using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Cache;
using GameTranslator.Domain.Profiles;
using GameTranslator.Infrastructure.Ocr;

namespace GameTranslator.Tests.Application;

public sealed class KoreanOcrRegressionTests
{
    [Theory]
    [InlineData("가 나", "가 나")]
    [InlineData("  가  나\n다  ", "가 나 다")]
    [InlineData("日 本 語", "日本語")]
    [InlineData("한 日", "한 日")]
    [InlineData("hello  world", "hello world")]
    public void Normalization_PreservesMeaningfulKoreanSpaces(string input, string expected) =>
        Assert.Equal(expected, OcrTextNormalizer.NormalizeForComparison(input));

    [Fact]
    public void CacheKey_KoreanWordBoundaryDoesNotReuseOldCompactKey()
    {
        var compact = new TranslationCacheKey("YandexWeb", "ko", "ru", "가나");
        var spaced = new TranslationCacheKey("YandexWeb", "ko", "ru", "가 나");
        Assert.NotEqual(compact, spaced);
        Assert.NotEqual(compact.SourceTextHash, spaced.SourceTextHash);
        Assert.Equal("가 나", spaced.SourceText);
    }

    [Theory]
    [InlineData("ko", OcrOrientationMode.Horizontal, true)]
    [InlineData("ko-KR", OcrOrientationMode.Horizontal, true)]
    [InlineData("kor", OcrOrientationMode.Horizontal, true)]
    [InlineData("ja", OcrOrientationMode.Horizontal, false)]
    [InlineData("en", OcrOrientationMode.Horizontal, false)]
    [InlineData("ko", OcrOrientationMode.Vertical, false)]
    [InlineData("ko+en", OcrOrientationMode.Horizontal, false)]
    public void DetectorLines_AreLimitedToKoreanHorizontal(string language, OcrOrientationMode mode, bool enabled)
    {
        var request = Request(language, new[] { new BoundingBox(0, 0, 60, 20), new BoundingBox(0, 25, 80, 20) });
        Assert.Equal(enabled ? 2 : 0, TesseractOcrEngine.ResolveKoreanDetectorLines(request, mode).Count);
    }

    [Fact]
    public void DetectorLines_RejectTallAndOverlappingRegions()
    {
        Assert.Empty(TesseractOcrEngine.ResolveKoreanDetectorLines(
            Request("ko", new[] { new BoundingBox(0, 0, 20, 40) }), OcrOrientationMode.Horizontal));
        Assert.Empty(TesseractOcrEngine.ResolveKoreanDetectorLines(
            Request("ko", new[] { new BoundingBox(0, 0, 60, 30), new BoundingBox(5, 20, 60, 30) }), OcrOrientationMode.Horizontal));
        Assert.Empty(TesseractOcrEngine.ResolveKoreanDetectorLines(Request("ko", Array.Empty<BoundingBox>()), OcrOrientationMode.Horizontal));
        Assert.Empty(TesseractOcrEngine.ResolveKoreanDetectorLines(Request("ko", new[] { new BoundingBox(0, 0, 60, 12) }), OcrOrientationMode.Horizontal));
        Assert.Empty(TesseractOcrEngine.ResolveKoreanDetectorLines(Request("ko", new[] { new BoundingBox(0, 0, 60, 58) }), OcrOrientationMode.Horizontal));
    }

    [Fact]
    public void DetectorLines_CopyAndValidateBounds()
    {
        var lines = new[] { new BoundingBox(0, 0, 60, 15) };
        var request = Request("ko", lines);
        lines[0] = new BoundingBox(5, 5, 70, 15);
        Assert.Equal(0, request.DetectorLineBounds[0].X);
        Assert.Throws<ArgumentException>(() => Request("ko", new[] { new BoundingBox(90, 0, 20, 15) }));
    }

    [Fact]
    public async Task Preprocessing_ScalesDetectorBoundsWithPixels()
    {
        var engine = new RecordingEngine();
        var request = new OcrRequest(Frame(), "ko", engineId: "Tesseract",
            preprocessingSettings: new OcrPreprocessingSettings { IsEnabled = true, Scale = 2 })
            { DetectorLineBounds = new[] { new BoundingBox(10, 20, 50, 15) } };
        await new OcrService(engine).RecognizeAsync(request);
        Assert.Equal(new BoundingBox(20, 40, 100, 30), Assert.Single(engine.Request!.DetectorLineBounds));
        Assert.Equal(200, engine.Request.Frame.Width);
    }

    [Fact]
    public void Coverage_DistinguishesPartialFromCompleteAndEmpty()
    {
        Assert.Equal(1, new OcrLineRecognitionDiagnostics(2, 1).MissingLines);
        Assert.Equal(0, new OcrLineRecognitionDiagnostics(2, 2).MissingLines);
        Assert.Equal(2, new OcrLineRecognitionDiagnostics(2, 0).MissingLines);
        Assert.Throws<ArgumentOutOfRangeException>(() => new OcrLineRecognitionDiagnostics(2, 3));
    }

    private static CapturedFrame Frame() => new(new CaptureRegion(0, 0, 100, 100), 100, 100, 400, "Bgra32", new byte[40000], DateTimeOffset.UtcNow);
    private static OcrRequest Request(string language, IReadOnlyList<BoundingBox> lines) =>
        new(Frame(), language) { DetectorLineBounds = lines };
    private sealed class RecordingEngine : IOcrEngine
    {
        public string EngineId => "Tesseract";
        public OcrRequest? Request { get; private set; }
        public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new OcrResult(request, Array.Empty<OcrTextBlock>(), DateTimeOffset.UtcNow));
        }
    }
}
