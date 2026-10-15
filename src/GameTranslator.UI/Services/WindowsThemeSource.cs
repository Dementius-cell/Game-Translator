using Microsoft.Win32;

namespace GameTranslator.UI.Services;

public interface IWindowsThemeSource
{
    bool IsDark { get; }
    event EventHandler? Changed;
}

public sealed class WindowsThemeSource : IWindowsThemeSource, IDisposable
{
    public WindowsThemeSource() => SystemEvents.UserPreferenceChanged += OnPreferenceChanged;

    public bool IsDark
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
            {
                return false;
            }
        }
    }

    public event EventHandler? Changed;
    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
}
