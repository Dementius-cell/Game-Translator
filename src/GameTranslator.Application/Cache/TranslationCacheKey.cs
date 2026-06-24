using System.Security.Cryptography;
using System.Text;
using GameTranslator.Application.Ocr;

namespace GameTranslator.Application.Cache;

public sealed class TranslationCacheKey : IEquatable<TranslationCacheKey>
{
    public TranslationCacheKey(
        string provider,
        string sourceLanguage,
        string targetLanguage,
        string sourceText)
    {
        Provider = Normalize(provider, nameof(provider));
        SourceLanguage = Normalize(sourceLanguage, nameof(sourceLanguage));
        TargetLanguage = Normalize(targetLanguage, nameof(targetLanguage));
        SourceText = NormalizeSourceText(sourceText, nameof(sourceText));
        SourceTextHash = ComputeHash(SourceText);
    }

    public string Provider { get; }

    public string SourceLanguage { get; }

    public string TargetLanguage { get; }

    public string SourceText { get; }

    public string SourceTextHash { get; }

    public bool Equals(TranslationCacheKey? other)
    {
        return other is not null
            && string.Equals(Provider, other.Provider, StringComparison.OrdinalIgnoreCase)
            && string.Equals(SourceLanguage, other.SourceLanguage, StringComparison.OrdinalIgnoreCase)
            && string.Equals(TargetLanguage, other.TargetLanguage, StringComparison.OrdinalIgnoreCase)
            && string.Equals(SourceTextHash, other.SourceTextHash, StringComparison.Ordinal)
            && string.Equals(SourceText, other.SourceText, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as TranslationCacheKey);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(Provider),
            StringComparer.OrdinalIgnoreCase.GetHashCode(SourceLanguage),
            StringComparer.OrdinalIgnoreCase.GetHashCode(TargetLanguage),
            StringComparer.Ordinal.GetHashCode(SourceTextHash),
            StringComparer.Ordinal.GetHashCode(SourceText));
    }

    private static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        return value.Trim();
    }

    private static string NormalizeSourceText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        return OcrTextNormalizer.NormalizeForComparison(value);
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));

        return Convert.ToHexString(bytes);
    }
}
