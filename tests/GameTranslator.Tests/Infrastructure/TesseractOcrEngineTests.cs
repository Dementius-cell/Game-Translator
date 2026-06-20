using System.IO;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;
using GameTranslator.Infrastructure.Composition;
using GameTranslator.Infrastructure.Ocr;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.Tests.Infrastructure;

public sealed class TesseractOcrEngineTests
{
    [Fact]
    public void InfrastructureServiceModule_RegistersTesseractOcrEngine()
    {
        var services = new ServiceCollection();

        new InfrastructureServiceModule().RegisterServices(services);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IOcrEngine)
                && descriptor.ImplementationType == typeof(TesseractOcrEngine));
    }

    [Fact]
    public void TesseractOcrEngine_ExposesTesseractEngineId()
    {
        var engine = new TesseractOcrEngine("tessdata");

        Assert.Equal(OcrSettings.TesseractEngineId, engine.EngineId);
    }

    [Fact]
    public void TesseractOcrEngine_UsesTesseractWrapperAndMapsLayoutBoundingBoxesSafely()
    {
        var source = File.ReadAllText(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.Infrastructure",
                "Ocr",
                "TesseractOcrEngine.cs"));

        Assert.Contains("new Engine", source, StringComparison.Ordinal);
        Assert.Contains("PixImage.LoadFromMemory", source, StringComparison.Ordinal);
        Assert.Contains("PageSegMode.SingleBlock", source, StringComparison.Ordinal);
        Assert.Contains("page.Layout", source, StringComparison.Ordinal);
        Assert.Contains("textLine.BoundingBox", source, StringComparison.Ordinal);
        Assert.Contains("new OcrTextBlock", source, StringComparison.Ordinal);
        Assert.Contains("OcrEngineException", source, StringComparison.Ordinal);
        Assert.Contains("MapLanguage", source, StringComparison.Ordinal);
        Assert.Contains("CreateBitmapBytes", source, StringComparison.Ordinal);

        var forbiddenApiNames = new[]
        {
            "ReadProcessMemory",
            "WriteProcessMemory",
            "CreateRemoteThread",
            "SetWindowsHookEx",
            "BitBlt",
            "CopyFromScreen",
        };

        foreach (var forbiddenApiName in forbiddenApiNames)
        {
            Assert.DoesNotContain(forbiddenApiName, source, StringComparison.Ordinal);
        }
    }
}