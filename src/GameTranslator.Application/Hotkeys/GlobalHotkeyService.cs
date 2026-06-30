using GameTranslator.Application.Abstractions;

namespace GameTranslator.Application.Hotkeys;

public sealed class GlobalHotkeyService
{
    private const string HotkeyBindingsSettingKey = "hotkeys.bindings.v1";
    private const int RegistrationIdBase = 0x4700;
    private static readonly GlobalHotkeyGesture PreviousRecognizeOcrPreviewDefault =
        new(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt, "R");

    private static readonly GlobalHotkeyBinding[] DefaultBindings =
    {
        new(GlobalHotkeyAction.StartPausePipeline, new GlobalHotkeyGesture(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt, "T")),
        new(GlobalHotkeyAction.RecognizeOcrPreview, new GlobalHotkeyGesture(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Shift, "F8")),
        new(GlobalHotkeyAction.ToggleOverlay, new GlobalHotkeyGesture(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt, "O")),
        new(GlobalHotkeyAction.ExportDiagnostics, new GlobalHotkeyGesture(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Shift, "F9")),
        new(GlobalHotkeyAction.ShowSettings, new GlobalHotkeyGesture(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt, "S")),
        new(GlobalHotkeyAction.ExitApplication, new GlobalHotkeyGesture(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Alt, "Q")),
    };

    private readonly ISettingsService settings;
    private readonly IGlobalHotkeyRegistrar registrar;
    private readonly Dictionary<int, GlobalHotkeyAction> registeredActionsById = new();

    public GlobalHotkeyService(ISettingsService settings, IGlobalHotkeyRegistrar registrar)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
        registrar.HotkeyPressed += OnRegistrarHotkeyPressed;
    }

    public event EventHandler<GlobalHotkeyPressedEventArgs>? HotkeyPressed;

    public IReadOnlyList<GlobalHotkeyBinding> DefaultHotkeys => DefaultBindings;

    public IReadOnlyList<GlobalHotkeyBinding> LoadConfiguredHotkeys()
    {
        var configured = settings.GetValue<GlobalHotkeyBinding[]>(HotkeyBindingsSettingKey);
        if (configured is null || configured.Length == 0)
        {
            return DefaultBindings;
        }

        return MergeWithDefaultBindings(configured);
    }

    public void SaveConfiguredHotkeys(IEnumerable<GlobalHotkeyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        settings.SetValue(HotkeyBindingsSettingKey, bindings.ToArray());
    }

    public GlobalHotkeyConfigurationResult RegisterConfiguredHotkeys()
    {
        return RegisterHotkeys(LoadConfiguredHotkeys());
    }

    public GlobalHotkeyConfigurationResult RegisterHotkeys(IEnumerable<GlobalHotkeyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        registrar.UnregisterAll();
        registeredActionsById.Clear();

        var statuses = new List<GlobalHotkeyRegistrationStatus>();
        var enabledBindings = bindings.Where(binding => binding.IsEnabled).ToArray();
        var duplicateGestures = FindDuplicateGestures(enabledBindings);

        for (var index = 0; index < enabledBindings.Length; index++)
        {
            var binding = enabledBindings[index];
            if (duplicateGestures.Any(gesture => gesture.HasSameChord(binding.Gesture)))
            {
                statuses.Add(new GlobalHotkeyRegistrationStatus(
                    binding,
                    false,
                    $"Duplicate hotkey {binding.Gesture.DisplayText}."));
                continue;
            }

            var registration = new GlobalHotkeyRegistration(
                RegistrationIdBase + index,
                binding.Action,
                binding.Gesture);
            var result = registrar.Register(registration);
            if (!result.Succeeded)
            {
                statuses.Add(new GlobalHotkeyRegistrationStatus(
                    binding,
                    false,
                    result.Message ?? $"Hotkey {binding.Gesture.DisplayText} could not be registered.",
                    result.ErrorCode));
                continue;
            }

            registeredActionsById[registration.Id] = registration.Action;
            statuses.Add(new GlobalHotkeyRegistrationStatus(
                binding,
                true,
                $"Registered {binding.Gesture.DisplayText}."));
        }

        return new GlobalHotkeyConfigurationResult(statuses);
    }

    public void UnregisterAll()
    {
        registrar.UnregisterAll();
        registeredActionsById.Clear();
    }

    private static GlobalHotkeyGesture[] FindDuplicateGestures(IReadOnlyList<GlobalHotkeyBinding> bindings)
    {
        var duplicates = new List<GlobalHotkeyGesture>();

        for (var first = 0; first < bindings.Count; first++)
        {
            for (var second = first + 1; second < bindings.Count; second++)
            {
                if (bindings[first].Gesture.HasSameChord(bindings[second].Gesture)
                    && !duplicates.Any(gesture => gesture.HasSameChord(bindings[first].Gesture)))
                {
                    duplicates.Add(bindings[first].Gesture);
                }
            }
        }

        return duplicates.ToArray();
    }

    private static IReadOnlyList<GlobalHotkeyBinding> MergeWithDefaultBindings(IReadOnlyList<GlobalHotkeyBinding> configured)
    {
        var merged = configured
            .Select(MigrateConfiguredBinding)
            .ToList();
        foreach (var defaultBinding in DefaultBindings)
        {
            if (merged.Any(binding => binding.Action == defaultBinding.Action))
            {
                continue;
            }

            merged.Add(defaultBinding);
        }

        return merged;
    }

    private static GlobalHotkeyBinding MigrateConfiguredBinding(GlobalHotkeyBinding binding)
    {
        if (binding.Action != GlobalHotkeyAction.RecognizeOcrPreview
            || !binding.Gesture.HasSameChord(PreviousRecognizeOcrPreviewDefault))
        {
            return binding;
        }

        var defaultBinding = DefaultBindings.Single(defaultBinding => defaultBinding.Action == binding.Action);
        return binding with { Gesture = defaultBinding.Gesture };
    }

    private void OnRegistrarHotkeyPressed(object? sender, GlobalHotkeyRegisteredEventArgs e)
    {
        if (registeredActionsById.TryGetValue(e.Id, out var action))
        {
            HotkeyPressed?.Invoke(this, new GlobalHotkeyPressedEventArgs(action));
        }
    }
}
