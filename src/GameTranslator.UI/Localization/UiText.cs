using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameTranslator.UI.Localization;

public static class UiText
{
    public sealed record Entry(string Key, string Source, string Ru, string En);
    private static readonly Entry[] entries = Load();
    private static readonly Dictionary<string, Entry> byKey = entries.ToDictionary(item => item.Key);
    private static readonly Dictionary<string, Entry> bySource = entries.GroupBy(item => item.Source).ToDictionary(group => group.Key, group => group.First());
    private static readonly (Entry Entry, Regex Pattern)[] patterns = entries.Where(item => Regex.IsMatch(item.Source, @"\{\d+\}"))
        .OrderByDescending(item => Regex.Replace(item.Source, @"\{\d+\}", string.Empty).Length)
        .Select(item => (item, new Regex("^" + Regex.Replace(Regex.Escape(item.Source), @"\\\{(\d+)}", match => "(?<p" + match.Groups[1].Value + ">.*?)") + "$", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))))
        .ToArray();

    public static string SelectLanguage(string? cultureName) => cultureName?.Split('-')[0].Equals("ru", StringComparison.OrdinalIgnoreCase) == true ? "ru" : "en";
    private static bool Russian => SelectLanguage(CultureInfo.CurrentUICulture.Name) == "ru";
    public static IReadOnlyList<Entry> Entries => entries;
    public static void InitializeFromWindows()
    {
        var language = CultureInfo.GetCultureInfo(GetUserDefaultUILanguage()).Name;
        var culture = CultureInfo.GetCultureInfo(SelectLanguage(language) == "ru" ? "ru-RU" : "en-US");
        // UI language only; keep the user's numeric and date input culture.
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
    public static string Get(string key) => byKey.TryGetValue(key, out var entry) ? (Russian ? entry.Ru : entry.En) : throw new ArgumentException("Unknown UI text key", nameof(key));
    public static string Translate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        if (bySource.TryGetValue(value, out var entry)) return Russian ? entry.Ru : entry.En;
        foreach (var (candidate, pattern) in patterns)
        {
            try
            {
                var match = pattern.Match(value);
                if (match.Success) return Regex.Replace(Russian ? candidate.Ru : candidate.En, @"\{(\d+)\}", token => match.Groups["p" + token.Groups[1].Value].Value);
            }
            catch (RegexMatchTimeoutException) { }
        }
        if (value.Contains('\n')) return string.Join("\n", value.Split('\n').Select(line => Translate(line.TrimEnd('\r'))));
        return value;
    }
    private static Entry[] Load()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("GameTranslator.UI.Localization.Strings.json") ?? throw new InvalidOperationException("UI string catalog is missing.");
        return JsonSerializer.Deserialize<Entry[]>(stream) ?? throw new InvalidOperationException("UI string catalog is empty.");
    }
    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();
}
