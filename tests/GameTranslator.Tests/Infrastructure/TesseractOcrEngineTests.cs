using System.IO;
using System.Reflection;
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

    [Theory]
    [InlineData("ja", OcrOrientationMode.Horizontal, "jpn")]
    [InlineData("ja", OcrOrientationMode.Vertical, "jpn_vert")]
    [InlineData("ja-JP", OcrOrientationMode.Vertical, "jpn_vert")]
    [InlineData("jpn_vert", OcrOrientationMode.Horizontal, "jpn_vert")]
    [InlineData("zh", OcrOrientationMode.Horizontal, "chi_sim")]
    [InlineData("zh", OcrOrientationMode.Vertical, "chi_sim_vert")]
    [InlineData("zh-CN", OcrOrientationMode.Vertical, "chi_sim_vert")]
    [InlineData("zh-Hans", OcrOrientationMode.Vertical, "chi_sim_vert")]
    [InlineData("chi_sim_vert", OcrOrientationMode.Horizontal, "chi_sim_vert")]
    [InlineData("zh-TW", OcrOrientationMode.Horizontal, "chi_tra")]
    [InlineData("zh-TW", OcrOrientationMode.Vertical, "chi_tra_vert")]
    [InlineData("zh-Hant", OcrOrientationMode.Vertical, "chi_tra_vert")]
    [InlineData("chi_tra_vert", OcrOrientationMode.Horizontal, "chi_tra_vert")]
    [InlineData("ja+en", OcrOrientationMode.Vertical, "jpn_vert+eng")]
    public void TesseractOcrEngine_MapsJapaneseAndChineseLanguageModelsForOrientation(
        string languageTag,
        OcrOrientationMode orientationMode,
        string expectedTesseractLanguage)
    {
        var actual = InvokeMapLanguage(languageTag, orientationMode);

        Assert.Equal(expectedTesseractLanguage, actual);
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
        Assert.Contains("PageSegMode.OsdOnly", source, StringComparison.Ordinal);
        Assert.Contains("PageSegMode.SingleBlockVertText", source, StringComparison.Ordinal);
        Assert.Contains("DetectOrientation", source, StringComparison.Ordinal);
        Assert.Contains("GetRecognitionOrientationMode", source, StringComparison.Ordinal);
        Assert.Contains("jpn_vert", source, StringComparison.Ordinal);
        Assert.Contains("chi_sim_vert", source, StringComparison.Ordinal);
        Assert.Contains("chi_tra_vert", source, StringComparison.Ordinal);
        Assert.Contains("OrientationConfidenceThreshold", source, StringComparison.Ordinal);
        Assert.Contains("catch (TesseractException)", source, StringComparison.Ordinal);
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

    private static string InvokeMapLanguage(string languageTag, OcrOrientationMode orientationMode)
    {
        var method = typeof(TesseractOcrEngine).GetMethod(
            "MapLanguage",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(OcrOrientationMode) },
            modifiers: null)
            ?? throw new InvalidOperationException("MapLanguage overload was not found.");

        return (string)(method.Invoke(null, new object[] { languageTag, orientationMode })
            ?? throw new InvalidOperationException("MapLanguage returned null."));
    }
}
