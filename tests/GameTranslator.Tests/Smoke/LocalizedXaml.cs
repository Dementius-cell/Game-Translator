using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GameTranslator.Tests.Smoke;

// Resolve catalog references to their original wording for existing structural contracts.
// Runtime localization is checked separately across actual UI cultures.
internal static class LocalizedXaml
{
    public static XDocument Load(string path) => XDocument.Parse(ReadSource(path));
    public static string ReadSource(string path)
    {
        var source = File.ReadAllText(path);
        if (!path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) return source;
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "src", "GameTranslator.UI", "Localization", "Strings.json")));
        var entries = catalog.RootElement.EnumerateArray().ToDictionary(e => e.GetProperty("Key").GetString()!, e => e.GetProperty("Source").GetString()!);
        source = Regex.Replace(source, @"\{loc:Text (T[a-f0-9]+)\}", match => System.Security.SecurityElement.Escape(entries[match.Groups[1].Value])!);
        return source.Replace(", Converter={StaticResource LocalizedText}", string.Empty);
    }
}
