using GameTranslator.Application.Ocr;

namespace GameTranslator.Application.Pipeline;

/// <summary>
/// Contains separate OCR views for translation and masking after text grouping.
/// </summary>
public sealed class TranslationTextGroupingResult
{
    public TranslationTextGroupingResult(OcrResult translationSourceResult, OcrResult maskSourceResult)
    {
        TranslationSourceResult = translationSourceResult ?? throw new ArgumentNullException(nameof(translationSourceResult));
        MaskSourceResult = maskSourceResult ?? throw new ArgumentNullException(nameof(maskSourceResult));
    }

    /// <summary>
    /// Gets semantic blocks sent to cache and translation providers.
    /// </summary>
    public OcrResult TranslationSourceResult { get; }

    /// <summary>
    /// Gets accepted raw OCR blocks used to hide the original source text.
    /// </summary>
    public OcrResult MaskSourceResult { get; }
}
