using System.Text.RegularExpressions;
using GameTranslator.Application.Translation;

namespace GameTranslator.Application.Content;

/// <summary>
/// Keeps recognized values and explicit key hints authoritative for menu labels.
/// This checks translation output, not OCR correctness.
/// </summary>
public static partial class MenuTranslationIntegrity
{
    public const string Warning = "Menu translation retained the source because a number or key hint changed.";

    public static TranslateResponse Validate(IReadOnlyList<string> sources, TranslateResponse response)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(response);
        if (sources.Count != response.TranslatedTexts.Count)
            throw new ArgumentException("Menu translation count must match source count.", nameof(response));

        var retained = false;
        var texts = response.TranslatedTexts.Select((translated, index) =>
        {
            if (IsStandaloneControlHint(sources[index])
                ? string.Equals(sources[index].Trim(), translated.Trim(), StringComparison.Ordinal)
                : Tokens(sources[index]).SequenceEqual(Tokens(translated), StringComparer.Ordinal))
                return translated;
            retained = true;
            return sources[index];
        }).ToArray();
        return retained
            ? new TranslateResponse(texts, response.TranslatedAt, response.ProviderId,
                string.Join(" ", new[] { response.DiagnosticMessage, Warning }.Where(s => s.Length > 0)))
            : response;
    }

    private static IEnumerable<string> Tokens(string text) =>
        ProtectedTokens().Matches(text).Select(match => match.Value);

    public static bool IsNumericDisplay(string text) => NumericDisplay().IsMatch(text.Trim());

    // Match the entire element only: these letters inside prose are ordinary text.
    public static bool IsStandaloneControlHint(string text) => StandaloneControlHint().IsMatch(text.Trim());

    [GeneratedRegex(@"^(?:[ABXYEFWSD]|LB|RB|LT|RT|L1|L2|L3|R1|R2|R3|Esc|Enter|Shift|Ctrl|Alt|Tab|Space|Backspace|[Ff](?:[1-9]|1[0-2]))$", RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneControlHint();

    [GeneratedRegex(@"\[[A-Za-z0-9+ ]{1,24}\]|(?<!\w)[xX×]\s?\d+|[+\-−]?\d+(?:[.,:/]\d+)*(?:\s?(?:[%％]|(?:[KMGT]i?[Bb]|Hz|MHz|GHz|FPS|ms)\b))?", RegexOptions.CultureInvariant)]
    private static partial Regex ProtectedTokens();

    [GeneratedRegex(@"^[+\-−]?\d+(?:\s*[.,:/*×+\-]\s*\d+)*(?:\s?(?:%|％|[KMGT]i?[Bb]|Hz|MHz|GHz|FPS|ms))?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericDisplay();
}
