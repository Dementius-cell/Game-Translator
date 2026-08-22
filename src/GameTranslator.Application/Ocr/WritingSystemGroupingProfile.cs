using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Ocr;

/// <summary>
/// Selects bounded candidate-grouping behavior by writing-system layout rather than by an individual language.
/// </summary>
public enum WritingSystemGroupingProfile
{
    SpacedLeftToRight,
    CjkHorizontalOrHybrid,
    CjkVertical,
    ComplexSouthEastAsian,
    BrahmicIndic,
    RightToLeftHebrew,
    RightToLeftArabicDerived,
}

/// <summary>
/// Resolves profile language tags and Tesseract traineddata codes to a shared writing-system cohort.
/// </summary>
public static class WritingSystemGroupingProfileResolver
{
    public static WritingSystemGroupingProfile Resolve(
        string language,
        OcrOrientationMode orientationMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        if (!Enum.IsDefined(orientationMode))
        {
            throw new ArgumentOutOfRangeException(nameof(orientationMode));
        }

        var normalizedLanguage = NormalizeLanguage(language);
        if (normalizedLanguage is "jpn_vert" or "chi_sim_vert" or "chi_tra_vert"
            || (orientationMode is OcrOrientationMode.Vertical
                && normalizedLanguage is "jpn" or "chi_sim" or "chi_tra"))
        {
            return WritingSystemGroupingProfile.CjkVertical;
        }

        return normalizedLanguage switch
        {
            "jpn" or "chi_sim" or "chi_tra" or "kor" => WritingSystemGroupingProfile.CjkHorizontalOrHybrid,
            "tha" or "lao" or "khm" or "mya" => WritingSystemGroupingProfile.ComplexSouthEastAsian,
            "asm" or "ben" or "guj" or "hin" or "kan" or "mal" or "mar" or "nep" or "ori" or "pan" or "san" or "sin" or "tam" or "tel" => WritingSystemGroupingProfile.BrahmicIndic,
            "heb" or "yid" => WritingSystemGroupingProfile.RightToLeftHebrew,
            "ara" or "fas" or "pus" or "snd" or "syr" or "uig" or "urd" => WritingSystemGroupingProfile.RightToLeftArabicDerived,
            _ => WritingSystemGroupingProfile.SpacedLeftToRight,
        };
    }

    private static string NormalizeLanguage(string language)
    {
        if (TesseractLanguageCatalog.TryMapLanguageTagToTrainedDataCode(language, out var trainedDataCode))
        {
            return trainedDataCode;
        }

        return language
            .Trim()
            .Replace('-', '_')
            .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()
            ?.ToLowerInvariant()
            ?? string.Empty;
    }
}
