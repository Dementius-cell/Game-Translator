namespace GameTranslator.Application.Hotkeys;

public sealed class GlobalHotkeyRegisteredEventArgs : EventArgs
{
    public GlobalHotkeyRegisteredEventArgs(int id)
    {
        Id = id;
    }

    public int Id { get; }
}
