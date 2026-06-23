using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Ocr;

public sealed record OcrLanguagePackStatus(
    string EngineId,
    string LanguageTag,
    OcrOrientationMode OrientationMode,
    bool IsReady,
    bool CanInstall,
    string Message,
    Uri? ActionUri = null);
