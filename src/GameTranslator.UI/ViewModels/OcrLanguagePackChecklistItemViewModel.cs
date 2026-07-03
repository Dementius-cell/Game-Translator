using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.UI.ViewModels;

public sealed class OcrLanguagePackChecklistItemViewModel : ObservableObject
{
    private string state = "Not checked";
    private string status = "Not checked.";
    private bool isReady;
    private bool canInstall;

    public OcrLanguagePackChecklistItemViewModel(
        string engineId,
        string languageTag,
        OcrOrientationMode orientationMode,
        string displayName,
        string purpose)
    {
        EngineId = engineId;
        LanguageTag = languageTag;
        OrientationMode = orientationMode;
        DisplayName = displayName;
        Purpose = purpose;
    }

    public string EngineId { get; }

    public string LanguageTag { get; }

    public OcrOrientationMode OrientationMode { get; }

    public string DisplayName { get; }

    public string Purpose { get; }

    public bool IsTesseract => string.Equals(
        EngineId,
        OcrSettings.TesseractEngineId,
        StringComparison.OrdinalIgnoreCase);

    public bool IsWindowsOcr => string.Equals(
        EngineId,
        OcrSettings.WindowsEngineId,
        StringComparison.OrdinalIgnoreCase);

    public string State
    {
        get => state;
        private set => SetProperty(ref state, value);
    }

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public bool IsReady
    {
        get => isReady;
        private set => SetProperty(ref isReady, value);
    }

    public bool CanInstall
    {
        get => canInstall;
        private set => SetProperty(ref canInstall, value);
    }

    public void MarkChecking()
    {
        IsReady = false;
        CanInstall = false;
        State = "Checking";
        Status = "Checking...";
    }

    public void MarkInstalling()
    {
        IsReady = false;
        CanInstall = false;
        State = "Installing";
        Status = "Downloading traineddata...";
    }

    public void ApplyStatus(OcrLanguagePackStatus packStatus)
    {
        IsReady = packStatus.IsReady;
        CanInstall = packStatus.CanInstall;
        State = packStatus.IsReady ? "Ready" : packStatus.CanInstall ? "Missing" : "Blocked";
        Status = packStatus.Message;
    }

    public void ApplyInstallResult(OcrLanguagePackInstallResult result)
    {
        IsReady = result.Succeeded;
        CanInstall = !result.Succeeded && IsTesseract;
        State = result.Succeeded ? "Ready" : "Failed";
        Status = result.Message;
    }

    public void MarkFailed(string message)
    {
        IsReady = false;
        CanInstall = IsTesseract;
        State = "Failed";
        Status = message;
    }
}
