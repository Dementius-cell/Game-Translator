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
        Assert.Contains("lstrip(\"\\ufeffï»¿п»ї\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("from paddleocr import TextDetection", source, StringComparison.Ordinal);

        var detectorSource = File.ReadAllText(Path.Combine(
            RepositoryRoot.Find(),
            "src",
            "GameTranslator.Infrastructure",
            "Ocr",
            "PaddleOcrTextCandidateDetector.cs"));
        Assert.Contains(
            "StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)",
            detectorSource,
            StringComparison.Ordinal);

        var releaseBuilderSource = File.ReadAllText(Path.Combine(
            RepositoryRoot.Find(),
            "tools",
            "build-track-d-opt-in-release.ps1"));
        Assert.Contains(
            "Get-Content -LiteralPath $workerScript -Raw -Encoding utf8",
            releaseBuilderSource,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TextCandidateDetectorPreset.Standard, "chi_sim", OcrOrientationMode.Vertical, 0.60)]
    [InlineData(TextCandidateDetectorPreset.ChineseExperimental, "chi_sim", OcrOrientationMode.Vertical, 0.65)]
    [InlineData(TextCandidateDetectorPreset.ChineseStrictExperimental, "zh-TW", OcrOrientationMode.Vertical, 0.70)]
    [InlineData(TextCandidateDetectorPreset.ChineseExperimental, "jpn_vert", OcrOrientationMode.Vertical, 0.60)]
    [InlineData(TextCandidateDetectorPreset.ChineseStrictExperimental, "eng", OcrOrientationMode.Horizontal, 0.60)]
    public void PresetResolver_AppliesExperimentalThresholdsOnlyToChineseRequests(
        TextCandidateDetectorPreset preset,
        string language,
        OcrOrientationMode orientationMode,
        double expectedBoxThreshold)
    {
        var settings = PaddleTextDetectionPresetResolver.Resolve(preset, language, orientationMode);

        Assert.Equal(0.30, settings.Threshold);
        Assert.Equal(expectedBoxThreshold, settings.BoxThreshold);
        Assert.Equal(1.20, settings.UnclipRatio);
    }

    [Fact]
    public void Worker_AppliesDetectionThresholdsFromEachRequestWithoutMutatingThePredictor()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot.Find(),
            "src",
            "GameTranslator.Infrastructure",
            "Ocr",
            "paddle_text_detector_worker.py"));

        Assert.Contains("detector.detect(input_path, request[\"threshold\"], request[\"boxThreshold\"], request[\"unclipRatio\"])", source, StringComparison.Ordinal);
        Assert.Contains("self.postprocess([output], image_shapes, threshold, box_threshold, unclip_ratio)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PackagedWorkerVerifier_SendsTheCompleteStandardPresetRequestContract()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot.Find(),
            "tools",
            "verify-track-d-opt-in-release.ps1"));

        Assert.Contains("threshold = 0.3", source, StringComparison.Ordinal);
        Assert.Contains("boxThreshold = 0.6", source, StringComparison.Ordinal);
        Assert.Contains("unclipRatio = 1.2", source, StringComparison.Ordinal);
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
