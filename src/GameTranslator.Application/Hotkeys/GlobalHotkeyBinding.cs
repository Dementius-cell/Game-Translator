namespace GameTranslator.Application.Hotkeys;

public sealed record GlobalHotkeyBinding(
    GlobalHotkeyAction Action,
    GlobalHotkeyGesture Gesture,
    bool IsEnabled = true);
