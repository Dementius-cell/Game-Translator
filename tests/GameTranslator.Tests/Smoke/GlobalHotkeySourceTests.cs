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
        Assert.Contains("ExportDiagnosticsCommand", source, StringComparison.Ordinal);
        Assert.Contains("DiagnosticExportStatus", source, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath)
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot.Find(), relativePath));
    }
}
