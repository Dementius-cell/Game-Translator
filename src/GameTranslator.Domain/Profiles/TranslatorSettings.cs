namespace GameTranslator.Domain.Profiles;

public sealed record TranslatorSettings
{
    public static TranslatorSettings Default { get; } = new();

    public string Provider { get; init; } = string.Empty;

    public string SourceLanguage { get; init; } = string.Empty;

    public string TargetLanguage { get; init; } = string.Empty;
}
