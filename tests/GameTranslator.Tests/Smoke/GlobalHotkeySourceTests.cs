using System.IO;

namespace GameTranslator.Tests.Smoke;

public sealed class GlobalHotkeySourceTests
{
    [Fact]
    public void WpfGlobalHotkeyRegistrar_UsesDocumentedWin32HotkeyApis()
    {
        var source = ReadSource("src/GameTranslator.UI/Services/WpfGlobalHotkeyRegistrar.cs");

        Assert.Contains("RegisterHotKey", source, StringComparison.Ordinal);
        Assert.Contains("UnregisterHotKey", source, StringComparison.Ordinal);
        Assert.Contains("WmHotkey = 0x0312", source, StringComparison.Ordinal);
        Assert.Contains("GetLastWin32Error", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetWindowsHookEx", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadProcessMemory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteProcessMemory", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellView_RendersGlobalHotkeyConfigurationControls()
    {
        var source = ReadSource("src/GameTranslator.UI/Views/ShellView.xaml");

        Assert.Contains("Global hotkeys", source, StringComparison.Ordinal);
        Assert.Contains("HotkeyBindings", source, StringComparison.Ordinal);
        Assert.Contains("ApplyGlobalHotkeysCommand", source, StringComparison.Ordinal);
        Assert.Contains("ResetGlobalHotkeysCommand", source, StringComparison.Ordinal);
        Assert.Contains("GlobalHotkeyStatus", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellView_RendersManualDebugInfoExportControls()
    {
        var source = ReadSource("src/GameTranslator.UI/Views/ShellView.xaml");

        Assert.Contains("CollectDebugInfoCommand", source, StringComparison.Ordinal);
        Assert.Contains("Collect debug info", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellView_UsesLiveStatusInsteadOfAStaticReleaseLabel()
    {
        var source = ReadSource("src/GameTranslator.UI/Views/ShellView.xaml");
        var viewModelSource = ReadSource("src/GameTranslator.UI/ViewModels/ShellViewModel.cs");

        Assert.Contains("Text=\"{Binding StatusMessage}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentStage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Ready\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentStage", viewModelSource, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath)
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot.Find(), relativePath));
    }
}
