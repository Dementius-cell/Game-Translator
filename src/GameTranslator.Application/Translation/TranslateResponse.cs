namespace GameTranslator.Application.Translation;

public sealed class TranslateResponse
{
    public TranslateResponse(
        IEnumerable<string> translatedTexts,
        DateTimeOffset translatedAt,
        string providerId = "",
        string diagnosticMessage = "")
    {
        ArgumentNullException.ThrowIfNull(translatedTexts);

        var textArray = translatedTexts.ToArray();
        if (textArray.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Translated text items must not be empty.", nameof(translatedTexts));
        }

        TranslatedTexts = textArray;
        TranslatedAt = translatedAt;
        ProviderId = providerId?.Trim() ?? string.Empty;
        DiagnosticMessage = diagnosticMessage?.Trim() ?? string.Empty;
    }

    public IReadOnlyList<string> TranslatedTexts { get; }

    public DateTimeOffset TranslatedAt { get; }

    public string ProviderId { get; }

    public string DiagnosticMessage { get; }
}
