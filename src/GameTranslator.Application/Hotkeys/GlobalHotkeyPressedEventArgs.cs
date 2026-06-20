namespace GameTranslator.Application.Hotkeys;

public sealed class GlobalHotkeyPressedEventArgs : EventArgs
{
    public GlobalHotkeyPressedEventArgs(GlobalHotkeyAction action)
    {
        Action = action;
    }

    public GlobalHotkeyAction Action { get; }
}
