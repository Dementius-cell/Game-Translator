namespace GameTranslator.Application.Ocr;

public sealed record TesseractLanguageInfo(string Code, string Name);

public static class TesseractLanguageCatalog
{
    private static readonly TesseractLanguageInfo[] KnownLanguages =
    {
        new("afr", "Afrikaans"),
        new("amh", "Amharic"),
        new("ara", "Arabic"),
        new("asm", "Assamese"),
        new("aze", "Azerbaijani"),
        new("aze_cyrl", "Azerbaijani (Cyrillic)"),
        new("bel", "Belarusian"),
        new("ben", "Bengali"),
        new("bod", "Tibetan"),
        new("bos", "Bosnian"),
        new("bre", "Breton"),
        new("bul", "Bulgarian"),
        new("cat", "Catalan"),
        new("ceb", "Cebuano"),
        new("ces", "Czech"),
        new("chi_sim", "Chinese (Simplified)"),
        new("chi_sim_vert", "Chinese (Simplified vertical)"),
        new("chi_tra", "Chinese (Traditional)"),
        new("chi_tra_vert", "Chinese (Traditional vertical)"),
        new("chr", "Cherokee"),
        new("cos", "Corsican"),
        new("cym", "Welsh"),
        new("dan", "Danish"),
        new("deu", "German"),
        new("div", "Divehi"),
        new("dzo", "Dzongkha"),
        new("ell", "Greek"),
        new("eng", "English"),
        new("enm", "Middle English"),
        new("epo", "Esperanto"),
        new("est", "Estonian"),
        new("eus", "Basque"),
        new("fao", "Faroese"),
        new("fas", "Persian"),
        new("fil", "Filipino"),
        new("fin", "Finnish"),
        new("fra", "French"),
        new("frk", "German Fraktur"),
        new("frm", "Middle French"),
        new("fry", "Western Frisian"),
        new("gla", "Scottish Gaelic"),
        new("gle", "Irish"),
        new("glg", "Galician"),
        new("grc", "Ancient Greek"),
        new("guj", "Gujarati"),
        new("hat", "Haitian Creole"),
        new("heb", "Hebrew"),
        new("hin", "Hindi"),
        new("hrv", "Croatian"),
        new("hun", "Hungarian"),
        new("hye", "Armenian"),
        new("iku", "Inuktitut"),
        new("ind", "Indonesian"),
        new("isl", "Icelandic"),
        new("ita", "Italian"),
        new("ita_old", "Italian (Old)"),
        new("jav", "Javanese"),
        new("jpn", "Japanese"),
        new("jpn_vert", "Japanese vertical"),
        new("kan", "Kannada"),
        new("kat", "Georgian"),
        new("kat_old", "Georgian (Old)"),
        new("kaz", "Kazakh"),
        new("khm", "Khmer"),
        new("kir", "Kyrgyz"),
        new("kmr", "Kurdish (Kurmanji)"),
        new("kor", "Korean"),
        new("lao", "Lao"),
        new("lat", "Latin"),
        new("lav", "Latvian"),
        new("lit", "Lithuanian"),
        new("ltz", "Luxembourgish"),
        new("mal", "Malayalam"),
        new("mar", "Marathi"),
        new("mkd", "Macedonian"),
        new("mlt", "Maltese"),
        new("mon", "Mongolian"),
        new("mri", "Maori"),
        new("msa", "Malay"),
        new("mya", "Burmese"),
        new("nep", "Nepali"),
        new("nld", "Dutch"),
        new("nor", "Norwegian"),
        new("oci", "Occitan"),
        new("ori", "Odia"),
        new("pan", "Punjabi"),
        new("pol", "Polish"),
        new("por", "Portuguese"),
        new("pus", "Pashto"),
        new("que", "Quechua"),
        new("ron", "Romanian"),
        new("rus", "Russian"),
        new("san", "Sanskrit"),
        new("sin", "Sinhala"),
        new("slk", "Slovak"),
        new("slv", "Slovenian"),
        new("snd", "Sindhi"),
        new("spa", "Spanish"),
        new("spa_old", "Spanish (Old)"),
        new("sqi", "Albanian"),
        new("srp", "Serbian"),
        new("srp_latn", "Serbian (Latin)"),
        new("sun", "Sundanese"),
        new("swa", "Swahili"),
        new("swe", "Swedish"),
        new("syr", "Syriac"),
        new("tam", "Tamil"),
        new("tat", "Tatar"),
        new("tel", "Telugu"),
        new("tgk", "Tajik"),
        new("tha", "Thai"),
        new("tir", "Tigrinya"),
        new("ton", "Tongan"),
        new("tur", "Turkish"),
        new("uig", "Uyghur"),
        new("ukr", "Ukrainian"),
        new("urd", "Urdu"),
        new("uzb", "Uzbek"),
        new("uzb_cyrl", "Uzbek (Cyrillic)"),
        new("vie", "Vietnamese"),
        new("yid", "Yiddish"),
        new("yor", "Yoruba"),
    };

    private static readonly IReadOnlyDictionary<string, string> KnownLanguageCodes =
        KnownLanguages.ToDictionary(language => language.Code, language => language.Code, StringComparer.OrdinalIgnoreCase);

    // Google/Windows-style language tags used by profiles are ISO 639-1 in most cases,
    // whereas Tesseract model filenames use ISO 639-2/3-style identifiers.
    // Keep this separate from KnownLanguageCodes: callers that need to distinguish an
    // explicit Tesseract model from a Windows language tag must retain that distinction.
    private static readonly IReadOnlyDictionary<string, string> KnownLanguageTagAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["af"] = "afr", ["am"] = "amh", ["ar"] = "ara", ["az"] = "aze",
            ["be"] = "bel", ["bg"] = "bul", ["bn"] = "ben", ["bs"] = "bos",
            ["ca"] = "cat", ["co"] = "cos", ["cs"] = "ces", ["cy"] = "cym",
            ["da"] = "dan", ["de"] = "deu", ["el"] = "ell", ["en"] = "eng",
            ["eo"] = "epo", ["es"] = "spa", ["et"] = "est", ["eu"] = "eus",
            ["fa"] = "fas", ["fi"] = "fin", ["fr"] = "fra", ["fy"] = "fry",
            ["ga"] = "gle", ["gd"] = "gla", ["gl"] = "glg", ["gu"] = "guj",
            ["he"] = "heb", ["hi"] = "hin", ["hr"] = "hrv", ["ht"] = "hat",
            ["hu"] = "hun", ["hy"] = "hye", ["id"] = "ind", ["is"] = "isl",
            ["it"] = "ita", ["ja"] = "jpn", ["jv"] = "jav", ["ka"] = "kat",
            ["kk"] = "kaz", ["km"] = "khm", ["kn"] = "kan", ["ko"] = "kor",
            ["ku"] = "kmr", ["ky"] = "kir", ["la"] = "lat", ["lb"] = "ltz",
            ["lo"] = "lao", ["lt"] = "lit", ["lv"] = "lav", ["mi"] = "mri",
            ["mk"] = "mkd", ["ml"] = "mal", ["mn"] = "mon", ["mr"] = "mar",
            ["ms"] = "msa", ["mt"] = "mlt", ["my"] = "mya", ["ne"] = "nep",
            ["nl"] = "nld", ["no"] = "nor", ["or"] = "ori", ["pa"] = "pan",
            ["pl"] = "pol", ["ps"] = "pus", ["pt"] = "por", ["ro"] = "ron",
            ["ru"] = "rus", ["sd"] = "snd", ["si"] = "sin", ["sk"] = "slk",
            ["sl"] = "slv", ["sq"] = "sqi", ["sr"] = "srp", ["su"] = "sun",
            ["sv"] = "swe", ["sw"] = "swa", ["ta"] = "tam", ["te"] = "tel",
            ["tg"] = "tgk", ["th"] = "tha", ["tr"] = "tur", ["uk"] = "ukr",
            ["ur"] = "urd", ["uz"] = "uzb", ["vi"] = "vie", ["yi"] = "yid",
            ["yo"] = "yor",
        };

    private static readonly IReadOnlyDictionary<string, string> PreferredLanguageTagsByTrainedDataCode =
        new Dictionary<string, string>(
            KnownLanguageTagAliases.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase)
        {
            ["aze_cyrl"] = "az",
            ["chi_sim"] = "zh-CN",
            ["chi_sim_vert"] = "zh-CN",
            ["chi_tra"] = "zh-TW",
            ["chi_tra_vert"] = "zh-TW",
            ["jpn_vert"] = "ja",
            ["srp_latn"] = "sr",
            ["uzb_cyrl"] = "uz",
        };

    public static IReadOnlyList<TesseractLanguageInfo> Languages => KnownLanguages;

    public static bool TryGetTrainedDataCode(string languageCode, out string trainedDataCode)
    {
        trainedDataCode = string.Empty;
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return false;
        }

        var normalizedCode = languageCode.Trim().Replace('-', '_').ToLowerInvariant();
        if (!KnownLanguageCodes.TryGetValue(normalizedCode, out var knownCode))
        {
            return false;
        }

        trainedDataCode = knownCode;
        return true;
    }

    public static bool TryMapLanguageTagToTrainedDataCode(string languageTag, out string trainedDataCode)
    {
        trainedDataCode = string.Empty;
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return false;
        }

        if (TryGetTrainedDataCode(languageTag, out trainedDataCode))
        {
            return true;
        }

        var normalizedTag = languageTag.Trim().Replace('_', '-');
        if (normalizedTag.Equals("zh-tw", StringComparison.OrdinalIgnoreCase)
            || normalizedTag.Equals("zh-hk", StringComparison.OrdinalIgnoreCase)
            || normalizedTag.Equals("zh-mo", StringComparison.OrdinalIgnoreCase)
            || normalizedTag.Equals("zh-hant", StringComparison.OrdinalIgnoreCase))
        {
            trainedDataCode = "chi_tra";
            return true;
        }

        if (normalizedTag.Equals("zh", StringComparison.OrdinalIgnoreCase)
            || normalizedTag.Equals("zh-cn", StringComparison.OrdinalIgnoreCase)
            || normalizedTag.Equals("zh-hans", StringComparison.OrdinalIgnoreCase))
        {
            trainedDataCode = "chi_sim";
            return true;
        }

        var primaryLanguage = normalizedTag
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(primaryLanguage))
        {
            return false;
        }

        if (!KnownLanguageTagAliases.TryGetValue(primaryLanguage, out var mappedCode))
        {
            return false;
        }

        trainedDataCode = mappedCode;
        return true;
    }

    public static bool TryMapTrainedDataCodeToPreferredLanguageTag(string trainedDataCode, out string languageTag)
    {
        languageTag = string.Empty;
        if (!TryGetTrainedDataCode(trainedDataCode, out var normalizedTrainedDataCode))
        {
            return false;
        }

        if (!PreferredLanguageTagsByTrainedDataCode.TryGetValue(normalizedTrainedDataCode, out var mappedLanguageTag))
        {
            return false;
        }

        languageTag = mappedLanguageTag;
        return true;
    }
}
