using System.IO;
using System.Reflection;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;
using GameTranslator.Infrastructure.Composition;
using GameTranslator.Infrastructure.Ocr;
using Microsoft.Extensions.DependencyInjection;
using TesseractOCR.Enums;

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
    [InlineData("th", OcrOrientationMode.Horizontal, "tha")]
    [InlineData("th-TH", OcrOrientationMode.Vertical, "tha")]
    [InlineData("tha", OcrOrientationMode.Horizontal, "tha")]
    [InlineData("ar", OcrOrientationMode.Horizontal, "ara")]
    [InlineData("de-DE", OcrOrientationMode.Horizontal, "deu")]
    [InlineData("ku", OcrOrientationMode.Horizontal, "kmr")]
    [InlineData("zh-CN", OcrOrientationMode.Horizontal, "chi_sim")]
    [InlineData("ja+en", OcrOrientationMode.Vertical, "jpn_vert+eng")]
    [InlineData("aze_cyrl", OcrOrientationMode.Horizontal, "aze_cyrl")]
    [InlineData("srp-latn", OcrOrientationMode.Horizontal, "srp_latn")]
    public void TesseractOcrEngine_MapsConfiguredLanguageModelsForOrientation(
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
        Assert.Contains("PageSegMode.SingleLine", source, StringComparison.Ordinal);
        Assert.Contains("PageSegMode.SparseText", source, StringComparison.Ordinal);
        Assert.Contains("PageSegMode.OsdOnly", source, StringComparison.Ordinal);
        Assert.Contains("PageSegMode.SingleBlockVertText", source, StringComparison.Ordinal);
        Assert.Contains("DetectOrientation", source, StringComparison.Ordinal);
        Assert.Contains("SelectRecognitionOrientationMode", source, StringComparison.Ordinal);
        Assert.Contains("jpn_vert", source, StringComparison.Ordinal);
        Assert.Contains("chi_sim_vert", source, StringComparison.Ordinal);
        Assert.Contains("chi_tra_vert", source, StringComparison.Ordinal);
        Assert.Contains("OrientationConfidenceThreshold", source, StringComparison.Ordinal);
        Assert.Contains("catch (TesseractException)", source, StringComparison.Ordinal);
        Assert.Contains("page.Layout", source, StringComparison.Ordinal);
        Assert.Contains("textLine.BoundingBox", source, StringComparison.Ordinal);
        Assert.Contains("textLine.Words", source, StringComparison.Ordinal);
        Assert.Contains("word.Confidence", source, StringComparison.Ordinal);
        Assert.Contains("new OcrWord", source, StringComparison.Ordinal);
        Assert.Contains("CreateRecognitionPassId", source, StringComparison.Ordinal);
        Assert.Contains("CreateComicResult", source, StringComparison.Ordinal);
        Assert.Contains("CreateCroppedFrame", source, StringComparison.Ordinal);
        Assert.Contains("CreateComicSourceBlock", source, StringComparison.Ordinal);
        Assert.Contains("CreateTextBlockSources", source, StringComparison.Ordinal);
        Assert.Contains("AddEmptyComicFallback", source, StringComparison.Ordinal);
        Assert.Contains("empty-comic-fallback", source, StringComparison.Ordinal);
        Assert.Contains("TryCreateQualityUpscaleFallback", source, StringComparison.Ordinal);
        Assert.Contains("QualityUpscaleFallbackScale", source, StringComparison.Ordinal);
        Assert.Contains("quality-upscale-fallback", source, StringComparison.Ordinal);
        Assert.Contains("quality-upscale-detection", source, StringComparison.Ordinal);
        Assert.Contains("quality-upscale-line-refinement", source, StringComparison.Ordinal);
        Assert.Contains("ScaleFrameBilinear", source, StringComparison.Ordinal);
        Assert.Contains("MapBoundsFromPreprocessedFrame", source, StringComparison.Ordinal);
        Assert.Contains("IsCjkOrThaiLanguage", source, StringComparison.Ordinal);
        Assert.Contains("MinimumComicSourceWordConfidence", source, StringComparison.Ordinal);
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

    [Theory]
    [InlineData(OcrLayoutMode.Menu, OcrOrientationMode.Horizontal, PageSegMode.SparseText)]
    [InlineData(OcrLayoutMode.Comic, OcrOrientationMode.Vertical, PageSegMode.SparseText)]
    [InlineData(OcrLayoutMode.Dialog, OcrOrientationMode.Horizontal, PageSegMode.SingleBlock)]
    [InlineData(OcrLayoutMode.Dialog, OcrOrientationMode.Vertical, PageSegMode.SingleBlockVertText)]
    [InlineData(OcrLayoutMode.Auto, OcrOrientationMode.Horizontal, PageSegMode.SingleBlock)]
    public void TesseractOcrEngine_MapsLayoutModeToTheExpectedPageSegmentationMode(
        OcrLayoutMode layoutMode,
        OcrOrientationMode orientationMode,
        PageSegMode expectedPageSegMode)
    {
        Assert.Equal(expectedPageSegMode, InvokeMapLayoutMode(layoutMode, orientationMode));
    }

    [Fact]
    public void TesseractOcrEngine_ComicSourceBlock_UsesOnlyReliableWordGeometry()
    {
        var reliableWord = new OcrWord(
            "visible",
            new BoundingBox(30, 20, 18, 24),
            confidence: 88,
            recognitionPassId: "tesseract:SingleLine:line-refinement");
        var lowConfidenceWord = new OcrWord(
            "noise",
            new BoundingBox(0, 0, 120, 100),
            confidence: 49.99,
            recognitionPassId: "tesseract:SingleLine:line-refinement");

        var block = InvokeCreateComicSourceBlock(new[] { reliableWord, lowConfidenceWord });

        Assert.NotNull(block);
        Assert.Equal("visible", block.Text);
        Assert.Equal(new BoundingBox(30, 20, 18, 24), block.Bounds);
    }

    [Theory]
    [InlineData("chi_sim_vert", true)]
    [InlineData("jpn_vert+eng", true)]
    [InlineData("tha", true)]
    [InlineData("eng", false)]
    public void TesseractOcrEngine_QualityUpscaleFallback_IsLimitedToCjkOrThai(
        string tesseractLanguage,
        bool expected)
    {
        Assert.Equal(expected, InvokeIsCjkOrThaiLanguage(tesseractLanguage));
    }

    [Theory]
    [InlineData(21, 41, 19, 23, 100, 80, 2d, 10, 20, 10, 12)]
    [InlineData(198, 158, 20, 20, 100, 80, 2d, 99, 79, 1, 1)]
    public void TesseractOcrEngine_QualityUpscaleFallback_MapsBoundsBackToOriginalFrame(
        int x,
        int y,
        int width,
        int height,
        int originalWidth,
        int originalHeight,
        double scale,
        int expectedX,
        int expectedY,
        int expectedWidth,
        int expectedHeight)
    {
        var actual = InvokeMapBoundsFromPreprocessedFrame(
            new BoundingBox(x, y, width, height),
            originalWidth,
            originalHeight,
            scale);

        Assert.Equal(new BoundingBox(expectedX, expectedY, expectedWidth, expectedHeight), actual);
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

    private static PageSegMode InvokeMapLayoutMode(OcrLayoutMode layoutMode, OcrOrientationMode orientationMode)
    {
        var method = typeof(TesseractOcrEngine).GetMethod(
            "MapLayoutMode",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(OcrLayoutMode), typeof(OcrOrientationMode) },
            modifiers: null)
            ?? throw new InvalidOperationException("MapLayoutMode was not found.");

        return (PageSegMode)(method.Invoke(null, new object[] { layoutMode, orientationMode })
            ?? throw new InvalidOperationException("MapLayoutMode returned null."));
    }

    private static OcrTextBlock? InvokeCreateComicSourceBlock(IReadOnlyList<OcrWord> words)
    {
        var method = typeof(TesseractOcrEngine).GetMethod(
            "CreateComicSourceBlock",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(IReadOnlyList<OcrWord>) },
            modifiers: null)
            ?? throw new InvalidOperationException("CreateComicSourceBlock was not found.");

        return (OcrTextBlock?)method.Invoke(null, new object[] { words });
    }

    private static bool InvokeIsCjkOrThaiLanguage(string tesseractLanguage)
    {
        var method = typeof(TesseractOcrEngine).GetMethod(
            "IsCjkOrThaiLanguage",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null)
            ?? throw new InvalidOperationException("IsCjkOrThaiLanguage was not found.");

        return (bool)(method.Invoke(null, new object[] { tesseractLanguage })
            ?? throw new InvalidOperationException("IsCjkOrThaiLanguage returned null."));
    }

    private static BoundingBox InvokeMapBoundsFromPreprocessedFrame(
        BoundingBox bounds,
        int originalWidth,
        int originalHeight,
        double scale)
    {
        var method = typeof(TesseractOcrEngine).GetMethod(
            "MapBoundsFromPreprocessedFrame",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(BoundingBox), typeof(int), typeof(int), typeof(double) },
            modifiers: null)
            ?? throw new InvalidOperationException("MapBoundsFromPreprocessedFrame was not found.");

        return (BoundingBox)(method.Invoke(null, new object[] { bounds, originalWidth, originalHeight, scale })
            ?? throw new InvalidOperationException("MapBoundsFromPreprocessedFrame returned null."));
    }
}
