using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GameTranslator.Application.Translation;

internal static class TranslationOutputSanitizer
{
    private const string YandexWebProviderId = "YandexWeb";
    private const int MinimumSourceElementCount = 4;
    private const int MinimumRepeatCount = 5;
    private static readonly Regex WordPattern = new(
        @"[\p{L}\p{M}\p{Nd}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static TranslationOutputSanitizationResult Sanitize(
        string providerId,
        string sourceText,
        string translatedText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(translatedText);

        if (!string.Equals(providerId, YandexWebProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return TranslationOutputSanitizationResult.Unchanged(translatedText);
        }

        var sourceElements = GetSourceElements(sourceText);
        if (sourceElements.Count < MinimumSourceElementCount
            || HasExactFullRepetition(sourceElements, minimumRepeatCount: 2))
        {
            return TranslationOutputSanitizationResult.Unchanged(translatedText);
        }

        var translatedWords = WordPattern.Matches(translatedText).Cast<Match>().ToArray();
        var unitLength = FindExactRepeatedUnitLength(translatedWords);
        if (unitLength > 0)
        {
            var unitEnd = checked(translatedWords[unitLength - 1].Index + translatedWords[unitLength - 1].Length);
            var collapsed = translatedText[..unitEnd].TrimEnd();
            var trimmedTranslation = translatedText.TrimEnd();
            if (trimmedTranslation.Length > 0
                && IsTerminalPunctuation(trimmedTranslation[^1])
                && !IsTerminalPunctuation(collapsed[^1]))
            {
                collapsed += trimmedTranslation[^1];
            }

            return new TranslationOutputSanitizationResult(
                collapsed,
                WasSanitized: true,
                RepeatCount: translatedWords.Length / unitLength);
        }

        if (!TryFindDominantRepeatedWordRun(
                translatedWords,
                out var repeatedWordStartIndex,
                out var repeatedWordCount))
        {
            return TranslationOutputSanitizationResult.Unchanged(translatedText);
        }

        var firstRepeatedWord = translatedWords[repeatedWordStartIndex];
        var lastRepeatedWord = translatedWords[repeatedWordStartIndex + repeatedWordCount - 1];
        var firstRepeatedWordEnd = checked(firstRepeatedWord.Index + firstRepeatedWord.Length);
        var lastRepeatedWordEnd = checked(lastRepeatedWord.Index + lastRepeatedWord.Length);
        var collapsedDominantRun = string.Concat(
                translatedText.AsSpan(0, firstRepeatedWordEnd),
                translatedText.AsSpan(lastRepeatedWordEnd))
            .TrimEnd();

        return new TranslationOutputSanitizationResult(
            collapsedDominantRun,
            WasSanitized: true,
            RepeatCount: repeatedWordCount);
    }

    private static bool TryFindDominantRepeatedWordRun(
        IReadOnlyList<Match> words,
        out int repeatedWordStartIndex,
        out int repeatedWordCount)
    {
        repeatedWordStartIndex = 0;
        repeatedWordCount = 0;

        for (var startIndex = 0; startIndex < words.Count;)
        {
            var endIndex = startIndex + 1;
            while (endIndex < words.Count
                   && string.Equals(
                       words[endIndex].Value,
                       words[startIndex].Value,
                       StringComparison.OrdinalIgnoreCase))
            {
                endIndex++;
            }

            var runLength = endIndex - startIndex;
            if (runLength > repeatedWordCount)
            {
                repeatedWordStartIndex = startIndex;
                repeatedWordCount = runLength;
            }

            startIndex = endIndex;
        }

        return repeatedWordCount >= MinimumRepeatCount
            && words.Count - repeatedWordCount <= 1;
    }

    private static int FindExactRepeatedUnitLength(IReadOnlyList<Match> words)
    {
        for (var unitLength = 1; unitLength <= words.Count / MinimumRepeatCount; unitLength++)
        {
            if (words.Count % unitLength != 0)
            {
                continue;
            }

            var repeatCount = words.Count / unitLength;
            if (repeatCount < MinimumRepeatCount)
            {
                continue;
            }

            var matches = true;
            for (var index = unitLength; index < words.Count; index++)
            {
                if (string.Equals(
                        words[index].Value,
                        words[index % unitLength].Value,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matches = false;
                break;
            }

            if (matches)
            {
                return unitLength;
            }
        }

        return 0;
    }

    private static IReadOnlyList<int> GetSourceElements(string sourceText)
    {
        return sourceText
            .EnumerateRunes()
            .Where(IsSourceElement)
            .Select(rune => Rune.ToLowerInvariant(rune).Value)
            .ToArray();
    }

    private static bool IsSourceElement(Rune rune)
    {
        return Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.DecimalDigitNumber or
            UnicodeCategory.LetterNumber or
            UnicodeCategory.OtherNumber;
    }

    private static bool HasExactFullRepetition(
        IReadOnlyList<int> elements,
        int minimumRepeatCount)
    {
        for (var unitLength = 1; unitLength <= elements.Count / minimumRepeatCount; unitLength++)
        {
            if (elements.Count % unitLength != 0
                || elements.Count / unitLength < minimumRepeatCount)
            {
                continue;
            }

            var matches = true;
            for (var index = unitLength; index < elements.Count; index++)
            {
                if (elements[index] == elements[index % unitLength])
                {
                    continue;
                }

                matches = false;
                break;
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTerminalPunctuation(char character)
    {
        return character is '.' or '!' or '?' or '\u2026';
    }
}

internal readonly record struct TranslationOutputSanitizationResult(
    string Text,
    bool WasSanitized,
    int RepeatCount)
{
    public static TranslationOutputSanitizationResult Unchanged(string text)
    {
        return new TranslationOutputSanitizationResult(text, WasSanitized: false, RepeatCount: 1);
    }
}
