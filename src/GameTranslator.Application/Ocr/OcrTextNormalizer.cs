using System.Text;

namespace GameTranslator.Application.Ocr;

public static class OcrTextNormalizer
{
    public static string NormalizeForComparison(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var collapsedText = string.Join(' ', text.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries));
        if (collapsedText.IndexOf(' ', StringComparison.Ordinal) < 0)
        {
            return collapsedText;
        }

        var normalizedText = new StringBuilder(collapsedText.Length);
        for (var index = 0; index < collapsedText.Length; index++)
        {
            var character = collapsedText[index];
            if (character == ' '
                && index > 0
                && index < collapsedText.Length - 1
                && IsCompactScriptCharacter(collapsedText[index - 1])
                && IsCompactScriptCharacter(collapsedText[index + 1]))
            {
                continue;
            }

            normalizedText.Append(character);
        }

        return normalizedText.ToString();
    }

    private static bool IsCompactScriptCharacter(char character)
    {
        return character is >= '\u3040' and <= '\u30ff'
            or >= '\u3400' and <= '\u4dbf'
            or >= '\u4e00' and <= '\u9fff'
            or >= '\uac00' and <= '\ud7af';
    }
}
