using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using GameTranslator.Application.Abstractions;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Profiles;
using GameTranslator.Domain.Profiles;
using GameTranslator.UI.Commands;

namespace GameTranslator.UI.ViewModels;

public sealed class MainViewModel : ValidatableObservableObject
{
    private const string SelectedProfileSettingKey = "profiles.selectedId";
    private const string ProfileFileDialogFilter = "Game Translator profile (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*";
    private const string DraftProfileNameSettingKey = "shell.draft.profile.name";
    private const string DraftProfileDescriptionSettingKey = "shell.draft.profile.description";
    private const string DraftTranslatorProviderSettingKey = "shell.draft.translator.provider";
    private const string DraftSourceLanguageSettingKey = "shell.draft.translator.sourceLanguage";
    private const string DraftTargetLanguageSettingKey = "shell.draft.translator.targetLanguage";
    private const string DraftOverlayMaskModeSettingKey = "shell.draft.overlay.maskMode";
    private const string DraftOverlayMaskColorSettingKey = "shell.draft.overlay.maskColor";
    private const string DraftOverlayOpacitySettingKey = "shell.draft.overlay.opacity";
    private const string DraftOverlayPaddingSettingKey = "shell.draft.overlay.padding";
    private const string DraftOcrZonesSettingKey = "shell.draft.ocrZones";
    private const string DraftSelectedZoneIdSettingKey = "shell.draft.selectedZoneId";
    private static readonly Regex HexColorPattern = new("^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);

    private readonly ProfileService profileService;
    private readonly ProfileExchangeService profileExchangeService;
    private readonly CaptureService captureService;
    private readonly OcrService ocrService;
    private readonly IOverlayService overlayService;
    private readonly OverlayPositioningService overlayPositioningService;
    private readonly IDialogService dialogService;
    private readonly ISettingsService settings;
    private readonly IApplicationLogger logger;

    private string? pendingSelectedProfileId;
    private string? editingProfileId;
    private GameProfile? selectedProfile;
    private string profileName = string.Empty;
    private string profileDescription = string.Empty;
    private string translatorProvider = string.Empty;
    private string sourceLanguage = string.Empty;
    private string targetLanguage = string.Empty;
    private OverlayMaskMode overlayMaskMode = OverlayMaskMode.Solid;
    private string overlayMaskColor = "#000000";
    private double overlayOpacity = 1;
    private double overlayPadding;
    private OcrZoneEditorViewModel? selectedZone;
    private OcrResult? latestOcrPreviewResult;
    private ImageSource? capturePreviewImage;
    private string capturePreviewStatus = "No capture preview yet.";
    private string captureRefreshMetricsSummary = "Refresh rate not measured.";
    private string ocrPreviewStatus = "No OCR preview yet.";
    private string overlayPreviewStatus = "Overlay preview hidden.";
    private int capturePreviewWidth;
    private int capturePreviewHeight;
    private string statusMessage = "Loading profiles...";
    private bool isBusy;
    private bool isLoaded;
    private bool suppressDraftStatePersistence;
    private bool isZoneSelectionActive;
    private bool isZoneResizeActive;
    private double zoneSelectionStartX;
    private double zoneSelectionStartY;
    private double zoneSelectionPreviewX;
    private double zoneSelectionPreviewY;
    private double zoneSelectionPreviewWidth;
    private double zoneSelectionPreviewHeight;
    private int zoneResizeOriginalAbsoluteX;
    private int zoneResizeOriginalAbsoluteY;

    public MainViewModel(
        ProfileService profileService,
        ProfileExchangeService profileExchangeService,
        CaptureService captureService,
        OcrService ocrService,
        IOverlayService overlayService,
        OverlayPositioningService overlayPositioningService,
        IDialogService dialogService,
        ISettingsService settings,
        IApplicationLogger logger)
    {
        this.profileService = profileService;
        this.profileExchangeService = profileExchangeService;
        this.captureService = captureService;
        this.ocrService = ocrService;
        this.overlayService = overlayService;
        this.overlayPositioningService = overlayPositioningService;
        this.dialogService = dialogService;
        this.settings = settings;
        this.logger = logger;
        pendingSelectedProfileId = settings.GetValue<string>(SelectedProfileSettingKey);

        Profiles = new ObservableCollection<GameProfile>();
        OcrZones = new ObservableCollection<OcrZoneEditorViewModel>();
        OcrPreviewTextBlocks = new ObservableCollection<OcrTextBlock>();
        OcrDebugTextBlocks = new ObservableCollection<OcrDebugTextBlockViewModel>();
        ValidationErrors = new ObservableCollection<string>();
        OverlayMaskModes = Enum.GetValues<OverlayMaskMode>();
        TranslatorProviderOptions = new[] { "Google", "Azure", "Yandex" };
        LanguageOptions = new[] { "ja", "en", "ru", "ko", "zh-CN", "zh-TW" };
        BeginCreateProfileCommand = new RelayCommand(BeginCreateProfile, () => !IsBusy);
        RefreshProfilesCommand = new AsyncRelayCommand(RefreshProfilesAsync, () => !IsBusy);
        SaveProfileCommand = new AsyncRelayCommand(SaveAsync, CanSaveProfile);
        ImportProfileCommand = new AsyncRelayCommand(ImportProfileAsync, () => !IsBusy);
        ExportSelectedProfileCommand = new AsyncRelayCommand(ExportSelectedProfileAsync, CanExportSelectedProfile);
        CloneSelectedProfileCommand = new AsyncRelayCommand(CloneSelectedProfileAsync, CanCloneSelectedProfile);
        DeleteSelectedProfileCommand = new AsyncRelayCommand(DeleteSelectedProfileAsync, CanDeleteSelectedProfile);
        ResetEditorCommand = new RelayCommand(ResetEditor, () => !IsBusy);
        AddZoneCommand = new RelayCommand(AddZone, () => !IsBusy);
        DuplicateSelectedZoneCommand = new RelayCommand(DuplicateSelectedZone, CanDuplicateSelectedZone);
        MoveSelectedZoneUpCommand = new RelayCommand(MoveSelectedZoneUp, CanMoveSelectedZoneUp);
        MoveSelectedZoneDownCommand = new RelayCommand(MoveSelectedZoneDown, CanMoveSelectedZoneDown);
        RemoveSelectedZoneCommand = new RelayCommand(RemoveSelectedZone, CanRemoveSelectedZone);
        RefreshCapturePreviewCommand = new AsyncRelayCommand(RefreshCapturePreviewAsync, CanRefreshCapturePreview);
        MeasureCaptureRefreshCommand = new AsyncRelayCommand(MeasureCaptureRefreshAsync, CanRefreshCapturePreview);
        RecognizeOcrPreviewCommand = new AsyncRelayCommand(RecognizeOcrPreviewAsync, CanRecognizeOcrPreview);
        ShowOverlayPreviewCommand = new RelayCommand(ShowOverlayPreview, () => !IsBusy);
        HideOverlayPreviewCommand = new RelayCommand(HideOverlayPreview, () => !IsBusy && IsOverlayPreviewVisible);

        BeginCreateProfile();
        StatusMessage = "Ready to manage game profiles.";
    }

    public string ApplicationName => "Game Translator";

    public string CurrentStage => "Sprint 9";

    public double ZoneSurfaceWidth => OcrZoneEditorViewModel.PreviewSurfaceWidth;

    public double ZoneSurfaceHeight => OcrZoneEditorViewModel.PreviewSurfaceHeight;

    public string ZoneSurfaceSummary => $"Reference surface {OcrZoneEditorViewModel.ReferenceSurfaceWidth}x{OcrZoneEditorViewModel.ReferenceSurfaceHeight}";

    public ObservableCollection<GameProfile> Profiles { get; }

    public ObservableCollection<OcrZoneEditorViewModel> OcrZones { get; }

    public ObservableCollection<OcrTextBlock> OcrPreviewTextBlocks { get; }

    public ObservableCollection<OcrDebugTextBlockViewModel> OcrDebugTextBlocks { get; }

    public ObservableCollection<string> ValidationErrors { get; }

    public IReadOnlyList<OverlayMaskMode> OverlayMaskModes { get; }

    public IReadOnlyList<string> TranslatorProviderOptions { get; }

    public IReadOnlyList<string> LanguageOptions { get; }

    public GameProfile? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (!SetProperty(ref selectedProfile, value))
            {
                return;
            }

            editingProfileId = value?.Id;
            settings.SetValue(SelectedProfileSettingKey, value?.Id);

            if (value is null)
            {
                LoadDraftValues();
            }
            else
            {
                LoadEditorFromProfile(value);
            }

            SyncSelectedZoneState();
            OnPropertyChanged(nameof(HasSelectedProfile));
            OnPropertyChanged(nameof(ActiveProfileName));
            OnPropertyChanged(nameof(EditorTitle));
            OnPropertyChanged(nameof(ProfileSummary));
            OnPropertyChanged(nameof(HasValidationErrors));
            OnPropertyChanged(nameof(IsEditorValid));
            NotifyCommandStateChanged();
        }
    }

    public string ProfileName
    {
        get => profileName;
        set
        {
            if (SetProperty(ref profileName, value))
            {
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
            }
        }
    }

    public string ProfileDescription
    {
        get => profileDescription;
        set
        {
            if (SetProperty(ref profileDescription, value))
            {
                PersistDraftShellStateIfNeeded();
                OnPropertyChanged(nameof(ProfileSummary));
            }
        }
    }

    public string TranslatorProvider
    {
        get => translatorProvider;
        set
        {
            if (SetProperty(ref translatorProvider, value))
            {
                OnPropertyChanged(nameof(TranslatorSettingsSummary));
                OnPropertyChanged(nameof(ProfileSummary));
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
                NotifyCommandStateChanged();
            }
        }
    }

    public string SourceLanguage
    {
        get => sourceLanguage;
        set
        {
            if (SetProperty(ref sourceLanguage, value))
            {
                OnPropertyChanged(nameof(TranslatorSettingsSummary));
                OnPropertyChanged(nameof(ProfileSummary));
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
            }
        }
    }

    public string TargetLanguage
    {
        get => targetLanguage;
        set
        {
            if (SetProperty(ref targetLanguage, value))
            {
                OnPropertyChanged(nameof(TranslatorSettingsSummary));
                OnPropertyChanged(nameof(ProfileSummary));
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
            }
        }
    }

    public OverlayMaskMode OverlayMaskMode
    {
        get => overlayMaskMode;
        set
        {
            if (SetProperty(ref overlayMaskMode, value))
            {
                OnPropertyChanged(nameof(ProfileSummary));
                PersistDraftShellStateIfNeeded();
            }
        }
    }

    public string OverlayMaskColor
    {
        get => overlayMaskColor;
        set
        {
            if (SetProperty(ref overlayMaskColor, value))
            {
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
            }
        }
    }

    public double OverlayOpacity
    {
        get => overlayOpacity;
        set
        {
            if (SetProperty(ref overlayOpacity, value))
            {
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
            }
        }
    }

    public double OverlayPadding
    {
        get => overlayPadding;
        set
        {
            if (SetProperty(ref overlayPadding, value))
            {
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
            }
        }
    }

    public OcrZoneEditorViewModel? SelectedZone
    {
        get => selectedZone;
        set
        {
            if (!SetProperty(ref selectedZone, value))
            {
                return;
            }

            PersistDraftShellStateIfNeeded();
            SyncSelectedZoneState();
            ClearCapturePreview();
            OnPropertyChanged(nameof(HasSelectedZone));
            NotifyCommandStateChanged();
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                NotifyCommandStateChanged();
            }
        }
    }

    public bool IsIdle => !IsBusy;

    public bool HasSelectedProfile => SelectedProfile is not null;

    public bool HasSelectedZone => SelectedZone is not null;

    public bool HasValidationErrors => HasErrors || OcrZones.Any(zone => zone.HasErrors);

    public bool IsEditorValid => !HasValidationErrors;

    public string ActiveProfileName => SelectedProfile?.Name ?? "No active profile";

    public string EditorTitle => string.IsNullOrWhiteSpace(editingProfileId)
        ? "New Profile"
        : "Edit Profile";

    public string ProfileSummary
    {
        get
        {
            var schemaVersion = SelectedProfile?.SchemaVersion ?? GameProfile.CurrentSchemaVersion;

            return $"schema {schemaVersion} | {TranslatorSettingsSummary} | zones {OcrZones.Count} | overlay {OverlayMaskMode}";
        }
    }

    public string TranslatorSettingsSummary
    {
        get
        {
            var provider = string.IsNullOrWhiteSpace(TranslatorProvider) ? "no provider" : TranslatorProvider.Trim();
            var source = string.IsNullOrWhiteSpace(SourceLanguage) ? "source ?" : SourceLanguage.Trim();
            var target = string.IsNullOrWhiteSpace(TargetLanguage) ? "target ?" : TargetLanguage.Trim();

            return $"{provider} | {source} -> {target}";
        }
    }

    public string ZoneSummary => $"{OcrZones.Count} zone(s)";

    public bool HasZoneSelectionPreview => isZoneSelectionActive && zoneSelectionPreviewWidth > 0 && zoneSelectionPreviewHeight > 0;

    public double ZoneSelectionPreviewX
    {
        get => zoneSelectionPreviewX;
        private set => SetProperty(ref zoneSelectionPreviewX, value);
    }

    public double ZoneSelectionPreviewY
    {
        get => zoneSelectionPreviewY;
        private set => SetProperty(ref zoneSelectionPreviewY, value);
    }

    public double ZoneSelectionPreviewWidth
    {
        get => zoneSelectionPreviewWidth;
        private set => SetProperty(ref zoneSelectionPreviewWidth, value);
    }

    public double ZoneSelectionPreviewHeight
    {
        get => zoneSelectionPreviewHeight;
        private set => SetProperty(ref zoneSelectionPreviewHeight, value);
    }

    public ImageSource? CapturePreviewImage
    {
        get => capturePreviewImage;
        private set
        {
            if (SetProperty(ref capturePreviewImage, value))
            {
                OnPropertyChanged(nameof(HasCapturePreview));
            }
        }
    }

    public bool HasCapturePreview => CapturePreviewImage is not null;

    public int CapturePreviewWidth
    {
        get => capturePreviewWidth;
        private set => SetProperty(ref capturePreviewWidth, value);
    }

    public int CapturePreviewHeight
    {
        get => capturePreviewHeight;
        private set => SetProperty(ref capturePreviewHeight, value);
    }

    public string CapturePreviewStatus
    {
        get => capturePreviewStatus;
        private set => SetProperty(ref capturePreviewStatus, value);
    }

    public string CaptureRefreshMetricsSummary
    {
        get => captureRefreshMetricsSummary;
        private set => SetProperty(ref captureRefreshMetricsSummary, value);
    }

    public string OcrPreviewStatus
    {
        get => ocrPreviewStatus;
        private set => SetProperty(ref ocrPreviewStatus, value);
    }

    public bool HasOcrPreview => OcrDebugTextBlocks.Count > 0;

    public string OcrPreviewText => string.Join(Environment.NewLine, OcrDebugTextBlocks.Select(block => block.Text));

    public string OverlayPreviewStatus
    {
        get => overlayPreviewStatus;
        private set => SetProperty(ref overlayPreviewStatus, value);
    }

    public bool IsOverlayPreviewVisible => overlayService.IsVisible;

    public ICommand BeginCreateProfileCommand { get; }

    public ICommand RefreshProfilesCommand { get; }

    public ICommand SaveProfileCommand { get; }

    public ICommand ImportProfileCommand { get; }

    public ICommand ExportSelectedProfileCommand { get; }

    public ICommand CloneSelectedProfileCommand { get; }

    public ICommand DeleteSelectedProfileCommand { get; }

    public ICommand ResetEditorCommand { get; }

    public ICommand AddZoneCommand { get; }

    public ICommand DuplicateSelectedZoneCommand { get; }

    public ICommand MoveSelectedZoneUpCommand { get; }

    public ICommand MoveSelectedZoneDownCommand { get; }

    public ICommand RemoveSelectedZoneCommand { get; }

    public ICommand RefreshCapturePreviewCommand { get; }

    public ICommand MeasureCaptureRefreshCommand { get; }

    public ICommand RecognizeOcrPreviewCommand { get; }

    public ICommand ShowOverlayPreviewCommand { get; }

    public ICommand HideOverlayPreviewCommand { get; }

    public async Task LoadAsync()
    {
        if (isLoaded)
        {
            return;
        }

        isLoaded = true;
        await RefreshProfilesAsync();
    }

    public void SelectZone(string zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            return;
        }

        SelectedZone = OcrZones.FirstOrDefault(zone => string.Equals(zone.Id, zoneId, StringComparison.Ordinal));
    }

    public void StartZoneSelection(double surfaceX, double surfaceY)
    {
        if (IsBusy)
        {
            return;
        }

        ClearZoneResizeState();

        zoneSelectionStartX = Math.Clamp(surfaceX, 0, ZoneSurfaceWidth);
        zoneSelectionStartY = Math.Clamp(surfaceY, 0, ZoneSurfaceHeight);
        isZoneSelectionActive = true;
        UpdateZoneSelectionPreview(zoneSelectionStartX, zoneSelectionStartY, 0, 0);
        StatusMessage = "Drag on the surface to create an OCR zone.";
    }

    public void UpdateZoneSelection(double surfaceX, double surfaceY)
    {
        if (!isZoneSelectionActive)
        {
            return;
        }

        var currentX = Math.Clamp(surfaceX, 0, ZoneSurfaceWidth);
        var currentY = Math.Clamp(surfaceY, 0, ZoneSurfaceHeight);
        var left = Math.Min(zoneSelectionStartX, currentX);
        var top = Math.Min(zoneSelectionStartY, currentY);
        var width = Math.Abs(currentX - zoneSelectionStartX);
        var height = Math.Abs(currentY - zoneSelectionStartY);

        UpdateZoneSelectionPreview(left, top, width, height);
    }

    public void CompleteZoneSelection(double surfaceX, double surfaceY)
    {
        if (!isZoneSelectionActive)
        {
            return;
        }

        UpdateZoneSelection(surfaceX, surfaceY);
        isZoneSelectionActive = false;

        if (ZoneSelectionPreviewWidth < 4 || ZoneSelectionPreviewHeight < 4)
        {
            ClearZoneSelectionPreview();
            StatusMessage = "Zone selection canceled.";
            return;
        }

        var zone = OcrZoneEditorViewModel.CreateDefault(OcrZones.Count + 1);
        AttachZone(zone);
        OcrZones.Add(zone);
        ApplySurfaceBoundsToZone(
            zone,
            ZoneSelectionPreviewX,
            ZoneSelectionPreviewY,
            ZoneSelectionPreviewWidth,
            ZoneSelectionPreviewHeight);
        SelectedZone = zone;
        PersistDraftShellStateIfNeeded();
        OnPropertyChanged(nameof(ZoneSummary));
        OnPropertyChanged(nameof(ProfileSummary));
        RefreshValidationState();
        ClearZoneSelectionPreview();
        StatusMessage = $"Created zone '{zone.DisplayName}' from the surface.";
    }

    public void StartSelectedZoneResize()
    {
        if (IsBusy || SelectedZone is null)
        {
            return;
        }

        ClearZoneSelectionPreview();
        isZoneResizeActive = true;
        zoneResizeOriginalAbsoluteX = SelectedZone.AbsoluteX;
        zoneResizeOriginalAbsoluteY = SelectedZone.AbsoluteY;
        StatusMessage = $"Resize '{SelectedZone.DisplayName}' using the surface handle.";
    }

    public void UpdateSelectedZoneResize(double surfaceX, double surfaceY)
    {
        if (!isZoneResizeActive || SelectedZone is null)
        {
            return;
        }

        var left = ConvertAbsoluteToSurface(zoneResizeOriginalAbsoluteX, OcrZoneEditorViewModel.ReferenceSurfaceWidth, ZoneSurfaceWidth);
        var top = ConvertAbsoluteToSurface(zoneResizeOriginalAbsoluteY, OcrZoneEditorViewModel.ReferenceSurfaceHeight, ZoneSurfaceHeight);
        var clampedX = Math.Clamp(surfaceX, left + 1, ZoneSurfaceWidth);
        var clampedY = Math.Clamp(surfaceY, top + 1, ZoneSurfaceHeight);

        ApplySurfaceBoundsToZone(
            SelectedZone,
            left,
            top,
            clampedX - left,
            clampedY - top);
        PersistDraftShellStateIfNeeded();
        OnPropertyChanged(nameof(ProfileSummary));
        RefreshValidationState();
    }

    public void CompleteSelectedZoneResize(double surfaceX, double surfaceY)
    {
        if (!isZoneResizeActive)
        {
            return;
        }

        UpdateSelectedZoneResize(surfaceX, surfaceY);
        ClearZoneResizeState();

        if (SelectedZone is not null)
        {
            StatusMessage = $"Resized zone '{SelectedZone.DisplayName}'.";
        }
    }

    public void BeginCreateProfile()
    {
        selectedProfile = null;
        editingProfileId = null;
        ClearSurfaceInteractionState();

        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(HasSelectedProfile));
        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(EditorTitle));

        LoadDraftValues();
        StatusMessage = "Drafting a new profile.";
        RefreshValidationState();
        NotifyCommandStateChanged();
    }

    public async Task RefreshProfilesAsync()
    {
        var preferredProfileId = editingProfileId
            ?? pendingSelectedProfileId
            ?? settings.GetValue<string>(SelectedProfileSettingKey);
        await RefreshProfilesAsync(preferredProfileId);
    }

    public async Task SaveAsync()
    {
        RefreshValidationState();
        if (!CanSaveProfile())
        {
            if (HasValidationErrors)
            {
                StatusMessage = ValidationErrors[0];
            }

            return;
        }

        await RunProfileOperationAsync(
            string.IsNullOrWhiteSpace(editingProfileId) ? "Creating profile..." : "Saving profile...",
            async () =>
            {
                var profileToSave = BuildProfileFromEditor();
                var savedProfile = string.IsNullOrWhiteSpace(editingProfileId)
                    ? await profileService.CreateAsync(profileToSave)
                    : await profileService.UpdateAsync(profileToSave);

                logger.Information($"Profile '{savedProfile.Name}' saved.");
                await RefreshProfilesAsync(savedProfile.Id);
                StatusMessage = $"Profile '{savedProfile.Name}' saved.";
            });
    }

    public async Task ImportProfileAsync()
    {
        var filePath = await dialogService.ShowOpenFileDialogAsync(
            "Import profile",
            ProfileFileDialogFilter);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        GameProfile? importedResult = null;
        var succeeded = await RunProfileOperationAsync(
            "Importing profile...",
            async () =>
            {
                var importedProfile = await profileExchangeService.ImportAsync(filePath);
                var conflictingProfile = FindProfileByName(importedProfile.Name);
                if (conflictingProfile is not null)
                {
                    var dialogChoice = await dialogService.ShowYesNoCancelDialogAsync(
                        "Import conflict",
                        $"A profile named '{conflictingProfile.Name}' already exists.\n\nYes: replace the existing profile.\nNo: keep both profiles.\nCancel: abort the import.");
                    if (dialogChoice == DialogChoice.Cancel)
                    {
                        StatusMessage = "Profile import canceled.";
                        return;
                    }

                    var conflictPolicy = dialogChoice == DialogChoice.Yes
                        ? ProfileImportConflictPolicy.ReplaceExisting
                        : ProfileImportConflictPolicy.KeepBoth;
                    importedResult = await SaveImportedProfileAsync(importedProfile, conflictingProfile, conflictPolicy);
                }
                else
                {
                    importedResult = await SaveImportedProfileAsync(importedProfile, null, ProfileImportConflictPolicy.KeepBoth);
                }

                if (importedResult is null)
                {
                    return;
                }

                logger.Information($"Profile '{importedResult.Name}' imported from '{filePath}'.");
                await RefreshProfilesAsync(importedResult.Id);
                StatusMessage = conflictingProfile is null
                    ? $"Profile '{importedResult.Name}' imported."
                    : $"Profile '{importedResult.Name}' imported with conflict policy applied.";
            });

        if (succeeded && importedResult is not null)
        {
            await dialogService.ShowInformationAsync(
                "Import complete",
                $"Profile '{importedResult.Name}' is ready to use.");
        }
    }

    public async Task ExportSelectedProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var filePath = await dialogService.ShowSaveFileDialogAsync(
            "Export profile",
            BuildDefaultExportFileName(SelectedProfile.Name),
            ProfileFileDialogFilter);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var succeeded = await RunProfileOperationAsync(
            $"Exporting '{SelectedProfile.Name}'...",
            async () =>
            {
                await profileExchangeService.ExportAsync(SelectedProfile, filePath);

                logger.Information($"Profile '{SelectedProfile.Name}' exported to '{filePath}'.");
                StatusMessage = $"Profile '{SelectedProfile.Name}' exported.";
            });

        if (succeeded)
        {
            await dialogService.ShowInformationAsync(
                "Export complete",
                $"Profile '{SelectedProfile.Name}' was exported to:\n{filePath}");
        }
    }

    public async Task CloneSelectedProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunProfileOperationAsync(
            $"Cloning '{SelectedProfile.Name}'...",
            async () =>
            {
                var cloneName = BuildCloneName(SelectedProfile.Name);
                var clonedProfile = await profileService.CloneAsync(SelectedProfile.Id, cloneName);

                logger.Information($"Profile '{SelectedProfile.Name}' cloned to '{clonedProfile.Name}'.");
                await RefreshProfilesAsync(clonedProfile.Id);
                StatusMessage = $"Profile '{clonedProfile.Name}' created.";
            });
    }

    public async Task DeleteSelectedProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var profileId = SelectedProfile.Id;
        var profileNameToDelete = SelectedProfile.Name;

        await RunProfileOperationAsync(
            $"Deleting '{profileNameToDelete}'...",
            async () =>
            {
                await profileService.DeleteAsync(profileId);

                logger.Information($"Profile '{profileNameToDelete}' deleted.");
                await RefreshProfilesAsync();
                StatusMessage = $"Profile '{profileNameToDelete}' deleted.";
            });
    }

    public void ResetEditor()
    {
        if (SelectedProfile is null)
        {
            BeginCreateProfile();
            return;
        }

        LoadEditorFromProfile(SelectedProfile);
        StatusMessage = $"Reset editor for '{SelectedProfile.Name}'.";
        RefreshValidationState();
    }

    public void AddZone()
    {
        ClearSurfaceInteractionState();
        var zone = OcrZoneEditorViewModel.CreateDefault(OcrZones.Count + 1);
        AttachZone(zone);
        OcrZones.Add(zone);
        SelectedZone = zone;
        PersistDraftShellStateIfNeeded();
        OnPropertyChanged(nameof(ZoneSummary));
        OnPropertyChanged(nameof(ProfileSummary));
        RefreshValidationState();

        if (OcrZones.Count == 0)
        {
            ClearZoneResizeState();
        }
    }

    public void RemoveSelectedZone()
    {
        if (SelectedZone is null)
        {
            return;
        }

        var index = OcrZones.IndexOf(SelectedZone);
        DetachZone(SelectedZone);
        OcrZones.Remove(SelectedZone);
        SelectedZone = OcrZones.Count == 0
            ? null
            : OcrZones[Math.Clamp(index, 0, OcrZones.Count - 1)];
        PersistDraftShellStateIfNeeded();
        OnPropertyChanged(nameof(ZoneSummary));
        OnPropertyChanged(nameof(ProfileSummary));
        RefreshValidationState();
    }

    public void DuplicateSelectedZone()
    {
        if (SelectedZone is null)
        {
            return;
        }

        var selectedIndex = OcrZones.IndexOf(SelectedZone);
        var duplicate = SelectedZone.CreateDuplicate(BuildDuplicateZoneName(SelectedZone.Name));
        AttachZone(duplicate);
        OcrZones.Insert(selectedIndex + 1, duplicate);
        SelectedZone = duplicate;
        PersistDraftShellStateIfNeeded();
        OnPropertyChanged(nameof(ZoneSummary));
        OnPropertyChanged(nameof(ProfileSummary));
        RefreshValidationState();
    }

    public void MoveSelectedZoneUp()
    {
        MoveSelectedZoneBy(-1);
    }

    public void MoveSelectedZoneDown()
    {
        MoveSelectedZoneBy(1);
    }

    public async Task RefreshCapturePreviewAsync()
    {
        if (SelectedZone is null)
        {
            CapturePreviewStatus = "Select an OCR zone to preview capture.";
            StatusMessage = CapturePreviewStatus;
            return;
        }

        try
        {
            IsBusy = true;
            var region = new CaptureRegion(
                SelectedZone.AbsoluteX,
                SelectedZone.AbsoluteY,
                SelectedZone.AbsoluteWidth,
                SelectedZone.AbsoluteHeight);

            StatusMessage = $"Capturing preview for '{SelectedZone.DisplayName}'...";
            var frame = await captureService.CaptureAsync(region);
            UpdateCapturePreview(frame);
            ClearOcrPreview();
            CapturePreviewStatus = $"Captured {frame.Width}x{frame.Height} at {frame.CapturedAt:HH:mm:ss}.";
            StatusMessage = CapturePreviewStatus;
            logger.Information($"Capture preview refreshed for zone '{SelectedZone.DisplayName}'.");
        }
        catch (CaptureFrameSourceException exception)
        {
            logger.Error(exception, "Capture preview failed.");
            CapturePreviewStatus = $"Capture preview failed: {exception.Message}";
            StatusMessage = CapturePreviewStatus;
        }
        catch (OperationCanceledException)
        {
            CapturePreviewStatus = "Capture preview canceled.";
            StatusMessage = CapturePreviewStatus;
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Unexpected capture preview failure.");
            CapturePreviewStatus = "Capture preview failed. Check logs for details.";
            StatusMessage = CapturePreviewStatus;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task MeasureCaptureRefreshAsync()
    {
        if (SelectedZone is null)
        {
            CaptureRefreshMetricsSummary = "Select an OCR zone to measure capture refresh.";
            StatusMessage = CaptureRefreshMetricsSummary;
            return;
        }

        try
        {
            IsBusy = true;
            var region = new CaptureRegion(
                SelectedZone.AbsoluteX,
                SelectedZone.AbsoluteY,
                SelectedZone.AbsoluteWidth,
                SelectedZone.AbsoluteHeight);
            await using var session = captureService.CreateSession(region);

            StatusMessage = $"Measuring capture refresh for '{SelectedZone.DisplayName}'...";
            var result = await session.MeasureRefreshAsync(CaptureSessionOptions.MvpTargetFramesPerSecond);
            UpdateCapturePreview(result.LatestFrame);
            ClearOcrPreview();
            CapturePreviewStatus = $"Captured {result.LatestFrame.Width}x{result.LatestFrame.Height} at {result.LatestFrame.CapturedAt:HH:mm:ss}.";
            CaptureRefreshMetricsSummary = FormatCaptureRefreshMetrics(result.Metrics);
            StatusMessage = CaptureRefreshMetricsSummary;
            logger.Information($"Capture refresh measured for zone '{SelectedZone.DisplayName}': {CaptureRefreshMetricsSummary}");
        }
        catch (CaptureFrameSourceException exception)
        {
            logger.Error(exception, "Capture refresh measurement failed.");
            CaptureRefreshMetricsSummary = $"Capture refresh failed: {exception.Message}";
            StatusMessage = CaptureRefreshMetricsSummary;
        }
        catch (OperationCanceledException)
        {
            CaptureRefreshMetricsSummary = "Capture refresh measurement canceled.";
            StatusMessage = CaptureRefreshMetricsSummary;
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Unexpected capture refresh measurement failure.");
            CaptureRefreshMetricsSummary = "Capture refresh failed. Check logs for details.";
            StatusMessage = CaptureRefreshMetricsSummary;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RecognizeOcrPreviewAsync()
    {
        if (SelectedZone is null)
        {
            OcrPreviewStatus = "Select an OCR zone to recognize text.";
            StatusMessage = OcrPreviewStatus;
            return;
        }

        if (string.IsNullOrWhiteSpace(SourceLanguage))
        {
            OcrPreviewStatus = "Source language is required for OCR.";
            StatusMessage = OcrPreviewStatus;
            return;
        }

        var zone = SelectedZone;

        try
        {
            IsBusy = true;
            var region = new CaptureRegion(
                zone.AbsoluteX,
                zone.AbsoluteY,
                zone.AbsoluteWidth,
                zone.AbsoluteHeight);

            StatusMessage = $"Recognizing text for '{zone.DisplayName}'...";
            var frame = await captureService.CaptureAsync(region);
            UpdateCapturePreview(frame);
            CapturePreviewStatus = $"Captured {frame.Width}x{frame.Height} at {frame.CapturedAt:HH:mm:ss}.";

            var result = await ocrService.RecognizeAsync(
                new OcrRequest(frame, SourceLanguage.Trim(), zone.Id));
            latestOcrPreviewResult = result;
            ReplaceOcrPreviewTextBlocks(result.TextBlocks);
            UpdateVisibleOverlayPreview(result);

            OcrPreviewStatus = result.TextBlocks.Count == 0
                ? $"No text recognized for '{zone.DisplayName}'."
                : $"Recognized {result.TextBlocks.Count} text block(s) for '{zone.DisplayName}'.";
            StatusMessage = OcrPreviewStatus;
            logger.Information($"OCR preview recognized {result.TextBlocks.Count} text block(s) for zone '{zone.DisplayName}'.");
        }
        catch (CaptureFrameSourceException exception)
        {
            logger.Error(exception, "OCR preview capture failed.");
            OcrPreviewStatus = $"OCR preview capture failed: {exception.Message}";
            StatusMessage = OcrPreviewStatus;
            latestOcrPreviewResult = null;
            ReplaceOcrPreviewTextBlocks(Array.Empty<OcrTextBlock>());
        }
        catch (OcrEngineException exception)
        {
            logger.Error(exception, "OCR preview failed.");
            OcrPreviewStatus = $"OCR preview failed: {exception.Message}";
            StatusMessage = OcrPreviewStatus;
            latestOcrPreviewResult = null;
            ReplaceOcrPreviewTextBlocks(Array.Empty<OcrTextBlock>());
        }
        catch (OperationCanceledException)
        {
            OcrPreviewStatus = "OCR preview canceled.";
            StatusMessage = OcrPreviewStatus;
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Unexpected OCR preview failure.");
            OcrPreviewStatus = "OCR preview failed. Check logs for details.";
            StatusMessage = OcrPreviewStatus;
            latestOcrPreviewResult = null;
            ReplaceOcrPreviewTextBlocks(Array.Empty<OcrTextBlock>());
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ShowOverlayPreview()
    {
        try
        {
            var snapshot = CreateOverlayPreviewSnapshot(DateTimeOffset.UtcNow, out var snapshotSource);

            overlayService.Show(snapshot);
            OverlayPreviewStatus = $"Overlay preview shown with {snapshot.TextItems.Count} {snapshotSource} text item(s).";
            StatusMessage = OverlayPreviewStatus;
            OnPropertyChanged(nameof(IsOverlayPreviewVisible));
            NotifyCommandStateChanged();
            logger.Information($"Overlay preview shown with {snapshotSource} text.");
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Overlay preview failed.");
            OverlayPreviewStatus = "Overlay preview failed. Check logs for details.";
            StatusMessage = OverlayPreviewStatus;
            OnPropertyChanged(nameof(IsOverlayPreviewVisible));
            NotifyCommandStateChanged();
        }
    }

    public void HideOverlayPreview()
    {
        try
        {
            overlayService.Hide();
            OverlayPreviewStatus = "Overlay preview hidden.";
            StatusMessage = OverlayPreviewStatus;
            OnPropertyChanged(nameof(IsOverlayPreviewVisible));
            NotifyCommandStateChanged();
            logger.Information("Overlay preview hidden.");
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Overlay preview hide failed.");
            OverlayPreviewStatus = "Overlay preview hide failed. Check logs for details.";
            StatusMessage = OverlayPreviewStatus;
            OnPropertyChanged(nameof(IsOverlayPreviewVisible));
            NotifyCommandStateChanged();
        }
    }

    private OverlaySnapshot CreateOverlayPreviewSnapshot(DateTimeOffset shownAt, out string snapshotSource)
    {
        if (latestOcrPreviewResult is { TextBlocks.Count: > 0 } result)
        {
            snapshotSource = "OCR";
            return overlayPositioningService.CreateSnapshot(result, shownAt);
        }

        snapshotSource = "test";
        return CreateTestOverlaySnapshot(shownAt);
    }

    private static OverlaySnapshot CreateTestOverlaySnapshot(DateTimeOffset shownAt)
    {
        return new OverlaySnapshot(
            new[]
            {
                new OverlayTextItem("Game Translator overlay test", 120, 120, 520, 72),
                new OverlayTextItem("Click-through smoke text", 120, 212, 420, 64),
            },
            shownAt);
    }

    private void UpdateVisibleOverlayPreview(OcrResult result)
    {
        if (!overlayService.IsVisible)
        {
            return;
        }

        var snapshot = overlayPositioningService.CreateSnapshot(result, DateTimeOffset.UtcNow);
        overlayService.Show(snapshot);
        OverlayPreviewStatus = $"Overlay preview updated with {snapshot.TextItems.Count} OCR text item(s).";
        OnPropertyChanged(nameof(IsOverlayPreviewVisible));
        NotifyCommandStateChanged();
        logger.Information("Overlay preview updated from OCR text blocks.");
    }

    private async Task RefreshProfilesAsync(string? preferredProfileId)
    {
        await RunProfileOperationAsync(
            "Loading profiles...",
            async () =>
            {
                var profiles = await profileService.ListAsync();
                ReplaceProfiles(profiles);

                var selected = profiles.FirstOrDefault(profile => profile.Id == preferredProfileId)
                    ?? profiles.FirstOrDefault();

                if (selected is null)
                {
                    BeginCreateProfile();
                    StatusMessage = "No profiles found. Create the first one.";
                    return;
                }

                SelectedProfile = selected;
                pendingSelectedProfileId = null;
                StatusMessage = $"Loaded {Profiles.Count} profile(s).";
            });
    }

    private async Task<bool> RunProfileOperationAsync(string activityMessage, Func<Task> action)
    {
        try
        {
            IsBusy = true;
            StatusMessage = activityMessage;
            await action();
            return true;
        }
        catch (ProfileValidationException exception)
        {
            var message = string.Join(" ", exception.Errors.Select(error => error.Message));
            logger.Warning(message);
            StatusMessage = message;
        }
        catch (ProfileImportException exception)
        {
            logger.Warning(exception.Message);
            StatusMessage = exception.Message;
        }
        catch (ProfileNotFoundException exception)
        {
            logger.Warning(exception.Message);
            StatusMessage = exception.Message;
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Profile manager operation failed.");
            StatusMessage = "Profile operation failed. Check logs for details.";
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }

    private GameProfile BuildProfileFromEditor()
    {
        var existingProfile = SelectedProfile;

        return new GameProfile
        {
            Id = editingProfileId ?? string.Empty,
            SchemaVersion = existingProfile?.SchemaVersion ?? GameProfile.CurrentSchemaVersion,
            Name = ProfileName.Trim(),
            Description = ProfileDescription.Trim(),
            OcrZones = OcrZones.Select(zone => zone.ToModel()).ToArray(),
            OverlaySettings = new OverlaySettings
            {
                MaskMode = OverlayMaskMode,
                MaskColor = OverlayMaskColor.Trim(),
                Opacity = OverlayOpacity,
                Padding = OverlayPadding,
            },
            TranslatorSettings = new TranslatorSettings
            {
                Provider = TranslatorProvider.Trim(),
                SourceLanguage = SourceLanguage.Trim(),
                TargetLanguage = TargetLanguage.Trim(),
            },
        };
    }

    private string BuildCloneName(string sourceName)
    {
        var baseName = string.IsNullOrWhiteSpace(sourceName)
            ? "Profile Copy"
            : $"{sourceName} Copy";
        var candidate = baseName;
        var suffix = 2;

        while (Profiles.Any(profile => string.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} {suffix}";
            suffix++;
        }

        return candidate;
    }

    private void ReplaceProfiles(IEnumerable<GameProfile> profiles)
    {
        Profiles.Clear();

        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }
    }

    private void LoadEditorFromProfile(GameProfile profile)
    {
        RunWithoutPersistingDraftState(() =>
        {
            ProfileName = profile.Name;
            ProfileDescription = profile.Description;
            TranslatorProvider = profile.TranslatorSettings.Provider;
            SourceLanguage = profile.TranslatorSettings.SourceLanguage;
            TargetLanguage = profile.TranslatorSettings.TargetLanguage;
            OverlayMaskMode = profile.OverlaySettings.MaskMode;
            OverlayMaskColor = profile.OverlaySettings.MaskColor;
            OverlayOpacity = profile.OverlaySettings.Opacity;
            OverlayPadding = profile.OverlaySettings.Padding;
            ReplaceZones(profile.OcrZones.Select(OcrZoneEditorViewModel.FromModel));
        });

        SyncSelectedZoneState();
        OnPropertyChanged(nameof(ProfileSummary));
        OnPropertyChanged(nameof(TranslatorSettingsSummary));
        OnPropertyChanged(nameof(ZoneSummary));
        RefreshValidationState();
    }

    private void LoadDraftValues()
    {
        var draftZones = settings.GetValue<OcrZone[]>(DraftOcrZonesSettingKey) ?? Array.Empty<OcrZone>();
        var draftSelectedZoneId = settings.GetValue<string>(DraftSelectedZoneIdSettingKey);

        RunWithoutPersistingDraftState(() =>
        {
            ProfileName = settings.GetValue<string>(DraftProfileNameSettingKey) ?? string.Empty;
            ProfileDescription = settings.GetValue<string>(DraftProfileDescriptionSettingKey) ?? string.Empty;
            TranslatorProvider = settings.GetValue<string>(DraftTranslatorProviderSettingKey) ?? string.Empty;
            SourceLanguage = settings.GetValue<string>(DraftSourceLanguageSettingKey) ?? string.Empty;
            TargetLanguage = settings.GetValue<string>(DraftTargetLanguageSettingKey) ?? string.Empty;
            OverlayMaskMode = settings.GetValue<OverlayMaskMode?>(DraftOverlayMaskModeSettingKey) ?? OverlaySettings.Default.MaskMode;
            OverlayMaskColor = settings.GetValue<string>(DraftOverlayMaskColorSettingKey) ?? OverlaySettings.Default.MaskColor;
            OverlayOpacity = settings.GetValue<double?>(DraftOverlayOpacitySettingKey) ?? OverlaySettings.Default.Opacity;
            OverlayPadding = settings.GetValue<double?>(DraftOverlayPaddingSettingKey) ?? OverlaySettings.Default.Padding;
            ReplaceZones(draftZones.Select(OcrZoneEditorViewModel.FromModel));
            if (!string.IsNullOrWhiteSpace(draftSelectedZoneId))
            {
                SelectedZone = OcrZones.FirstOrDefault(zone => string.Equals(zone.Id, draftSelectedZoneId, StringComparison.Ordinal))
                    ?? OcrZones.FirstOrDefault();
            }
        });

        SyncSelectedZoneState();
        OnPropertyChanged(nameof(ProfileSummary));
        OnPropertyChanged(nameof(TranslatorSettingsSummary));
        OnPropertyChanged(nameof(ZoneSummary));
        RefreshValidationState();
    }

    private bool CanSaveProfile()
    {
        return !IsBusy && !HasValidationErrors;
    }

    private bool CanCloneSelectedProfile()
    {
        return !IsBusy && SelectedProfile is not null;
    }

    private bool CanExportSelectedProfile()
    {
        return !IsBusy && SelectedProfile is not null;
    }

    private bool CanDeleteSelectedProfile()
    {
        return !IsBusy && SelectedProfile is not null;
    }

    private bool CanRemoveSelectedZone()
    {
        return !IsBusy && SelectedZone is not null;
    }

    private bool CanRefreshCapturePreview()
    {
        return !IsBusy && SelectedZone is not null;
    }

    private bool CanRecognizeOcrPreview()
    {
        return !IsBusy
            && SelectedZone is not null
            && !string.IsNullOrWhiteSpace(SourceLanguage);
    }

    private bool CanDuplicateSelectedZone()
    {
        return !IsBusy && SelectedZone is not null;
    }

    private bool CanMoveSelectedZoneUp()
    {
        return !IsBusy
            && SelectedZone is not null
            && OcrZones.IndexOf(SelectedZone) > 0;
    }

    private bool CanMoveSelectedZoneDown()
    {
        return !IsBusy
            && SelectedZone is not null
            && OcrZones.IndexOf(SelectedZone) >= 0
            && OcrZones.IndexOf(SelectedZone) < OcrZones.Count - 1;
    }

    private void ReplaceZones(IEnumerable<OcrZoneEditorViewModel> zones)
    {
        foreach (var zone in OcrZones)
        {
            DetachZone(zone);
        }

        OcrZones.Clear();

        foreach (var zone in zones)
        {
            AttachZone(zone);
            OcrZones.Add(zone);
        }

        SelectedZone = OcrZones.FirstOrDefault();
        SyncSelectedZoneState();
    }

    private void AttachZone(OcrZoneEditorViewModel zone)
    {
        zone.PropertyChanged += OnZonePropertyChanged;
    }

    private void DetachZone(OcrZoneEditorViewModel zone)
    {
        zone.PropertyChanged -= OnZonePropertyChanged;
    }

    private void OnZonePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        PersistDraftShellStateIfNeeded();
        OnPropertyChanged(nameof(ProfileSummary));
        RefreshValidationState();
    }

    private void PersistDraftShellStateIfNeeded()
    {
        if (suppressDraftStatePersistence || !IsDraftEditor)
        {
            return;
        }

        settings.SetValue(DraftProfileNameSettingKey, NormalizeOptionalText(ProfileName));
        settings.SetValue(DraftProfileDescriptionSettingKey, NormalizeOptionalText(ProfileDescription));
        settings.SetValue(DraftTranslatorProviderSettingKey, NormalizeOptionalText(TranslatorProvider));
        settings.SetValue(DraftSourceLanguageSettingKey, NormalizeOptionalText(SourceLanguage));
        settings.SetValue(DraftTargetLanguageSettingKey, NormalizeOptionalText(TargetLanguage));
        settings.SetValue(DraftOverlayMaskModeSettingKey, OverlayMaskMode);
        settings.SetValue(DraftOverlayMaskColorSettingKey, OverlayMaskColor.Trim());
        settings.SetValue(DraftOverlayOpacitySettingKey, OverlayOpacity);
        settings.SetValue(DraftOverlayPaddingSettingKey, OverlayPadding);
        settings.SetValue(DraftOcrZonesSettingKey, OcrZones.Select(zone => zone.ToModel()).ToArray());
        settings.SetValue(DraftSelectedZoneIdSettingKey, SelectedZone?.Id);
    }

    private void RunWithoutPersistingDraftState(Action action)
    {
        suppressDraftStatePersistence = true;

        try
        {
            action();
        }
        finally
        {
            suppressDraftStatePersistence = false;
        }
    }

    private void RefreshValidationState()
    {
        SetErrors(
            nameof(ProfileName),
            string.IsNullOrWhiteSpace(ProfileName)
                ? new[] { "Profile name is required." }
                : Array.Empty<string>());
        SetErrors(
            nameof(TranslatorProvider),
            string.IsNullOrWhiteSpace(TranslatorProvider)
                ? new[] { "Translator provider is required." }
                : Array.Empty<string>());
        SetErrors(
            nameof(SourceLanguage),
            string.IsNullOrWhiteSpace(SourceLanguage)
                ? new[] { "Source language is required." }
                : Array.Empty<string>());
        SetErrors(
            nameof(TargetLanguage),
            string.IsNullOrWhiteSpace(TargetLanguage)
                ? new[] { "Target language is required." }
                : Array.Empty<string>());
        SetErrors(
            nameof(OverlayMaskColor),
            !HexColorPattern.IsMatch(OverlayMaskColor.Trim())
                ? new[] { "Overlay mask color must use #RRGGBB or #AARRGGBB format." }
                : Array.Empty<string>());
        SetErrors(
            nameof(OverlayOpacity),
            OverlayOpacity is < 0 or > 1
                ? new[] { "Overlay opacity must be between 0 and 1." }
                : Array.Empty<string>());
        SetErrors(
            nameof(OverlayPadding),
            OverlayPadding < 0
                ? new[] { "Overlay padding must be zero or greater." }
                : Array.Empty<string>());

        var overlapErrorsByZoneId = OcrZones.ToDictionary(zone => zone.Id, _ => new List<string>(), StringComparer.Ordinal);

        for (var first = 0; first < OcrZones.Count; first++)
        {
            var firstZone = OcrZones[first];
            var firstBounds = new AbsoluteRectangle(
                firstZone.AbsoluteX,
                firstZone.AbsoluteY,
                firstZone.AbsoluteWidth,
                firstZone.AbsoluteHeight);

            for (var second = first + 1; second < OcrZones.Count; second++)
            {
                var secondZone = OcrZones[second];
                var secondBounds = new AbsoluteRectangle(
                    secondZone.AbsoluteX,
                    secondZone.AbsoluteY,
                    secondZone.AbsoluteWidth,
                    secondZone.AbsoluteHeight);

                if (!firstBounds.Intersects(secondBounds))
                {
                    continue;
                }

                var overlapMessage = $"OCR zones '{firstZone.DisplayName}' and '{secondZone.DisplayName}' overlap.";
                overlapErrorsByZoneId[firstZone.Id].Add(overlapMessage);
                overlapErrorsByZoneId[secondZone.Id].Add(overlapMessage);
            }
        }

        foreach (var zone in OcrZones)
        {
            zone.SetAbsoluteOverlapErrors(overlapErrorsByZoneId[zone.Id]);
        }

        ReplaceValidationErrors(
            GetErrors(null).Cast<string>()
                .Concat(OcrZones.SelectMany(zone => zone.GetErrors(null).Cast<string>()))
                .Distinct(StringComparer.Ordinal));
    }

    private void ReplaceValidationErrors(IEnumerable<string> errors)
    {
        ValidationErrors.Clear();

        foreach (var error in errors.Distinct(StringComparer.Ordinal))
        {
            ValidationErrors.Add(error);
        }

        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(IsEditorValid));
        NotifyCommandStateChanged();
    }

    private static string? NormalizeOptionalText(string value)
    {
        var normalized = value.Trim();

        return normalized.Length == 0 ? null : normalized;
    }

    private string BuildDuplicateZoneName(string sourceName)
    {
        var baseName = string.IsNullOrWhiteSpace(sourceName)
            ? "Zone Copy"
            : $"{sourceName.Trim()} Copy";
        var candidate = baseName;
        var suffix = 2;

        while (OcrZones.Any(zone => string.Equals(zone.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} {suffix}";
            suffix++;
        }

        return candidate;
    }

    private string BuildImportedProfileName(string sourceName)
    {
        var baseName = string.IsNullOrWhiteSpace(sourceName)
            ? "Imported Profile"
            : sourceName.Trim();
        var candidate = baseName;
        var suffix = 2;

        while (Profiles.Any(profile => string.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} Imported {suffix}";
            suffix++;
        }

        return candidate;
    }

    private GameProfile? FindProfileByName(string profileName)
    {
        var normalizedName = profileName.Trim();
        if (normalizedName.Length == 0)
        {
            return null;
        }

        return Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<GameProfile> SaveImportedProfileAsync(
        GameProfile importedProfile,
        GameProfile? conflictingProfile,
        ProfileImportConflictPolicy conflictPolicy)
    {
        var profileName = conflictPolicy == ProfileImportConflictPolicy.ReplaceExisting && conflictingProfile is not null
            ? importedProfile.Name.Trim()
            : BuildImportedProfileName(importedProfile.Name);

        var profileToPersist = importedProfile with
        {
            Id = conflictPolicy == ProfileImportConflictPolicy.ReplaceExisting && conflictingProfile is not null
                ? conflictingProfile.Id
                : string.Empty,
            Name = profileName,
            OcrZones = importedProfile.OcrZones
                .Select(zone => zone with { Id = Guid.NewGuid().ToString("N") })
                .ToArray(),
        };

        return conflictPolicy == ProfileImportConflictPolicy.ReplaceExisting && conflictingProfile is not null
            ? await profileService.UpdateAsync(profileToPersist)
            : await profileService.CreateAsync(profileToPersist);
    }

    private static string BuildDefaultExportFileName(string profileName)
    {
        var baseName = string.IsNullOrWhiteSpace(profileName)
            ? "game-translator-profile"
            : profileName.Trim();
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitizedName = new string(baseName.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray());

        return $"{sanitizedName}.json";
    }

    private void MoveSelectedZoneBy(int offset)
    {
        if (SelectedZone is null)
        {
            return;
        }

        var currentIndex = OcrZones.IndexOf(SelectedZone);
        if (currentIndex < 0)
        {
            return;
        }

        var targetIndex = currentIndex + offset;
        if (targetIndex < 0 || targetIndex >= OcrZones.Count)
        {
            return;
        }

        OcrZones.Move(currentIndex, targetIndex);
        PersistDraftShellStateIfNeeded();
        OnPropertyChanged(nameof(ZoneSummary));
        OnPropertyChanged(nameof(ProfileSummary));
        RefreshValidationState();
    }

    private void SyncSelectedZoneState()
    {
        foreach (var zone in OcrZones)
        {
            zone.IsSelected = ReferenceEquals(zone, SelectedZone);
        }
    }

    private void ApplySurfaceBoundsToZone(
        OcrZoneEditorViewModel zone,
        double surfaceX,
        double surfaceY,
        double surfaceWidth,
        double surfaceHeight)
    {
        var left = Math.Clamp(surfaceX, 0, ZoneSurfaceWidth - 1);
        var top = Math.Clamp(surfaceY, 0, ZoneSurfaceHeight - 1);
        var right = Math.Clamp(left + Math.Max(1, surfaceWidth), left + 1, ZoneSurfaceWidth);
        var bottom = Math.Clamp(top + Math.Max(1, surfaceHeight), top + 1, ZoneSurfaceHeight);

        var absoluteLeft = ConvertSurfaceToAbsolute(left, OcrZoneEditorViewModel.ReferenceSurfaceWidth, ZoneSurfaceWidth);
        var absoluteTop = ConvertSurfaceToAbsolute(top, OcrZoneEditorViewModel.ReferenceSurfaceHeight, ZoneSurfaceHeight);
        var absoluteRight = ConvertSurfaceToAbsolute(right, OcrZoneEditorViewModel.ReferenceSurfaceWidth, ZoneSurfaceWidth);
        var absoluteBottom = ConvertSurfaceToAbsolute(bottom, OcrZoneEditorViewModel.ReferenceSurfaceHeight, ZoneSurfaceHeight);

        zone.AbsoluteX = absoluteLeft;
        zone.AbsoluteY = absoluteTop;
        zone.AbsoluteWidth = Math.Max(1, absoluteRight - absoluteLeft);
        zone.AbsoluteHeight = Math.Max(1, absoluteBottom - absoluteTop);
        var relativeX = RoundRelativeCoordinate(left / ZoneSurfaceWidth);
        var relativeY = RoundRelativeCoordinate(top / ZoneSurfaceHeight);
        var relativeWidth = RoundRelativeCoordinate((right - left) / ZoneSurfaceWidth);
        var relativeHeight = RoundRelativeCoordinate((bottom - top) / ZoneSurfaceHeight);

        zone.RelativeX = relativeX;
        zone.RelativeY = relativeY;
        zone.RelativeWidth = ClampRelativeSizeToBounds(relativeX, relativeWidth);
        zone.RelativeHeight = ClampRelativeSizeToBounds(relativeY, relativeHeight);
    }

    private void UpdateZoneSelectionPreview(double left, double top, double width, double height)
    {
        ZoneSelectionPreviewX = left;
        ZoneSelectionPreviewY = top;
        ZoneSelectionPreviewWidth = width;
        ZoneSelectionPreviewHeight = height;
        OnPropertyChanged(nameof(HasZoneSelectionPreview));
    }

    private void ClearZoneSelectionPreview()
    {
        isZoneSelectionActive = false;
        UpdateZoneSelectionPreview(0, 0, 0, 0);
    }

    private void ClearZoneResizeState()
    {
        isZoneResizeActive = false;
    }

    private void ClearSurfaceInteractionState()
    {
        ClearZoneSelectionPreview();
        ClearZoneResizeState();
    }

    private void ClearCapturePreview()
    {
        CapturePreviewImage = null;
        CapturePreviewWidth = 0;
        CapturePreviewHeight = 0;
        CapturePreviewStatus = SelectedZone is null
            ? "Select an OCR zone to preview capture."
            : "No capture preview yet.";
        CaptureRefreshMetricsSummary = SelectedZone is null
            ? "Select an OCR zone to measure capture refresh."
            : "Refresh rate not measured.";
        ClearOcrPreview();
    }

    private void ClearOcrPreview()
    {
        latestOcrPreviewResult = null;
        ReplaceOcrPreviewTextBlocks(Array.Empty<OcrTextBlock>());
        OcrPreviewStatus = SelectedZone is null
            ? "Select an OCR zone to recognize text."
            : "No OCR preview yet.";
    }

    private void ReplaceOcrPreviewTextBlocks(IEnumerable<OcrTextBlock> textBlocks)
    {
        OcrPreviewTextBlocks.Clear();
        OcrDebugTextBlocks.Clear();

        foreach (var textBlock in textBlocks)
        {
            OcrPreviewTextBlocks.Add(textBlock);
            OcrDebugTextBlocks.Add(new OcrDebugTextBlockViewModel(textBlock));
        }

        OnPropertyChanged(nameof(HasOcrPreview));
        OnPropertyChanged(nameof(OcrPreviewText));
    }

    private void UpdateCapturePreview(CapturedFrame frame)
    {
        CapturePreviewImage = CreateCapturePreviewImage(frame);
        CapturePreviewWidth = frame.Width;
        CapturePreviewHeight = frame.Height;
    }

    private static BitmapSource CreateCapturePreviewImage(CapturedFrame frame)
    {
        var image = BitmapSource.Create(
            frame.Width,
            frame.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            frame.PixelData.ToArray(),
            frame.Stride);
        image.Freeze();

        return image;
    }

    private static string FormatCaptureRefreshMetrics(CaptureRefreshMetrics metrics)
    {
        var result = metrics.MeetsTarget ? "meets target" : "below target";

        return $"{metrics.CapturedFrameCount} frames in {metrics.Elapsed.TotalMilliseconds:F0} ms | {metrics.FramesPerSecond:F1} FPS ({result}, target {metrics.TargetFramesPerSecond}+).";
    }

    private static int ConvertSurfaceToAbsolute(double coordinate, int referenceSize, double surfaceSize)
    {
        return (int)Math.Round(coordinate * referenceSize / surfaceSize, MidpointRounding.AwayFromZero);
    }

    private static double ConvertAbsoluteToSurface(int coordinate, int referenceSize, double surfaceSize)
    {
        return coordinate * surfaceSize / referenceSize;
    }

    private static double RoundRelativeCoordinate(double value)
    {
        return Math.Round(value, 4, MidpointRounding.AwayFromZero);
    }

    private static double ClampRelativeSizeToBounds(double relativePosition, double relativeSize)
    {
        return Math.Max(0.0001, Math.Min(relativeSize, RoundRelativeCoordinate(1 - relativePosition)));
    }

    private bool IsDraftEditor => SelectedProfile is null && string.IsNullOrWhiteSpace(editingProfileId);

    private void NotifyCommandStateChanged()
    {
        ((RelayCommand)BeginCreateProfileCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RefreshProfilesCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)SaveProfileCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)ImportProfileCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)ExportSelectedProfileCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)CloneSelectedProfileCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)DeleteSelectedProfileCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ResetEditorCommand).RaiseCanExecuteChanged();
        ((RelayCommand)AddZoneCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DuplicateSelectedZoneCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveSelectedZoneUpCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveSelectedZoneDownCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RemoveSelectedZoneCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RefreshCapturePreviewCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)MeasureCaptureRefreshCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RecognizeOcrPreviewCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ShowOverlayPreviewCommand).RaiseCanExecuteChanged();
        ((RelayCommand)HideOverlayPreviewCommand).RaiseCanExecuteChanged();
    }
}
