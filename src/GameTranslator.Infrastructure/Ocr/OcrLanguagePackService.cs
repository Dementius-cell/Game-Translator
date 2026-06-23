using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace GameTranslator.Infrastructure.Ocr;

public sealed class OcrLanguagePackService : IOcrLanguagePackService
{
    private static readonly Uri WindowsLanguageSettingsUri = new("ms-settings:regionlanguage");
    private static readonly Uri TesseractFastDataBaseUri = new("https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/");

    private readonly HttpClient httpClient;
    private readonly string tessdataPath;

    public OcrLanguagePackService(HttpClient httpClient)
        : this(httpClient, Path.Combine(AppContext.BaseDirectory, "tessdata"))
    {
    }

    public OcrLanguagePackService(HttpClient httpClient, string tessdataPath)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.tessdataPath = string.IsNullOrWhiteSpace(tessdataPath)
            ? Path.Combine(AppContext.BaseDirectory, "tessdata")
            : tessdataPath.Trim();
    }

    public Task<OcrLanguagePackStatus> CheckAsync(
        string engineId,
        string languageTag,
        OcrOrientationMode orientationMode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);

        if (string.Equals(engineId, OcrSettings.WindowsEngineId, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(CheckWindowsOcrLanguage(languageTag, orientationMode));
        }

        if (string.Equals(engineId, OcrSettings.TesseractEngineId, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(CheckTesseractLanguage(languageTag, orientationMode));
        }

        return Task.FromResult(new OcrLanguagePackStatus(
            engineId,
            languageTag,
            orientationMode,
            IsReady: false,
            CanInstall: false,
            $"OCR engine '{engineId}' is not supported for language pack management."));
    }

    public async Task<OcrLanguagePackInstallResult> InstallAsync(
        string engineId,
        string languageTag,
        OcrOrientationMode orientationMode,
        CancellationToken cancellationToken = default)
    {
        var status = await CheckAsync(engineId, languageTag, orientationMode, cancellationToken);
        if (status.IsReady)
        {
            return new OcrLanguagePackInstallResult(true, status.Message);
        }

        if (string.Equals(engineId, OcrSettings.WindowsEngineId, StringComparison.OrdinalIgnoreCase))
        {
            return new OcrLanguagePackInstallResult(
                false,
                "Windows OCR language packs are installed through Windows language settings. Add the language and OCR/handwriting features there, then check again.",
                WindowsLanguageSettingsUri);
        }

        if (!string.Equals(engineId, OcrSettings.TesseractEngineId, StringComparison.OrdinalIgnoreCase))
        {
            return new OcrLanguagePackInstallResult(false, status.Message);
        }

        var requiredLanguages = GetRequiredTesseractLanguages(languageTag, orientationMode);
        Directory.CreateDirectory(tessdataPath);

        foreach (var language in requiredLanguages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = Path.Combine(tessdataPath, $"{language}.traineddata");
            if (File.Exists(targetPath))
            {
                continue;
            }

            await DownloadTesseractLanguageAsync(language, targetPath, cancellationToken);
        }

        return new OcrLanguagePackInstallResult(
            true,
            $"Tesseract OCR language data installed for {languageTag} ({string.Join("+", requiredLanguages)}) in '{tessdataPath}'.");
    }

    private static OcrLanguagePackStatus CheckWindowsOcrLanguage(
        string languageTag,
        OcrOrientationMode orientationMode)
    {
        try
        {
            var language = new Language(languageTag);
            if (OcrEngine.IsLanguageSupported(language))
            {
                return new OcrLanguagePackStatus(
                    OcrSettings.WindowsEngineId,
                    languageTag,
                    orientationMode,
                    IsReady: true,
                    CanInstall: false,
                    $"Windows OCR language '{languageTag}' is installed and available.");
            }

            var installedLanguages = OcrEngine.AvailableRecognizerLanguages
                .Select(item => item.LanguageTag)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var installedSummary = installedLanguages.Length == 0
                ? "No Windows OCR languages are currently reported by the system."
                : $"Installed Windows OCR languages: {string.Join(", ", installedLanguages)}.";

            return new OcrLanguagePackStatus(
                OcrSettings.WindowsEngineId,
                languageTag,
                orientationMode,
                IsReady: false,
                CanInstall: true,
                $"Windows OCR language '{languageTag}' is not installed or not available. {installedSummary}",
                WindowsLanguageSettingsUri);
        }
        catch (Exception exception)
        {
            return new OcrLanguagePackStatus(
                OcrSettings.WindowsEngineId,
                languageTag,
                orientationMode,
                IsReady: false,
                CanInstall: false,
                $"Windows OCR language '{languageTag}' is invalid: {exception.Message}");
        }
    }

    private OcrLanguagePackStatus CheckTesseractLanguage(
        string languageTag,
        OcrOrientationMode orientationMode)
    {
        string[] requiredLanguages;
        try
        {
            requiredLanguages = GetRequiredTesseractLanguages(languageTag, orientationMode);
        }
        catch (OcrEngineException exception)
        {
            return new OcrLanguagePackStatus(
                OcrSettings.TesseractEngineId,
                languageTag,
                orientationMode,
                IsReady: false,
                CanInstall: false,
                exception.Message);
        }

        var missingLanguages = requiredLanguages
            .Where(language => !File.Exists(Path.Combine(tessdataPath, $"{language}.traineddata")))
            .ToArray();
        if (missingLanguages.Length == 0)
        {
            return new OcrLanguagePackStatus(
                OcrSettings.TesseractEngineId,
                languageTag,
                orientationMode,
                IsReady: true,
                CanInstall: false,
                $"Tesseract OCR language data is installed for {languageTag} ({string.Join("+", requiredLanguages)}).");
        }

        return new OcrLanguagePackStatus(
            OcrSettings.TesseractEngineId,
            languageTag,
            orientationMode,
            IsReady: false,
            CanInstall: true,
            $"Tesseract OCR is missing traineddata file(s): {string.Join(", ", missingLanguages.Select(language => $"{language}.traineddata"))}.",
            TesseractFastDataBaseUri);
    }

    private static string[] GetRequiredTesseractLanguages(string languageTag, OcrOrientationMode orientationMode)
    {
        return TesseractOcrEngine
            .MapLanguage(languageTag, orientationMode)
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task DownloadTesseractLanguageAsync(
        string language,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var downloadUri = new Uri(TesseractFastDataBaseUri, $"{language}.traineddata");
        using var response = await httpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var tempPath = $"{targetPath}.download";
        await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await response.Content.CopyToAsync(output, cancellationToken);
        }

        if (File.Exists(targetPath))
        {
            File.Delete(tempPath);
            return;
        }

        File.Move(tempPath, targetPath);
    }
}
