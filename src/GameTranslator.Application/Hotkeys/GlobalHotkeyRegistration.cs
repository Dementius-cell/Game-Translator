namespace GameTranslator.Application.Hotkeys;

public sealed record GlobalHotkeyRegistration(
    int Id,
    GlobalHotkeyAction Action,
    GlobalHotkeyGesture Gesture);
