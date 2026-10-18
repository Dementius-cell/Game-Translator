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

    [Fact]
    public void Inversion_PreservesAlphaAndOriginalPixels()
    {
        var frame = CreateFrame(10, 20, 30, 123, 255, 255, 255, 255);
        var result = new OcrPreprocessor().Apply(frame, new() { IsEnabled = true, InvertColors = true });
        Assert.Equal(new byte[] { 245, 235, 225, 123, 0, 0, 0, 255 }, result.PixelData.ToArray());
        Assert.Equal(10, frame.PixelData.Span[0]);
        Assert.Same(frame, new OcrPreprocessor().Apply(frame, new() { InvertColors = true, AdaptiveThresholding = true }));
    }

    [Fact]
    public void Sauvola_SeparatesStrokesAcrossUnevenBackground_AndOverridesFixedThreshold()
    {
        const int width = 120, height = 40;
        var data = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var value = (byte)(80 + x - (x % 20 is >= 8 and <= 11 ? 65 : 0));
            var offset = (y * width + x) * 4;
            data[offset] = data[offset + 1] = data[offset + 2] = value;
            data[offset + 3] = 255;
        }
        var frame = new CapturedFrame(new(0, 0, width, height), width, height, width * 4, "Bgra32", data, CapturedAt);
        var result = new OcrPreprocessor().Apply(frame, new()
            { IsEnabled = true, AdaptiveThresholding = true, ThresholdingEnabled = true, Threshold = 255 });
        for (var x = 20; x < 100; x++)
            Assert.Equal(x % 20 is >= 8 and <= 11 ? (byte)0 : (byte)255, result.PixelData.Span[(20 * width + x) * 4]);
    }

    [Fact]
    public void NewPreparation_IsCancellable()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => new OcrPreprocessor().Apply(
            CreateFrame(1, 2, 3, 255, 4, 5, 6, 255), new() { IsEnabled = true, AdaptiveThresholding = true }, source.Token));
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