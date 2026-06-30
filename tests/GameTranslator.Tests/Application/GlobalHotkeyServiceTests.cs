using GameTranslator.Application.Abstractions;
using GameTranslator.Application.Hotkeys;

namespace GameTranslator.Tests.Application;

public sealed class GlobalHotkeyServiceTests
{
    [Fact]
    public void LoadConfiguredHotkeys_WhenSettingsAreEmpty_ReturnsDefaults()
    {
        var service = new GlobalHotkeyService(new TestSettingsService(), new FakeGlobalHotkeyRegistrar());

        var bindings = service.LoadConfiguredHotkeys();

        Assert.Contains(bindings, binding => binding.Action == GlobalHotkeyAction.StartPausePipeline);
        var recognizeOcrBinding = Assert.Single(bindings, binding => binding.Action == GlobalHotkeyAction.RecognizeOcrPreview);
        Assert.Equal("Ctrl+Shift+F8", recognizeOcrBinding.Gesture.DisplayText);
        Assert.Contains(bindings, binding => binding.Action == GlobalHotkeyAction.ToggleOverlay);
        var exportDiagnosticsBinding = Assert.Single(bindings, binding => binding.Action == GlobalHotkeyAction.ExportDiagnostics);
        Assert.Equal("Ctrl+Shift+F9", exportDiagnosticsBinding.Gesture.DisplayText);
        Assert.Contains(bindings, binding => binding.Action == GlobalHotkeyAction.ShowSettings);
        Assert.Contains(bindings, binding => binding.Action == GlobalHotkeyAction.ExitApplication);
    }

    [Fact]
    public void LoadConfiguredHotkeys_WhenSettingsAreMissingNewDefaultActions_AppendsMissingDefaults()
    {
        var settings = new TestSettingsService();
        settings.SetValue(
            "hotkeys.bindings.v1",
            new[]
            {
                new GlobalHotkeyBinding(
                    GlobalHotkeyAction.StartPausePipeline,
                    new GlobalHotkeyGesture(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt, "T")),
                new GlobalHotkeyBinding(
                    GlobalHotkeyAction.ToggleOverlay,
                    new GlobalHotkeyGesture(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt, "O")),
                new GlobalHotkeyBinding(
                    GlobalHotkeyAction.ShowSettings,
                    new GlobalHotkeyGesture(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt, "S")),
                new GlobalHotkeyBinding(
                    GlobalHotkeyAction.ExitApplication,
                    new GlobalHotkeyGesture(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt, "Q")),
            });
        var service = new GlobalHotkeyService(settings, new FakeGlobalHotkeyRegistrar());

        var bindings = service.LoadConfiguredHotkeys();

        var recognizeOcrBinding = Assert.Single(bindings, binding => binding.Action == GlobalHotkeyAction.RecognizeOcrPreview);
        Assert.Equal("Ctrl+Shift+F8", recognizeOcrBinding.Gesture.DisplayText);
        var exportDiagnosticsBinding = Assert.Single(bindings, binding => binding.Action == GlobalHotkeyAction.ExportDiagnostics);
        Assert.Equal("Ctrl+Shift+F9", exportDiagnosticsBinding.Gesture.DisplayText);
        Assert.Equal(6, bindings.Count);
    }

    [Fact]
    public void LoadConfiguredHotkeys_WhenRecognizeOcrUsesPreviousDefault_MigratesToCurrentDefault()
    {
        var settings = new TestSettingsService();
        settings.SetValue(
            "hotkeys.bindings.v1",
            new[]
            {
                new GlobalHotkeyBinding(
                    GlobalHotkeyAction.RecognizeOcrPreview,
                    new GlobalHotkeyGesture(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt, "R")),
            });
        var service = new GlobalHotkeyService(settings, new FakeGlobalHotkeyRegistrar());

        var bindings = service.LoadConfiguredHotkeys();

        var recognizeOcrBinding = Assert.Single(bindings, binding => binding.Action == GlobalHotkeyAction.RecognizeOcrPreview);
        Assert.Equal("Ctrl+Shift+F8", recognizeOcrBinding.Gesture.DisplayText);
    }

    [Fact]
    public void RegisterHotkeys_WhenTwoActionsUseSameGesture_SurfacesDuplicateConflict()
    {
        var registrar = new FakeGlobalHotkeyRegistrar();
        var service = new GlobalHotkeyService(new TestSettingsService(), registrar);
        var gesture = new GlobalHotkeyGesture(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt, "T");

        var result = service.RegisterHotkeys(new[]
        {
            new GlobalHotkeyBinding(GlobalHotkeyAction.StartPausePipeline, gesture),
            new GlobalHotkeyBinding(GlobalHotkeyAction.ToggleOverlay, gesture),
        });

        Assert.True(result.HasConflicts);
        Assert.Empty(registrar.Registered);
        Assert.All(result.Statuses, status => Assert.False(status.IsRegistered));
        Assert.Contains("Duplicate hotkey", result.Statuses[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterHotkeys_WhenRegistrarFails_SurfacesConflictWithErrorCode()
    {
        var registrar = new FakeGlobalHotkeyRegistrar
        {
            Failure = GlobalHotkeyRegistrationResult.Failure("Already registered.", 1409),
        };
        var service = new GlobalHotkeyService(new TestSettingsService(), registrar);

        var result = service.RegisterHotkeys(new[]
        {
            new GlobalHotkeyBinding(
                GlobalHotkeyAction.ToggleOverlay,
                new GlobalHotkeyGesture(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt, "O")),
        });

        var status = Assert.Single(result.Statuses);
        Assert.True(result.HasConflicts);
        Assert.False(status.IsRegistered);
        Assert.Equal(1409, status.ErrorCode);
        Assert.Equal("Already registered.", status.Message);
    }

    [Fact]
    public void RegisterHotkeys_WhenRegisteredIdIsRaised_PublishesConfiguredAction()
    {
        var registrar = new FakeGlobalHotkeyRegistrar();
        var service = new GlobalHotkeyService(new TestSettingsService(), registrar);
        GlobalHotkeyAction? pressedAction = null;
        service.HotkeyPressed += (_, e) => pressedAction = e.Action;
        service.RegisterHotkeys(new[]
        {
            new GlobalHotkeyBinding(
                GlobalHotkeyAction.ShowSettings,
                new GlobalHotkeyGesture(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt, "S")),
        });

        registrar.RaisePressed(registrar.Registered.Single().Id);

        Assert.Equal(GlobalHotkeyAction.ShowSettings, pressedAction);
    }

    private sealed class FakeGlobalHotkeyRegistrar : IGlobalHotkeyRegistrar
    {
        public event EventHandler<GlobalHotkeyRegisteredEventArgs>? HotkeyPressed;

        public List<GlobalHotkeyRegistration> Registered { get; } = new();

        public GlobalHotkeyRegistrationResult? Failure { get; init; }

        public GlobalHotkeyRegistrationResult Register(GlobalHotkeyRegistration registration)
        {
            if (Failure is not null)
            {
                return Failure;
            }

            Registered.Add(registration);
            return GlobalHotkeyRegistrationResult.Success();
        }

        public void Unregister(int id)
        {
            Registered.RemoveAll(registration => registration.Id == id);
        }

        public void UnregisterAll()
        {
            Registered.Clear();
        }

        public void RaisePressed(int id)
        {
            HotkeyPressed?.Invoke(this, new GlobalHotkeyRegisteredEventArgs(id));
        }
    }

    private sealed class TestSettingsService : ISettingsService
    {
        private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);

        public TValue? GetValue<TValue>(string key)
        {
            return values.TryGetValue(key, out var value)
                ? (TValue?)value
                : default;
        }

        public void SetValue<TValue>(string key, TValue? value)
        {
            values[key] = value;
        }
    }
}
