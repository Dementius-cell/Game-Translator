using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class OcrPreprocessorTests
{
    private static readonly CaptureRegion Region = new(0, 0, 2, 1);
    private static readonly DateTimeOffset CapturedAt = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Apply_WhenDisabled_ReturnsOriginalFrame()
    {
        var frame = CreateFrame(10, 20, 30, 255, 100, 110, 120, 255);
        var preprocessor = new OcrPreprocessor();

        var result = preprocessor.Apply(frame, OcrPreprocessingSettings.Default);

        Assert.Same(frame, result);
    }

    [Fact]
    public void Apply_WithThresholding_ConvertsPixelsToBlackAndWhite()
    {
        var frame = CreateFrame(10, 10, 10, 255, 240, 240, 240, 255);
        var preprocessor = new OcrPreprocessor();

        var result = preprocessor.Apply(frame, new OcrPreprocessingSettings
        {
            IsEnabled = true,
            ThresholdingEnabled = true,
            Threshold = 128,
        });
        var pixels = result.PixelData.ToArray();

        Assert.Equal(new byte[] { 0, 0, 0, 255, 255, 255, 255, 255 }, pixels);
    }

    [Fact]
    public void Apply_WithScale_ResizesFrameBeforeOcr()
    {
        var frame = CreateFrame(10, 20, 30, 255, 100, 110, 120, 255);
        var preprocessor = new OcrPreprocessor();

        var result = preprocessor.Apply(frame, new OcrPreprocessingSettings
        {
            IsEnabled = true,
            Scale = 2,
        });

        Assert.Equal(4, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(16, result.Stride);
        Assert.Equal(32, result.PixelData.Length);
    }

    private static CapturedFrame CreateFrame(params byte[] pixels)
    {
        return new CapturedFrame(
            Region,
            2,
            1,
            8,
            "Bgra32",
            pixels,
            CapturedAt);
    }
}