namespace GameTranslator.Infrastructure.Translation;

internal static class TranslatorSecretRedactor
{
    public static string Redact(string value, params string?[] secrets)
    {
        var redacted = value;

        foreach (var secret in secrets.SelectMany(GetSecretVariants))
        {
            redacted = redacted.Replace(secret, "<redacted>", StringComparison.Ordinal);
        }

        return redacted;
    }

    private static IEnumerable<string> GetSecretVariants(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            yield break;
        }

        var trimmed = secret.Trim();
        yield return trimmed;

        var separatorIndex = trimmed.IndexOf(' ', StringComparison.Ordinal);
        if (separatorIndex > 0 && separatorIndex < trimmed.Length - 1)
        {
            yield return trimmed[(separatorIndex + 1)..].Trim();
        }
    }
}
