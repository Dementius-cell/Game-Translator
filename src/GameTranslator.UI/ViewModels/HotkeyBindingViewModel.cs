using GameTranslator.Application.Hotkeys;

namespace GameTranslator.UI.ViewModels;

public sealed class HotkeyBindingViewModel : ValidatableObservableObject
{
    private string gestureText;
    private bool isEnabled;

    private HotkeyBindingViewModel(GlobalHotkeyAction action, string displayName, string gestureText, bool isEnabled)
    {
        Action = action;
        DisplayName = displayName;
        this.gestureText = gestureText;
        this.isEnabled = isEnabled;
        RefreshValidationState();
    }

    public GlobalHotkeyAction Action { get; }

    public string DisplayName { get; }

    public string GestureText
    {
        get => gestureText;
        set
        {
            if (SetProperty(ref gestureText, value))
            {
                RefreshValidationState();
            }
        }
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (SetProperty(ref isEnabled, value))
            {
                RefreshValidationState();
            }
        }
    }

    public string Summary => IsEnabled ? GestureText : "Disabled";

    public GlobalHotkeyBinding ToModel()
    {
        if (!IsEnabled)
        {
            return new GlobalHotkeyBinding(Action, new GlobalHotkeyGesture(GlobalHotkeyModifiers.None, "F24"), false);
        }

        if (!GlobalHotkeyGesture.TryParse(GestureText, out var gesture) || gesture is null)
        {
            throw new InvalidOperationException($"Hotkey '{GestureText}' is invalid.");
        }

        return new GlobalHotkeyBinding(Action, gesture, true);
    }

    public static HotkeyBindingViewModel FromModel(GlobalHotkeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return new HotkeyBindingViewModel(
            binding.Action,
            GetDisplayName(binding.Action),
            binding.Gesture.DisplayText,
            binding.IsEnabled);
    }

    private void RefreshValidationState()
    {
        SetErrors(
            nameof(GestureText),
            IsEnabled && !GlobalHotkeyGesture.TryParse(GestureText, out _)
                ? new[] { "Use a hotkey like Ctrl+Alt+T." }
                : Array.Empty<string>());
        OnPropertyChanged(nameof(Summary));
    }

    private static string GetDisplayName(GlobalHotkeyAction action)
    {
        return action switch
        {
            GlobalHotkeyAction.StartPausePipeline => "Start / pause",
            GlobalHotkeyAction.ToggleOverlay => "Show / hide overlay",
            GlobalHotkeyAction.ShowSettings => "Settings",
            GlobalHotkeyAction.ExitApplication => "Exit",
            _ => action.ToString(),
        };
    }
}
