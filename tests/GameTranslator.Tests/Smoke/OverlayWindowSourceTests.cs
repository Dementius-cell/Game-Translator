using System.IO;

namespace GameTranslator.Tests.Smoke;

public sealed class OverlayWindowSourceTests
{
    [Fact]
    public void Application_ShutsDownWhenMainWindowClosesEvenIfOverlayIsOpen()
    {
        var source = ReadSource("src/GameTranslator.UI/App.xaml");

        Assert.Contains("ShutdownMode=\"OnMainWindowClose\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayWindow_UsesTransparentTopmostWpfWindowSettings()
    {
        var source = ReadSource("src/GameTranslator.UI/Views/OverlayWindow.xaml");

        Assert.Contains("AllowsTransparency=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("WindowStyle=\"None\"", source, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", source, StringComparison.Ordinal);
        Assert.Contains("Topmost=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("UseLayoutRounding=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar=\"False\"", source, StringComparison.Ordinal);
        Assert.Contains("ShowActivated=\"False\"", source, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding TextItems}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayWindow_RendersPreviewTextInsideOcrBounds()
    {
        var source = ReadSource("src/GameTranslator.UI/Views/OverlayWindow.xaml");

        Assert.Contains("Width=\"{Binding Width}\"", source, StringComparison.Ordinal);
        Assert.Contains("Height=\"{Binding Height}\"", source, StringComparison.Ordinal);
        Assert.Contains("<Viewbox", source, StringComparison.Ordinal);
        Assert.Contains("Stretch=\"Uniform\"", source, StringComparison.Ordinal);
        Assert.Contains("StretchDirection=\"Both\"", source, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"NoWrap\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TextTrimming", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayWindow_ConvertsDevicePixelsToWpfCoordinates()
    {
        var source = ReadSource("src/GameTranslator.UI/Views/OverlayWindow.xaml.cs");

        Assert.Contains("TransformFromDevice", source, StringComparison.Ordinal);
        Assert.Contains("FromDevicePixels", source, StringComparison.Ordinal);
        Assert.Contains("MinReadableItemWidth", source, StringComparison.Ordinal);
        Assert.Contains("MinReadableItemHeight", source, StringComparison.Ordinal);
        Assert.Contains("PreviewPadding", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayWindow_AppliesClickThroughStylesWithoutForbiddenGameProcessApis()
    {
        var source = ReadSource("src/GameTranslator.UI/Views/OverlayWindow.xaml.cs");

        Assert.Contains("WsExTransparent", source, StringComparison.Ordinal);
        Assert.Contains("WsExNoActivate", source, StringComparison.Ordinal);
        Assert.Contains("WsExToolWindow", source, StringComparison.Ordinal);
        Assert.Contains("GetWindowLongPtr", source, StringComparison.Ordinal);
        Assert.Contains("SetWindowLongPtr", source, StringComparison.Ordinal);
        Assert.Contains("WmNcHitTest", source, StringComparison.Ordinal);
        Assert.Contains("HtTransparent", source, StringComparison.Ordinal);

        foreach (var forbiddenApi in new[]
                 {
                     "ReadProcessMemory",
                     "WriteProcessMemory",
                     "SetWindowsHookEx",
                     "CreateRemoteThread",
                     "VirtualAllocEx",
                     "OpenProcess",
                 })
        {
            Assert.DoesNotContain(forbiddenApi, source, StringComparison.Ordinal);
        }
    }

    private static string ReadSource(string relativePath)
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot.Find(), relativePath));
    }
}
