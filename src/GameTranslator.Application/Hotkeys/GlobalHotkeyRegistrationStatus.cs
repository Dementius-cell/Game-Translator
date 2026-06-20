namespace GameTranslator.Application.Hotkeys;

public sealed class GlobalHotkeyRegistrationStatus
{
    public GlobalHotkeyRegistrationStatus(
        GlobalHotkeyBinding binding,
        bool isRegistered,
        string message,
        int? errorCode = null)
    {
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        IsRegistered = isRegistered;
        Message = string.IsNullOrWhiteSpace(message)
            ? throw new ArgumentException("Hotkey registration message is required.", nameof(message))
            : message;
        ErrorCode = errorCode;
    }

    public GlobalHotkeyBinding Binding { get; }

    public bool IsRegistered { get; }

    public string Message { get; }

    public int? ErrorCode { get; }
}
