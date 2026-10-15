using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Media;
using GameTranslator.Application.Abstractions;

namespace GameTranslator.Tests.Smoke;

[Collection(WpfResourceCollection.Name)]
public sealed class ThemeServiceTests
{
    [Theory]
    [InlineData(null, true, "System", true)]
    [InlineData("obsolete", false, "System", false)]
    [InlineData("99", true, "System", true)]
    [InlineData("Light", true, "Light", false)]
    [InlineData("Dark", false, "Dark", true)]
    public void Preference_UsesSystemByDefaultAndRestoresExplicitChoice(string? saved, bool systemDark, string mode, bool expectedDark)
    {
        var assembly = LoadUi();
        var settings = new SettingsStub();
        settings.SetValue("ui.theme", saved);
        var source = CreateSource(assembly, systemDark);
        using var service = (IDisposable)Activator.CreateInstance(assembly.GetType("GameTranslator.UI.Services.ThemeService", true)!, settings, source)!;
        Assert.Equal(mode, Get(service, "SelectedMode")!.ToString());
        Assert.Equal(expectedDark, Get(service, "IsDark"));
    }

    [Fact]
    public void SystemChanges_UpdateAttachedResources_ManualOverridePersists_AndDisposeUnsubscribes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var assembly = LoadUi();
                var settings = new SettingsStub();
                var source = CreateSource(assembly, false);
                using var service = (IDisposable)Activator.CreateInstance(assembly.GetType("GameTranslator.UI.Services.ThemeService", true)!, settings, source)!;
                var owner = new System.Windows.Controls.Border();
                service.GetType().GetMethod("Attach")!.Invoke(service, new[] { owner });
                var editorWindow = new Window();
                var unrelatedWindow = new Window();
                service.GetType().GetMethod("Attach")!.Invoke(service, new[] { editorWindow });
                var light = ((SolidColorBrush)owner.FindResource("ThemePage")).Color;
                Task.Run(() => { source.IsDark = true; source.Notify(); }).GetAwaiter().GetResult();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var dark = ((SolidColorBrush)owner.FindResource("ThemePage")).Color;
                Assert.NotEqual(light, dark);
                Assert.Equal(dark, ((SolidColorBrush)editorWindow.FindResource("ThemePage")).Color);
                Assert.Empty(unrelatedWindow.Resources.MergedDictionaries);
                Assert.Equal(true, Get(service, "IsDark"));
                SetMode(service, "Light");
                Assert.Equal("Light", settings.GetValue<string>("ui.theme"));
                source.Notify();
                Assert.Equal(light, ((SolidColorBrush)owner.FindResource("ThemePage")).Color);
                SetMode(service, "System");
                Assert.Equal(dark, ((SolidColorBrush)owner.FindResource("ThemePage")).Color);
                source.IsDark = false;
                source.Notify();
                Assert.Equal(light, ((SolidColorBrush)owner.FindResource("ThemePage")).Color);
                Assert.Equal(2, owner.Resources.MergedDictionaries.Count);
                service.Dispose();
                Assert.Null(source.Subscribers);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }

    private static object? Get(object service, string property) => service.GetType().GetProperty(property)!.GetValue(service);
    private static void SetMode(object service, string value)
    {
        var property = service.GetType().GetProperty("SelectedMode")!;
        property.SetValue(service, Enum.Parse(property.PropertyType, value));
    }
    private static ThemeSourceProxy CreateSource(Assembly assembly, bool dark)
    {
        var source = (ThemeSourceProxy)DispatchProxy.Create(assembly.GetType("GameTranslator.UI.Services.IWindowsThemeSource", true)!, typeof(ThemeSourceProxy));
        source.IsDark = dark;
        return source;
    }
    private static Assembly LoadUi() => AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(RepositoryRoot.Find(), "src", "GameTranslator.UI", "bin",
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug",
        "net9.0-windows10.0.19041.0", "GameTranslator.UI.dll"));

    public class ThemeSourceProxy : DispatchProxy
    {
        public bool IsDark { get; set; }
        public EventHandler? Subscribers { get; private set; }
        public void Notify() => Subscribers?.Invoke(this, EventArgs.Empty);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case "get_IsDark": return IsDark;
                case "add_Changed": Subscribers += (EventHandler)args![0]!; break;
                case "remove_Changed": Subscribers -= (EventHandler)args![0]!; break;
                default: throw new NotSupportedException(targetMethod.Name);
            }
            return null;
        }
    }
    private sealed class SettingsStub : ISettingsService
    {
        private readonly Dictionary<string, object?> values = new();
        public TValue? GetValue<TValue>(string key) => values.TryGetValue(key, out var value) ? (TValue?)value : default;
        public void SetValue<TValue>(string key, TValue? value) => values[key] = value;
    }
}
