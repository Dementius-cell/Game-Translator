namespace GameTranslator.Application.Hotkeys;

public sealed record GlobalHotkeyGesture
{
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    public GlobalHotkeyGesture(GlobalHotkeyModifiers modifiers, string key, bool noRepeat = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        Modifiers = modifiers;
        Key = NormalizeKey(key);
        NoRepeat = noRepeat;
    }

    public GlobalHotkeyModifiers Modifiers { get; }

    public string Key { get; }

    public bool NoRepeat { get; }

    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(GlobalHotkeyModifiers.Control))
            {
                parts.Add("Ctrl");
            }

            if (Modifiers.HasFlag(GlobalHotkeyModifiers.Alt))
            {
                parts.Add("Alt");
            }

            if (Modifiers.HasFlag(GlobalHotkeyModifiers.Shift))
            {
                parts.Add("Shift");
            }

            if (Modifiers.HasFlag(GlobalHotkeyModifiers.Windows))
            {
                parts.Add("Win");
            }

            parts.Add(Key);

            return string.Join("+", parts);
        }
    }

    public bool HasSameChord(GlobalHotkeyGesture other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Modifiers == other.Modifiers && KeyComparer.Equals(Key, other.Key);
    }

    public static bool TryParse(string? value, out GlobalHotkeyGesture? gesture)
    {
        gesture = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var tokens = value
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
        if (tokens.Length == 0)
        {
            return false;
        }

        var modifiers = GlobalHotkeyModifiers.None;
        string? key = null;

        foreach (var token in tokens)
        {
            if (IsModifier(token, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (key is not null)
            {
                return false;
            }

            key = token;
        }

        if (key is null || IsModifier(key, out _))
        {
            return false;
        }

        gesture = new GlobalHotkeyGesture(modifiers, key);
        return true;
    }

    private static bool IsModifier(string value, out GlobalHotkeyModifiers modifier)
    {
        switch (value.Trim().ToUpperInvariant())
        {
            case "ALT":
                modifier = GlobalHotkeyModifiers.Alt;
                return true;
            case "CTRL":
            case "CONTROL":
                modifier = GlobalHotkeyModifiers.Control;
                return true;
            case "SHIFT":
                modifier = GlobalHotkeyModifiers.Shift;
                return true;
            case "WIN":
            case "WINDOWS":
                modifier = GlobalHotkeyModifiers.Windows;
                return true;
            default:
                modifier = GlobalHotkeyModifiers.None;
                return false;
        }
    }

    private static string NormalizeKey(string key)
    {
        var normalized = key.Trim();
        if (normalized.Length == 1)
        {
            return normalized.ToUpperInvariant();
        }

        var upper = normalized.ToUpperInvariant();
        if (upper.Length > 1 && upper[0] == 'F' && int.TryParse(upper[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            return upper;
        }

        return upper switch
        {
            "ESC" => "Escape",
            "ESCAPE" => "Escape",
            "PGUP" => "PageUp",
            "PAGEUP" => "PageUp",
            "PGDN" => "PageDown",
            "PAGEDOWN" => "PageDown",
            "PRTSC" => "PrintScreen",
            "PRINTSCREEN" => "PrintScreen",
            _ => char.ToUpperInvariant(normalized[0]) + normalized[1..],
        };
    }
}
