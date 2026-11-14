using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GameTranslator.Application.Ocr;

namespace GameTranslator.Tests.Smoke;

public sealed class LocalizationTests
{
    [Fact]
    public void Catalog_CoversEveryXamlTextKeyAndOcrLanguage()
    {
        var root = Path.Combine(RepositoryRoot.Find(), "src/GameTranslator.UI");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Localization/Strings.json")));
        var keys = document.RootElement.EnumerateArray().Select(e => e.GetProperty("Key").GetString()).ToHashSet();
        var sources = document.RootElement.EnumerateArray().Select(e => e.GetProperty("Source").GetString()).ToHashSet();
        foreach (var path in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(path), @"\{loc:Text\s+([^}]+)\}"))
                Assert.Contains(match.Groups[1].Value, keys);
        }
        foreach (var language in TesseractLanguageCatalog.Languages)
            Assert.Contains(language.Name, sources);
    }

    [Fact]
    public void ValidationTooltips_UseLocalization_AndUserNamesRemainUnconverted()
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot.Find(), "src/GameTranslator.UI/Views/ShellView.xaml"));
        var attributes = document.Descendants().Attributes().ToArray();
        var errors = attributes.Where(a => a.Value.Contains("(Validation.Errors)[0].ErrorContent")).ToArray();
        Assert.NotEmpty(errors);
        Assert.All(errors, a => Assert.Contains("Converter={StaticResource LocalizedText}", a.Value));
        Assert.All(attributes.Where(a => a.Value is "{Binding Name}" or "{Binding SelectedZone.DisplayName}"),
            a => Assert.DoesNotContain("LocalizedText", a.Value));
    }

    private static Type Localizer => AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(RepositoryRoot.Find(), "src", "GameTranslator.UI", "bin",
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug",
        "net9.0-windows10.0.19041.0", "GameTranslator.UI.dll")).GetType("GameTranslator.UI.Localization.UiText", true)!;

    [Theory]
    [InlineData("ru-RU", "ru")]
    [InlineData("ru-BY", "ru")]
    [InlineData("RU", "ru")]
    [InlineData("en-US", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("de-DE", "en")]
    [InlineData("ja-JP", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void SystemLanguage_FallsBackToEnglish(string? culture, string expected) =>
        Assert.Equal(expected, Localizer.GetMethod("SelectLanguage")!.Invoke(null, new object?[] { culture }));

    [Theory]
    [InlineData("ru-RU")]
    [InlineData("en-US")]
    public void Catalog_TranslatesEveryTemplateWithoutChangingInsertedUserContent(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "src/GameTranslator.UI/Localization/Strings.json")));
            var keys = new HashSet<string>();
            var sources = new HashSet<string>();
            var method = Localizer.GetMethod("Translate")!;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                Assert.True(keys.Add(item.GetProperty("Key").GetString()!));
                var source = item.GetProperty("Source").GetString()!;
                Assert.True(sources.Add(source));
                var ru = item.GetProperty("Ru").GetString()!;
                var en = item.GetProperty("En").GetString()!;
                Assert.False(string.IsNullOrWhiteSpace(ru));
                Assert.False(string.IsNullOrWhiteSpace(en));
                string[] Tokens(string text) => Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order().ToArray();
                Assert.Equal(Tokens(source), Tokens(ru));
                Assert.Equal(Tokens(source), Tokens(en));
                string Fill(string text) => Regex.Replace(text, @"\{(\d+)\}", m => @"C:\Мой профиль\Report-" + m.Groups[1].Value);
                Assert.Equal(Fill(culture == "ru-RU" ? ru : en), method.Invoke(null, new object[] { Fill(source) }));
            }
            Assert.Equal("Мой профиль 日本語", method.Invoke(null, new object[] { "Мой профиль 日本語" }));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
