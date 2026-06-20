namespace GameTranslator.Application.Hotkeys;

public sealed class GlobalHotkeyConfigurationResult
{
    public GlobalHotkeyConfigurationResult(IReadOnlyList<GlobalHotkeyRegistrationStatus> statuses)
    {
        Statuses = statuses ?? throw new ArgumentNullException(nameof(statuses));
    }

    public IReadOnlyList<GlobalHotkeyRegistrationStatus> Statuses { get; }

    public bool HasConflicts => Statuses.Any(status => !status.IsRegistered);

    public int RegisteredCount => Statuses.Count(status => status.IsRegistered);

    public string Summary
    {
        get
        {
            if (Statuses.Count == 0)
            {
                return "No global hotkeys configured.";
            }

            if (!HasConflicts)
            {
                return $"Registered {RegisteredCount} global hotkey(s).";
            }

            return $"Registered {RegisteredCount} global hotkey(s); {Statuses.Count - RegisteredCount} conflict(s) need attention.";
        }
    }
}
