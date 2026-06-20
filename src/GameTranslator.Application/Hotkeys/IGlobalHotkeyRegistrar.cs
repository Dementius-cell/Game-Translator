namespace GameTranslator.Application.Hotkeys;

public interface IGlobalHotkeyRegistrar
{
    event EventHandler<GlobalHotkeyRegisteredEventArgs>? HotkeyPressed;

    GlobalHotkeyRegistrationResult Register(GlobalHotkeyRegistration registration);

    void Unregister(int id);

    void UnregisterAll();
}
