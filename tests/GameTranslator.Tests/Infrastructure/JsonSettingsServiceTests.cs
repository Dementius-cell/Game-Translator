using System.IO;
using GameTranslator.Application.Hotkeys;
using GameTranslator.Infrastructure.Settings;

namespace GameTranslator.Tests.Infrastructure;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string workingDirectory = Path.Combine(
        Path.GetTempPath(),
        "GameTranslator.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SetValue_PersistsValueAcrossServiceInstances()
    {
        var settingsFilePath = Path.Combine(workingDirectory, "settings.json");
        var writer = new JsonSettingsService(settingsFilePath);

        writer.SetValue("profiles.selectedId", "profile-42");

        var reader = new JsonSettingsService(settingsFilePath);

        Assert.Equal("profile-42", reader.GetValue<string>("profiles.selectedId"));
    }

    [Fact]
    public void SetValue_WithNull_RemovesStoredValue()
    {
        var settingsFilePath = Path.Combine(workingDirectory, "settings.json");
        var service = new JsonSettingsService(settingsFilePath);

        service.SetValue("profiles.selectedId", "profile-42");
        service.SetValue<string?>("profiles.selectedId", null);

        Assert.Null(service.GetValue<string>("profiles.selectedId"));
        Assert.Equal("{}", File.ReadAllText(settingsFilePath).Trim());
    }


    [Fact]
    public void SetValue_PersistsGlobalHotkeyBindingsAcrossServiceInstances()
    {
        var settingsFilePath = Path.Combine(workingDirectory, "settings.json");
        var writer = new JsonSettingsService(settingsFilePath);
        var bindings = new[]
        {
            new GlobalHotkeyBinding(
                GlobalHotkeyAction.ToggleOverlay,
                new GlobalHotkeyGesture(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt, "O")),
        };

        writer.SetValue("hotkeys.bindings.v1", bindings);

        var reader = new JsonSettingsService(settingsFilePath);
        var restored = reader.GetValue<GlobalHotkeyBinding[]>("hotkeys.bindings.v1");

        var binding = Assert.Single(restored!);
        Assert.Equal(GlobalHotkeyAction.ToggleOverlay, binding.Action);
        Assert.Equal(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt, binding.Gesture.Modifiers);
        Assert.Equal("O", binding.Gesture.Key);
    }
    public void Dispose()
    {
        if (Directory.Exists(workingDirectory))
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }
}
