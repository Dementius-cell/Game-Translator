using System.IO;
using GameTranslator.Application.Ocr;
using GameTranslator.Infrastructure.Composition;
using GameTranslator.Infrastructure.Ocr;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.Tests.Infrastructure;

public sealed class WindowsOcrEngineTests
{
    [Fact]
    public void InfrastructureServiceModule_RegistersWindowsOcrEngine()
    {
        var services = new ServiceCollection();

        new InfrastructureServiceModule().RegisterServices(services);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IOcrEngine)
                && descriptor.ImplementationType == typeof(WindowsOcrEngine));
    }

    [Fact]
    public void WindowsOcrEngine_UsesWindowsOcrAndMapsBoundingBoxesWithoutForbiddenApis()
    {
        var source = File.ReadAllText(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.Infrastructure",
                "Ocr",
                "WindowsOcrEngine.cs"));

        Assert.Contains("OcrEngine.TryCreateFromLanguage", source, StringComparison.Ordinal);
        Assert.Contains("OcrEngine.IsLanguageSupported", source, StringComparison.Ordinal);
        Assert.Contains("RecognizeAsync", source, StringComparison.Ordinal);
        Assert.Contains("SoftwareBitmap", source, StringComparison.Ordinal);
        Assert.Contains("BoundingRect", source, StringComparison.Ordinal);
        Assert.Contains("OcrTextBlock", source, StringComparison.Ordinal);

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
