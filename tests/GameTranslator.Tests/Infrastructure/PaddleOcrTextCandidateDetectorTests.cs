using System.IO;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;
using GameTranslator.Infrastructure.Composition;
using GameTranslator.Infrastructure.Ocr;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.Tests.Infrastructure;

public sealed class PaddleOcrTextCandidateDetectorTests
{
    [Fact]
    public async Task DetectAsync_WhenPilotRuntimeIsNotPackaged_ReturnsUnavailableWithoutCandidates()
    {
        using var detector = new PaddleOcrTextCandidateDetector(new PaddleOcrTextCandidateDetectorOptions
        {
            PythonExecutablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "python.exe"),
            WorkerScriptPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "worker.py"),
        });

        var result = await detector.DetectAsync(new TextCandidateDetectionRequest(
            CreateFrame(),
            "ja",
            OcrOrientationMode.Vertical,
            OcrLayoutMode.Comic));

        Assert.Equal(TextCandidateDetectorAvailability.Unavailable, result.Availability);
        Assert.Empty(result.Candidates);
        Assert.Contains("not packaged", result.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InfrastructureServiceModule_RegistersPaddleCandidateDetectorBehindApplicationContract()
    {
        var services = new ServiceCollection();

        new InfrastructureServiceModule().RegisterServices(services);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ITextCandidateDetector)
                && descriptor.ImplementationType == typeof(PaddleOcrTextCandidateDetector));
    }

    [Fact]
    public void Options_DefaultStartupTimeout_AllowsAdr025OptInColdStartBudget()
    {
        var options = new PaddleOcrTextCandidateDetectorOptions();

        Assert.Equal(TimeSpan.FromSeconds(5), options.StartupTimeout);
    }

    [Fact]
    public void Worker_UsesBundledDirectPaddleInferenceWithoutHighLevelTextDetectionStartup()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot.Find(),
            "src",
            "GameTranslator.Infrastructure",
            "Ocr",
            "paddle_text_detector_worker.py"));

        Assert.Contains("paddle.inference.Config", source, StringComparison.Ordinal);
        Assert.Contains("PP-OCRv6_medium_det", source, StringComparison.Ordinal);
        Assert.Contains("DetResizeForTest(limit_side_len=1216, limit_type=\"max\")", source, StringComparison.Ordinal);
        Assert.Contains("DBPostProcess(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("from paddleocr import TextDetection", source, StringComparison.Ordinal);
    }

    private static CapturedFrame CreateFrame()
    {
        const int width = 20;
        const int height = 10;
        const int stride = width * 4;
        return new CapturedFrame(
            new CaptureRegion(0, 0, width, height),
            width,
            height,
            stride,
            "Bgra32",
            new byte[stride * height],
            DateTimeOffset.UtcNow);
    }
}
