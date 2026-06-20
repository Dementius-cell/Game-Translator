namespace GameTranslator.Application.Hotkeys;

[Flags]
public enum GlobalHotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}
