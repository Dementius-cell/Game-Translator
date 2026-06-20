namespace GameTranslator.Application.Hotkeys;

public sealed class GlobalHotkeyRegistrationResult
{
    private GlobalHotkeyRegistrationResult(bool succeeded, string? message, int? errorCode)
    {
        Succeeded = succeeded;
        Message = message;
        ErrorCode = errorCode;
    }

    public bool Succeeded { get; }

    public string? Message { get; }

    public int? ErrorCode { get; }

    public static GlobalHotkeyRegistrationResult Success()
    {
        return new GlobalHotkeyRegistrationResult(true, null, null);
    }

    public static GlobalHotkeyRegistrationResult Failure(string message, int? errorCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new GlobalHotkeyRegistrationResult(false, message, errorCode);
    }
}
