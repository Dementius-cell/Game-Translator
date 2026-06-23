using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.UI.Services;

public sealed class UnavailableOcrLanguagePackService : IOcrLanguagePackService
{
    public static UnavailableOcrLanguagePackService Instance { get; } = new();

    private UnavailableOcrLanguagePackService()
    {
    }

    public Task<OcrLanguagePackStatus> CheckAsync(
        string engineId,
        string languageTag,
        OcrOrientationMode orientationMode,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateUnavailableStatus(engineId, languageTag, orientationMode));
    }

    public Task<OcrLanguagePackInstallResult> InstallAsync(
        string engineId,
        string languageTag,
        OcrOrientationMode orientationMode,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OcrLanguagePackInstallResult(
            false,
            CreateUnavailableStatus(engineId, languageTag, orientationMode).Message));
    }

    private static OcrLanguagePackStatus CreateUnavailableStatus(
        string engineId,
        string languageTag,
        OcrOrientationMode orientationMode)
    {
        return new OcrLanguagePackStatus(
            engineId,
            languageTag,
            orientationMode,
            IsReady: false,
            CanInstall: false,
            "OCR language pack management is not available in this runtime.");
    }
}
