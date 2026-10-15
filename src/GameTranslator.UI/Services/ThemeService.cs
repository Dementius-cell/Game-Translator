using System.Windows;
using System.Windows.Threading;
using GameTranslator.Application.Abstractions;
using GameTranslator.UI.ViewModels;

namespace GameTranslator.UI.Services;

public enum AppThemePreference { System, Light, Dark }
public sealed record ThemeOption(AppThemePreference Mode, string DisplayName);

/// <summary>Theme resources are scoped to editor windows, never to translation overlays.</summary>
public sealed class ThemeService : ObservableObject, IDisposable
{
    public const string PreferenceKey = "ui.theme";
    private readonly ISettingsService settings;
    private readonly IWindowsThemeSource system;
    private readonly List<WeakReference<FrameworkElement>> owners = new();
    private AppThemePreference selectedMode;
    private bool disposed;

    public ThemeService(ISettingsService settings, IWindowsThemeSource system)
    {
        this.settings = settings;
        this.system = system;
        selectedMode = Enum.TryParse<AppThemePreference>(settings.GetValue<string>(PreferenceKey), out var saved)
            && Enum.IsDefined(saved) ? saved : AppThemePreference.System;
        system.Changed += OnSystemChanged;
    }

    public IReadOnlyList<ThemeOption> Options { get; } = new[]
    {
        new ThemeOption(AppThemePreference.System, "Как в Windows"),
        new ThemeOption(AppThemePreference.Light, "Светлая"),
        new ThemeOption(AppThemePreference.Dark, "Тёмная"),
    };

    public AppThemePreference SelectedMode
    {
        get => selectedMode;
        set
        {
            if (!Enum.IsDefined(value) || !SetProperty(ref selectedMode, value)) return;
            settings.SetValue(PreferenceKey, value.ToString());
            Refresh();
        }
    }

    public bool IsDark => SelectedMode == AppThemePreference.Dark
        || (SelectedMode == AppThemePreference.System && system.IsDark);

    public void Attach(FrameworkElement owner)
    {
        lock (owners)
        {
            if (!owners.Any(reference => reference.TryGetTarget(out var existing) && ReferenceEquals(owner, existing)))
                owners.Add(new WeakReference<FrameworkElement>(owner));
        }
        Apply(owner);
    }

    private void OnSystemChanged(object? sender, EventArgs e)
    {
        if (SelectedMode == AppThemePreference.System) Refresh();
    }

    private void Refresh()
    {
        if (disposed) return;
        OnPropertyChanged(nameof(IsDark));
        WeakReference<FrameworkElement>[] snapshot;
        lock (owners) snapshot = owners.ToArray();
        foreach (var reference in snapshot)
        {
            if (!reference.TryGetTarget(out var owner)) { lock (owners) owners.Remove(reference); continue; }
            if (owner.Dispatcher.HasShutdownStarted) continue;
            if (owner.Dispatcher.CheckAccess()) Apply(owner);
            else owner.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { if (!disposed) Apply(owner); }));
        }
    }

    private void Apply(FrameworkElement owner)
    {
        var mode = IsDark ? "Dark" : "Light";
        var dictionaries = owner.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = new Uri($"/GameTranslator.UI;component/Themes/{mode}.xaml", UriKind.Relative) };
        var fluent = new ResourceDictionary { Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{mode}.xaml") };
        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            var source = dictionaries[index].Source?.OriginalString ?? string.Empty;
            if (source.Contains("/Themes/Light.xaml", StringComparison.Ordinal) || source.Contains("/Themes/Dark.xaml", StringComparison.Ordinal)
                || source.Contains("PresentationFramework.Fluent", StringComparison.Ordinal)) dictionaries.RemoveAt(index);
        }
        dictionaries.Insert(0, fluent);
        dictionaries.Add(palette);
        if (owner is Window window)
        {
            // .NET 9 marks only the API experimental; keep its use scoped to editor windows.
#pragma warning disable WPF0001
            window.ThemeMode = IsDark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        }
    }

    public void Dispose()
    {
        disposed = true;
        system.Changed -= OnSystemChanged;
        lock (owners) owners.Clear();
    }
}
