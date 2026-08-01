namespace GameTranslator.Application.Ocr;

/// <summary>
/// Describes the runtime layout strategy requested from an OCR engine.
/// </summary>
public enum OcrLayoutMode
{
    Auto = 0,
    Menu = 1,
    Dialog = 2,
    Comic = 3,
}
