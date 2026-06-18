namespace GameTranslator.Application.Translation;

public sealed class TranslateResponse
{
    public TranslateResponse(IEnumerable<string> translatedTexts, DateTimeOffset translatedAt)
    {
        ArgumentNullException.ThrowIfNull(translatedTexts);

        var textArray = translatedTexts.ToArray();
        if (textArray.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Translated text items must not be empty.", nameof(translatedTexts));
        }

        TranslatedTexts = textArray;
        TranslatedAt = translatedAt;
    }

    public IReadOnlyList<string> TranslatedTexts { get; }

    public DateTimeOffset TranslatedAt { get; }
}
