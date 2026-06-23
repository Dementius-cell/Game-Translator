using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Ocr;

public interface IOcrLanguagePackService
{
    Task<OcrLanguagePackStatus> CheckAsync(
        string engineId,
        string languageTag,
        OcrOrientationMode orientationMode,
        CancellationToken cancellationToken = default);

    Task<OcrLanguagePackInstallResult> InstallAsync(
        string engineId,
        string languageTag,
        OcrOrientationMode orientationMode,
        CancellationToken cancellationToken = default);
}
