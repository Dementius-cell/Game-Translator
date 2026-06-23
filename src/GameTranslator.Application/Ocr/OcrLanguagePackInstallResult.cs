namespace GameTranslator.Application.Ocr;

public sealed record OcrLanguagePackInstallResult(
    bool Succeeded,
    string Message,
    Uri? ActionUri = null);
