using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;
using GameTranslator.Infrastructure.Ocr;

namespace GameTranslator.Tests.Infrastructure;

public sealed class TesseractDetectorOrientationTests
{
    [Theory]
    [InlineData("jpn")]
    [InlineData("ja-JP")]
    [InlineData("chi_sim")]
    [InlineData("chi_tra")]
    [InlineData("eng+jpn")]
    public void Auto_UsesVerticalDetectorColumns(string language)
    {
        var request = Request(language, new(10, 10, 30, 120), new(60, 10, 30, 120));
        Assert.Equal(OcrOrientationMode.Vertical, TesseractOcrEngine.SelectDetectorOrientation(request));
    }

    [Fact]
    public void Auto_TallGroupOfHorizontalLinesStaysHorizontal()
    {
        var request = Request("jpn", new(10, 10, 80, 20), new(10, 60, 80, 20), new(10, 110, 80, 20));
        Assert.Equal(OcrOrientationMode.Horizontal, TesseractOcrEngine.SelectDetectorOrientation(request));
    }

    [Theory]
    [InlineData("eng")]
    [InlineData("kor")]
    [InlineData("rus")]
    [InlineData("tha")]
    [InlineData("jpn+kor+chi_sim+eng")]
    public void OtherLanguages_RetainNativeAuto(string language) =>
        Assert.Null(TesseractOcrEngine.SelectDetectorOrientation(Request(language, new BoundingBox(10, 10, 30, 120))));

    [Theory]
    [InlineData(30, 30)]
    [InlineData(30, 50)]
    [InlineData(5, 100)]
    [InlineData(8, 180)]
    public void AmbiguousSmallOrExtremeLines_RetainNativeAuto(int width, int height) =>
        Assert.Null(TesseractOcrEngine.SelectDetectorOrientation(Request("jpn", new BoundingBox(0, 0, width, height))));

    [Fact]
    public void MissingOrMixedHints_RetainNativeAuto()
    {
        Assert.Null(TesseractOcrEngine.SelectDetectorOrientation(Request("jpn")));
        Assert.Null(TesseractOcrEngine.SelectDetectorOrientation(Request("jpn", new(0, 0, 30, 120), new(40, 0, 100, 20))));
    }

    [Theory]
    [InlineData(OcrOrientationMode.Horizontal)]
    [InlineData(OcrOrientationMode.Vertical)]
    public void ExplicitOrientation_IsNeverOverridden(OcrOrientationMode orientation)
    {
        var request = Request("jpn", new BoundingBox(10, 10, 30, 120));
        var explicitRequest = new OcrRequest(request.Frame, request.Language, orientationMode: orientation)
        { DetectorLineBounds = request.DetectorLineBounds };
        Assert.Null(TesseractOcrEngine.SelectDetectorOrientation(explicitRequest));
    }

    [Fact]
    public void OversizedOrUnboundedHints_RetainNativeAuto()
    {
        Assert.Null(TesseractOcrEngine.SelectDetectorOrientation(Request("jpn",
            Enumerable.Repeat(new BoundingBox(0, 0, 30, 100), 33).ToArray())));
        var large = new CapturedFrame(new CaptureRegion(0, 0, 1001, 1000), 1001, 1000, 4004,
            "Bgra32", new byte[4004000], DateTimeOffset.UnixEpoch);
        Assert.Null(TesseractOcrEngine.SelectDetectorOrientation(new OcrRequest(large, "jpn", orientationMode: OcrOrientationMode.Auto)
        { DetectorLineBounds = new[] { new BoundingBox(10, 10, 30, 100) } }));
    }

    [Fact]
    public void ExplicitComicSegmentation_RetainsExistingPath()
    {
        var request = Request("jpn", new BoundingBox(0, 0, 30, 100));
        Assert.Null(TesseractOcrEngine.SelectDetectorOrientation(new OcrRequest(request.Frame, "jpn",
            orientationMode: OcrOrientationMode.Auto, layoutMode: OcrLayoutMode.Comic)
        { DetectorLineBounds = request.DetectorLineBounds }));
    }

    [Fact]
    public void MissingVerticalColumns_RecoversOnlyAbsentLongColumn()
    {
        var request = Request("jpn", new(0, 0, 30, 120), new(35, 0, 30, 160), new(70, 0, 30, 45));
        var blocks = new[] { new OcrTextBlock("known", new(2, 2, 25, 110)) };
        var missing = TesseractOcrEngine.ResolveMissingVerticalColumns(request, OcrOrientationMode.Vertical, blocks);
        Assert.Equal(new BoundingBox(35, 0, 30, 160), Assert.Single(missing));
        Assert.Empty(TesseractOcrEngine.ResolveMissingVerticalColumns(request, OcrOrientationMode.Vertical,
            blocks.Append(new OcrTextBlock("known too", new(38, 2, 25, 150))).ToArray()));
    }

    [Theory]
    [InlineData("eng")]
    [InlineData("chi_sim")]
    [InlineData("jpn+eng")]
    public void MissingVerticalColumns_DoesNotChangeOtherLanguageSets(string language)
    {
        var request = Request(language, new(0, 0, 30, 120), new(35, 0, 30, 160));
        Assert.Empty(TesseractOcrEngine.ResolveMissingVerticalColumns(request, OcrOrientationMode.Vertical,
            new[] { new OcrTextBlock("known", new(2, 2, 25, 110)) }));
    }

    [Fact]
    public void MissingVerticalColumns_RejectsEmptyHorizontalExplicitAndInvalidRequests()
    {
        var request = Request("jpn", new(0, 0, 30, 120), new(35, 0, 30, 160));
        var blocks = new[] { new OcrTextBlock("known", new(2, 2, 25, 110)) };
        Assert.Empty(TesseractOcrEngine.ResolveMissingVerticalColumns(request, OcrOrientationMode.Vertical, Array.Empty<OcrTextBlock>()));
        Assert.Empty(TesseractOcrEngine.ResolveMissingVerticalColumns(request, OcrOrientationMode.Horizontal, blocks));
        var explicitRequest = new OcrRequest(request.Frame, "jpn", orientationMode: OcrOrientationMode.Vertical)
            { DetectorLineBounds = request.DetectorLineBounds };
        Assert.Empty(TesseractOcrEngine.ResolveMissingVerticalColumns(explicitRequest, OcrOrientationMode.Vertical, blocks));
        Assert.Throws<ArgumentException>(() => Request("jpn", new(0, 0, 30, 120), new(190, 0, 30, 160)));

        var mixed = Request("jpn", new(0, 0, 30, 120), new(35, 0, 100, 20));
        Assert.Empty(TesseractOcrEngine.ResolveMissingVerticalColumns(mixed, OcrOrientationMode.Vertical, blocks));
    }

    private static OcrRequest Request(string language, params BoundingBox[] lines) =>
        new(new CapturedFrame(new CaptureRegion(0, 0, 200, 200), 200, 200, 800, "Bgra32", new byte[160000], DateTimeOffset.UnixEpoch),
            language, engineId: "Tesseract", orientationMode: OcrOrientationMode.Auto)
        { DetectorLineBounds = lines };
}
