namespace GameTranslator.Application.Translation;

public sealed class TranslateRequest
{
    public TranslateRequest(
        IEnumerable<string> texts,
        string sourceLanguage,
        string targetLanguage,
        TranslatorCredentials credentials,
        TranslationProviderRequestDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);
        ArgumentNullException.ThrowIfNull(credentials);

        var textArray = texts.ToArray();
        if (textArray.Length == 0)
        {
            throw new ArgumentException("Translation request must contain at least one text item.", nameof(texts));
        }

        if (textArray.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Translation request text items must not be empty.", nameof(texts));
        }

        Texts = textArray;
        SourceLanguage = sourceLanguage.Trim();
        TargetLanguage = targetLanguage.Trim();
        Credentials = credentials;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<string> Texts { get; }

    public string SourceLanguage { get; }

    public string TargetLanguage { get; }

    public TranslatorCredentials Credentials { get; }

    public TranslationProviderRequestDiagnostics? Diagnostics { get; }

    public override string ToString()
    {
        return $"{nameof(TranslateRequest)} {{ TextCount = {Texts.Count}, SourceLanguage = {SourceLanguage}, TargetLanguage = {TargetLanguage}, Credentials = {Credentials} }}";
    }
}
