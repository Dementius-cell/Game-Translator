namespace GameTranslator.Domain.Profiles;

public sealed record TranslatorSettings
{
    public static TranslatorSettings Default { get; } = new();

    public string Provider { get; init; } = string.Empty;

    public string SourceLanguage { get; init; } = string.Empty;

    // Null preserves profiles saved before the independent Auto switch existed.
    public bool? AutoDetectSourceLanguage { get; init; }

    public string ResolveSourceLanguage() => AutoDetectSourceLanguage == true ? "auto" : SourceLanguage;

    public static string? GetAutoConfigurationError(bool enabled, string provider) => enabled
        && !new[] { "GoogleWeb", "BingWeb", "YandexWeb" }.Contains(provider, StringComparer.OrdinalIgnoreCase)
            ? "Automatic translation source language currently supports GoogleWeb, BingWeb and YandexWeb."
            : null;

    public string TargetLanguage { get; init; } = string.Empty;
}
