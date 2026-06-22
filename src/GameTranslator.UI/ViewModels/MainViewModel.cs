using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using GameTranslator.Application.Abstractions;
using GameTranslator.Application.Cache;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Credentials;
using GameTranslator.Application.Debug;
using GameTranslator.Application.Hotkeys;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Pipeline;
using GameTranslator.Application.Profiles;
using GameTranslator.Application.Updates;
using GameTranslator.Domain.Profiles;
using GameTranslator.UI.Commands;
using GameTranslator.UI.Services;

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
    private const string DraftOcrEngineSettingKey = "shell.draft.ocr.engine";
    private const string DraftOcrOrientationModeSettingKey = "shell.draft.ocr.orientationMode";
    private const string DraftOcrPreprocessingEnabledSettingKey = "shell.draft.ocr.preprocessing.enabled";
    private const string DraftOcrPreprocessingContrastSettingKey = "shell.draft.ocr.preprocessing.contrast";
    private const string DraftOcrPreprocessingBrightnessSettingKey = "shell.draft.ocr.preprocessing.brightness";
    private const string DraftOcrPreprocessingSharpnessSettingKey = "shell.draft.ocr.preprocessing.sharpness";
    private const string DraftOcrPreprocessingThresholdingSettingKey = "shell.draft.ocr.preprocessing.thresholding";
    private const string DraftOcrPreprocessingThresholdSettingKey = "shell.draft.ocr.preprocessing.threshold";
    private const string DraftOcrPreprocessingScaleSettingKey = "shell.draft.ocr.preprocessing.scale";
    private const string DraftOcrPreprocessingNoiseReductionSettingKey = "shell.draft.ocr.preprocessing.noiseReduction";
    private const string DraftOcrZonesSettingKey = "shell.draft.ocrZones";
    private const string DraftSelectedZoneIdSettingKey = "shell.draft.selectedZoneId";
    private const string DebugOverlayEnabledSettingKey = "debug.overlay.enabled";
    private static readonly Regex HexColorPattern = new("^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);
    private static readonly string[] SupportedOcrEngines =
    {
        OcrSettings.WindowsEngineId,
        OcrSettings.TesseractEngineId,
    };

    private static readonly string[] SupportedTranslatorProviders =
    {
        "Google",
        "Azure",
        "Yandex",
        "WebAuto",
        "GoogleWeb",
        "BingWeb",
        "YandexWeb",
    };

    private static readonly OcrOrientationMode[] SupportedOcrOrientations =
    {
        OcrOrientationMode.Auto,
        OcrOrientationMode.Horizontal,
        OcrOrientationMode.Vertical,
    };

    private static readonly TimeSpan LiveTranslationPollingInterval = TimeSpan.FromMilliseconds(300);

    private static readonly TranslationPipelineRunOptions LiveTranslationRunOptions = new(
        requireStableTextBeforeTranslation: true,
        stableTextInterval: TimeSpan.FromSeconds(1),
        preservePreviousOverlayWhileWaitingForStableText: true);

    private readonly ProfileService profileService;
    private readonly ProfileExchangeService profileExchangeService;
    private readonly CaptureService captureService;
    private readonly OcrService ocrService;
    private readonly TranslatorCredentialService credentialService;
    private readonly TranslationPipelineService translationPipelineService;
    private readonly TranslationCacheService translationCacheService;
    private readonly ApplicationUpdateService applicationUpdateService;
    private readonly IOverlayService overlayService;
    private readonly OverlayPositioningService overlayPositioningService;
    private readonly IDialogService dialogService;
    private readonly IScreenRegionPickerService screenRegionPickerService;
    private readonly ISettingsService settings;
    private readonly IApplicationLogger logger;
    private readonly GlobalHotkeyService globalHotkeyService;
    private readonly DebugMetricFormatter debugMetricFormatter;
    private readonly IDebugResourceMonitor debugResourceMonitor;

    private string? pendingSelectedProfileId;
    private string? editingProfileId;
    private GameProfile? selectedProfile;
    private string profileName = string.Empty;
    private string profileDescription = string.Empty;
    private string translatorProvider = string.Empty;
    private string sourceLanguage = string.Empty;
    private string targetLanguage = string.Empty;
    private string ocrEngine = OcrSettings.Default.Engine;
    private OcrOrientationMode ocrOrientationMode = OcrSettings.Default.OrientationMode;
    private string translatorCredentialSecret = string.Empty;
    private string translatorCredentialProjectId = string.Empty;
    private string translatorCredentialLocation = "global";
    private string translatorCredentialEndpoint = TranslatorCredentialService.GetDefaultEndpoint("Google");
    private string translatorCredentialStatus = "Translator credentials not checked.";
    private bool hasStoredTranslatorCredentials;
    private OverlayMaskMode overlayMaskMode = OverlayMaskMode.Solid;
    private string overlayMaskColor = "#000000";
    private double overlayOpacity = 1;
    private double overlayPadding;
    private bool ocrPreprocessingEnabled;
    private double ocrPreprocessingContrast = OcrPreprocessingSettings.Default.Contrast;
    private int ocrPreprocessingBrightness = OcrPreprocessingSettings.Default.Brightness;
    private double ocrPreprocessingSharpness = OcrPreprocessingSettings.Default.Sharpness;
    private bool ocrPreprocessingThresholdingEnabled;
    private int ocrPreprocessingThreshold = OcrPreprocessingSettings.Default.Threshold;
    private double ocrPreprocessingScale = OcrPreprocessingSettings.Default.Scale;
    private bool ocrPreprocessingNoiseReductionEnabled;
    private OcrZoneEditorViewModel? selectedZone;
    private OcrResult? latestOcrPreviewResult;
    private CaptureRefreshMetrics? latestCaptureRefreshMetrics;
    private ImageSource? capturePreviewImage;
    private string capturePreviewStatus = "No capture preview yet.";
    private string captureRefreshMetricsSummary = "Refresh rate not measured.";
    private string ocrPreviewStatus = "No OCR preview yet.";
    private string overlayPreviewStatus = "Overlay preview hidden.";
    private string pipelineStatus = "Full translation pipeline not run yet.";
    private string translationCacheStatus = "Translation cache not cleaned yet.";
    private string updateStatus = "Update check not run yet.";
    private string globalHotkeyStatus = "Global hotkeys not registered yet.";
    private string debugOverlayStatus = "Debug overlay disabled.";
    private int capturePreviewWidth;
    private int capturePreviewHeight;
    private string statusMessage = "Loading profiles...";
    private bool isBusy;
    private bool isLiveTranslationRunning;
    private bool isLoaded;
    private bool isDebugOverlayEnabled;
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
    private CancellationTokenSource? liveTranslationCancellation;

    public MainViewModel(
        ProfileService profileService,
        ProfileExchangeService profileExchangeService,
        CaptureService captureService,
        OcrService ocrService,
        TranslatorCredentialService credentialService,
        TranslationPipelineService translationPipelineService,
        TranslationCacheService translationCacheService,
        ApplicationUpdateService applicationUpdateService,
        GlobalHotkeyService globalHotkeyService,
        DebugMetricFormatter debugMetricFormatter,
        IDebugResourceMonitor debugResourceMonitor,
        IOverlayService overlayService,
        OverlayPositioningService overlayPositioningService,
        IDialogService dialogService,
        ISettingsService settings,
        IApplicationLogger logger)
        : this(
            profileService,
            profileExchangeService,
            captureService,
            ocrService,
            credentialService,
            translationPipelineService,
            translationCacheService,
            applicationUpdateService,
            globalHotkeyService,
            debugMetricFormatter,
            debugResourceMonitor,
            overlayService,
            overlayPositioningService,
            dialogService,
            new UnavailableScreenRegionPickerService(),
            settings,
            logger)
    {
    }

    public MainViewModel(
        ProfileService profileService,
        ProfileExchangeService profileExchangeService,
        CaptureService captureService,
        OcrService ocrService,
        TranslatorCredentialService credentialService,
        TranslationPipelineService translationPipelineService,
        TranslationCacheService translationCacheService,
        ApplicationUpdateService applicationUpdateService,
        GlobalHotkeyService globalHotkeyService,
        DebugMetricFormatter debugMetricFormatter,
        IDebugResourceMonitor debugResourceMonitor,
        IOverlayService overlayService,
        OverlayPositioningService overlayPositioningService,
        IDialogService dialogService,
        IScreenRegionPickerService screenRegionPickerService,
        ISettingsService settings,
        IApplicationLogger logger)
    {
        this.profileService = profileService;
        this.profileExchangeService = profileExchangeService;
        this.captureService = captureService;
        this.ocrService = ocrService;
        this.credentialService = credentialService;
        this.translationPipelineService = translationPipelineService;
        this.translationCacheService = translationCacheService;
        this.applicationUpdateService = applicationUpdateService;
        this.globalHotkeyService = globalHotkeyService;
        this.debugMetricFormatter = debugMetricFormatter;
        this.debugResourceMonitor = debugResourceMonitor;
        this.overlayService = overlayService;
        this.overlayPositioningService = overlayPositioningService;
        this.dialogService = dialogService;
        this.screenRegionPickerService = screenRegionPickerService;
        this.settings = settings;
        this.logger = logger;
        globalHotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
        pendingSelectedProfileId = settings.GetValue<string>(SelectedProfileSettingKey);
        isDebugOverlayEnabled = settings.GetValue<bool?>(DebugOverlayEnabledSettingKey) ?? false;

        Profiles = new ObservableCollection<GameProfile>();
        OcrZones = new ObservableCollection<OcrZoneEditorViewModel>();
        OcrPreviewTextBlocks = new ObservableCollection<OcrTextBlock>();
        OcrDebugTextBlocks = new ObservableCollection<OcrDebugTextBlockViewModel>();
        HotkeyBindings = new ObservableCollection<HotkeyBindingViewModel>();
        ValidationErrors = new ObservableCollection<string>();
        OverlayMaskModes = Enum.GetValues<OverlayMaskMode>();
        TranslatorProviderOptions = SupportedTranslatorProviders;
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
        PickScreenZoneCommand = new RelayCommand(PickScreenZone, CanPickScreenZone);
        DuplicateSelectedZoneCommand = new RelayCommand(DuplicateSelectedZone, CanDuplicateSelectedZone);
        MoveSelectedZoneUpCommand = new RelayCommand(MoveSelectedZoneUp, CanMoveSelectedZoneUp);
        MoveSelectedZoneDownCommand = new RelayCommand(MoveSelectedZoneDown, CanMoveSelectedZoneDown);
        RemoveSelectedZoneCommand = new RelayCommand(RemoveSelectedZone, CanRemoveSelectedZone);
        RefreshCapturePreviewCommand = new AsyncRelayCommand(RefreshCapturePreviewAsync, CanRefreshCapturePreview);
        MeasureCaptureRefreshCommand = new AsyncRelayCommand(MeasureCaptureRefreshAsync, CanRefreshCapturePreview);
        RecognizeOcrPreviewCommand = new AsyncRelayCommand(RecognizeOcrPreviewAsync, CanRecognizeOcrPreview);
        RunTranslationPipelineCommand = new AsyncRelayCommand(RunTranslationPipelineAsync, CanRunTranslationPipeline);
        StartLiveTranslationCommand = new AsyncRelayCommand(StartLiveTranslationAsync, CanStartLiveTranslation);
        StopLiveTranslationCommand = new RelayCommand(StopLiveTranslation, () => IsLiveTranslationRunning);
        CleanupTranslationCacheCommand = new AsyncRelayCommand(CleanupTranslationCacheAsync, () => !IsBusy);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsBusy);
        ApplyGlobalHotkeysCommand = new RelayCommand(ApplyGlobalHotkeys, CanApplyGlobalHotkeys);
        ResetGlobalHotkeysCommand = new RelayCommand(ResetGlobalHotkeys, () => !IsBusy);
        ShowOverlayPreviewCommand = new RelayCommand(ShowOverlayPreview, () => !IsBusy);
        HideOverlayPreviewCommand = new RelayCommand(HideOverlayPreview, () => !IsBusy && IsOverlayPreviewVisible);
        SaveTranslatorCredentialsCommand = new AsyncRelayCommand(SaveTranslatorCredentialsAsync, CanSaveTranslatorCredentials);
        ValidateTranslatorCredentialsCommand = new AsyncRelayCommand(ValidateTranslatorCredentialsAsync, CanSelectTranslatorProvider);
        DeleteTranslatorCredentialsCommand = new AsyncRelayCommand(DeleteTranslatorCredentialsAsync, CanSelectTranslatorProvider);

        BeginCreateProfile();
        StatusMessage = "Ready to manage game profiles.";
    }

    public string ApplicationName => "Game Translator";

    public string CurrentStage => "Sprint 26";

    public double ZoneSurfaceWidth => OcrZoneEditorViewModel.PreviewSurfaceWidth;

    public double ZoneSurfaceHeight => OcrZoneEditorViewModel.PreviewSurfaceHeight;

    public string ZoneSurfaceSummary => $"Reference surface {OcrZoneEditorViewModel.ReferenceSurfaceWidth}x{OcrZoneEditorViewModel.ReferenceSurfaceHeight}";

    public ObservableCollection<GameProfile> Profiles { get; }

    public ObservableCollection<OcrZoneEditorViewModel> OcrZones { get; }

    public ObservableCollection<OcrTextBlock> OcrPreviewTextBlocks { get; }

    public ObservableCollection<OcrDebugTextBlockViewModel> OcrDebugTextBlocks { get; }

    public ObservableCollection<HotkeyBindingViewModel> HotkeyBindings { get; }

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
                RefreshTranslatorCredentialDefaults();
                _ = ValidateTranslatorCredentialsAsync();
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

    public IReadOnlyList<string> OcrEngines => SupportedOcrEngines;

    public IReadOnlyList<OcrOrientationMode> OcrOrientations => SupportedOcrOrientations;

    public string OcrEngine
    {
        get => ocrEngine;
        set
        {
            if (SetProperty(ref ocrEngine, value))
            {
                OnPropertyChanged(nameof(ProfileSummary));
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
                NotifyCommandStateChanged();
            }
        }
    }

    public OcrOrientationMode OcrOrientationMode
    {
        get => ocrOrientationMode;
        set
        {
            if (SetProperty(ref ocrOrientationMode, value))
            {
                OnPropertyChanged(nameof(ProfileSummary));
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
            }
        }
    }

    public string TranslatorCredentialSecret
    {
        get => translatorCredentialSecret;
        set
        {
            if (SetProperty(ref translatorCredentialSecret, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public string TranslatorCredentialProjectId
    {
        get => translatorCredentialProjectId;
        set
        {
            if (SetProperty(ref translatorCredentialProjectId, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public string TranslatorCredentialLocation
    {
        get => translatorCredentialLocation;
        set
        {
            if (SetProperty(ref translatorCredentialLocation, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public string TranslatorCredentialEndpoint
    {
        get => translatorCredentialEndpoint;
        set
        {
            if (SetProperty(ref translatorCredentialEndpoint, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public string TranslatorCredentialStatus
    {
        get => translatorCredentialStatus;
        private set => SetProperty(ref translatorCredentialStatus, value);
    }

    public bool HasStoredTranslatorCredentials
    {
        get => hasStoredTranslatorCredentials;
        private set => SetProperty(ref hasStoredTranslatorCredentials, value);
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


    public bool OcrPreprocessingEnabled
    {
        get => ocrPreprocessingEnabled;
        set
        {
            if (SetProperty(ref ocrPreprocessingEnabled, value))
            {
                PersistDraftShellStateIfNeeded();
                OnPropertyChanged(nameof(ProfileSummary));
            }
        }
    }

    public double OcrPreprocessingContrast
    {
        get => ocrPreprocessingContrast;
        set
        {
            if (SetProperty(ref ocrPreprocessingContrast, value))
            {
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
            }
        }
    }

    public int OcrPreprocessingBrightness
    {
        get => ocrPreprocessingBrightness;
        set
        {
            if (SetProperty(ref ocrPreprocessingBrightness, value))
            {
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
            }
        }
    }

    public double OcrPreprocessingSharpness
    {
        get => ocrPreprocessingSharpness;
        set
        {
            if (SetProperty(ref ocrPreprocessingSharpness, value))
            {
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
            }
        }
    }

    public bool OcrPreprocessingThresholdingEnabled
    {
        get => ocrPreprocessingThresholdingEnabled;
        set
        {
            if (SetProperty(ref ocrPreprocessingThresholdingEnabled, value))
            {
                PersistDraftShellStateIfNeeded();
                OnPropertyChanged(nameof(ProfileSummary));
            }
        }
    }

    public int OcrPreprocessingThreshold
    {
        get => ocrPreprocessingThreshold;
        set
        {
            if (SetProperty(ref ocrPreprocessingThreshold, value))
            {
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
            }
        }
    }

    public double OcrPreprocessingScale
    {
        get => ocrPreprocessingScale;
        set
        {
            if (SetProperty(ref ocrPreprocessingScale, value))
            {
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
            }
        }
    }

    public bool OcrPreprocessingNoiseReductionEnabled
    {
        get => ocrPreprocessingNoiseReductionEnabled;
        set
        {
            if (SetProperty(ref ocrPreprocessingNoiseReductionEnabled, value))
            {
                PersistDraftShellStateIfNeeded();
                OnPropertyChanged(nameof(ProfileSummary));
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

    public bool IsLiveTranslationRunning
    {
        get => isLiveTranslationRunning;
        private set
        {
            if (SetProperty(ref isLiveTranslationRunning, value))
            {
                OnPropertyChanged(nameof(IsLiveTranslationStopped));
                NotifyCommandStateChanged();
            }
        }
    }

    public bool IsLiveTranslationStopped => !IsLiveTranslationRunning;

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

            return $"schema {schemaVersion} | {TranslatorSettingsSummary} | OCR {OcrEngine}/{OcrOrientationMode} | zones {OcrZones.Count} | overlay {OverlayMaskMode} | preprocess {(OcrPreprocessingEnabled ? "on" : "off")}";
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

    public string PipelineStatus
    {
        get => pipelineStatus;
        private set => SetProperty(ref pipelineStatus, value);
    }

    public string TranslationCacheStatus
    {
        get => translationCacheStatus;
        private set => SetProperty(ref translationCacheStatus, value);
    }

    public string UpdateStatus
    {
        get => updateStatus;
        private set => SetProperty(ref updateStatus, value);
    }

    public string GlobalHotkeyStatus
    {
        get => globalHotkeyStatus;
        private set => SetProperty(ref globalHotkeyStatus, value);
    }

    public bool HasGlobalHotkeyValidationErrors => HotkeyBindings.Any(binding => binding.HasErrors);

    public bool IsDebugOverlayEnabled
    {
        get => isDebugOverlayEnabled;
        set
        {
            if (SetProperty(ref isDebugOverlayEnabled, value))
            {
                settings.SetValue(DebugOverlayEnabledSettingKey, value);
                DebugOverlayStatus = value ? "Debug overlay enabled." : "Debug overlay disabled.";
            }
        }
    }

    public string DebugOverlayStatus
    {
        get => debugOverlayStatus;
        private set => SetProperty(ref debugOverlayStatus, value);
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

    public ICommand PickScreenZoneCommand { get; }

    public ICommand DuplicateSelectedZoneCommand { get; }

    public ICommand MoveSelectedZoneUpCommand { get; }

    public ICommand MoveSelectedZoneDownCommand { get; }

    public ICommand RemoveSelectedZoneCommand { get; }

    public ICommand RefreshCapturePreviewCommand { get; }

    public ICommand MeasureCaptureRefreshCommand { get; }

    public ICommand RecognizeOcrPreviewCommand { get; }

    public ICommand RunTranslationPipelineCommand { get; }

    public ICommand StartLiveTranslationCommand { get; }

    public ICommand StopLiveTranslationCommand { get; }

    public ICommand CleanupTranslationCacheCommand { get; }

    public ICommand CheckForUpdatesCommand { get; }

    public ICommand ApplyGlobalHotkeysCommand { get; }

    public ICommand ResetGlobalHotkeysCommand { get; }

    public ICommand ShowOverlayPreviewCommand { get; }

    public ICommand HideOverlayPreviewCommand { get; }

    public ICommand SaveTranslatorCredentialsCommand { get; }

    public ICommand ValidateTranslatorCredentialsCommand { get; }

    public ICommand DeleteTranslatorCredentialsCommand { get; }

    public async Task SaveTranslatorCredentialsAsync()
    {
        if (!CanSelectTranslatorProvider())
        {
            TranslatorCredentialStatus = "Select a translator provider to save credentials.";
            return;
        }

        if (!TranslatorCredentialService.RequiresStoredCredentials(TranslatorProvider))
        {
            TranslatorCredentialStatus = $"{TranslatorCredentialService.NormalizeProvider(TranslatorProvider)} is experimental and does not use stored credentials.";
            StatusMessage = TranslatorCredentialStatus;
            return;
        }

        if (!CanSaveTranslatorCredentials())
        {
            TranslatorCredentialStatus = "Translator credential fields are incomplete.";
            return;
        }

        try
        {
            IsBusy = true;
            await credentialService.SaveAsync(
                TranslatorProvider,
                TranslatorCredentialSecret,
                TranslatorCredentialProjectId,
                TranslatorCredentialLocation,
                TranslatorCredentialEndpoint);

            TranslatorCredentialSecret = string.Empty;
            HasStoredTranslatorCredentials = true;
            TranslatorCredentialStatus = $"Stored translator credentials for {TranslatorCredentialService.NormalizeProvider(TranslatorProvider)}.";
            StatusMessage = TranslatorCredentialStatus;
        }
        catch (ArgumentException exception)
        {
            logger.Warning(exception.Message);
            TranslatorCredentialStatus = exception.Message;
            StatusMessage = "Translator credentials were not saved.";
        }
        catch (CredentialStorageException exception)
        {
            logger.Warning(exception.Message);
            TranslatorCredentialStatus = exception.Message;
            StatusMessage = "Translator credentials were not saved.";
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Translator credential save failed.");
            TranslatorCredentialStatus = "Translator credential save failed. Check logs for details.";
            StatusMessage = "Translator credentials were not saved.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ValidateTranslatorCredentialsAsync()
    {
        if (!CanSelectTranslatorProvider())
        {
            HasStoredTranslatorCredentials = false;
            TranslatorCredentialStatus = "Select a translator provider to check credentials.";
            return;
        }

        try
        {
            if (!TranslatorCredentialService.RequiresStoredCredentials(TranslatorProvider))
            {
                var provider = TranslatorCredentialService.NormalizeProvider(TranslatorProvider);
                TranslatorCredentialEndpoint = TranslatorCredentialService.GetDefaultEndpoint(provider);
                TranslatorCredentialSecret = string.Empty;
                HasStoredTranslatorCredentials = true;
                TranslatorCredentialStatus = $"{provider} is experimental and does not use stored credentials.";
                return;
            }

            var record = await credentialService.ReadAsync(TranslatorProvider);
            if (record is null)
            {
                HasStoredTranslatorCredentials = false;
                TranslatorCredentialStatus = $"No stored translator credentials for {TranslatorCredentialService.NormalizeProvider(TranslatorProvider)}.";
                return;
            }

            TranslatorCredentialProjectId = record.ProjectId;
            TranslatorCredentialLocation = record.Location;
            TranslatorCredentialEndpoint = record.Endpoint.ToString();
            HasStoredTranslatorCredentials = !string.IsNullOrWhiteSpace(record.AccessToken)
                && !string.IsNullOrWhiteSpace(record.ProjectId)
                && !string.IsNullOrWhiteSpace(record.Location)
                && record.Endpoint.IsAbsoluteUri;
            TranslatorCredentialStatus = HasStoredTranslatorCredentials
                ? $"Stored translator credentials found for {record.Provider}."
                : $"Stored translator credentials for {record.Provider} are incomplete.";
        }
        catch (CredentialStorageException exception)
        {
            logger.Warning(exception.Message);
            HasStoredTranslatorCredentials = false;
            TranslatorCredentialStatus = exception.Message;
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Translator credential validation failed.");
            HasStoredTranslatorCredentials = false;
            TranslatorCredentialStatus = "Translator credential validation failed. Check logs for details.";
        }
    }

    public async Task DeleteTranslatorCredentialsAsync()
    {
        if (!CanSelectTranslatorProvider())
        {
            TranslatorCredentialStatus = "Select a translator provider to delete credentials.";
            return;
        }

        if (!TranslatorCredentialService.RequiresStoredCredentials(TranslatorProvider))
        {
            TranslatorCredentialSecret = string.Empty;
            HasStoredTranslatorCredentials = true;
            TranslatorCredentialStatus = $"{TranslatorCredentialService.NormalizeProvider(TranslatorProvider)} is experimental and does not store credentials.";
            StatusMessage = TranslatorCredentialStatus;
            return;
        }

        try
        {
            IsBusy = true;
            await credentialService.DeleteAsync(TranslatorProvider);
            TranslatorCredentialSecret = string.Empty;
            HasStoredTranslatorCredentials = false;
            TranslatorCredentialStatus = $"Deleted translator credentials for {TranslatorCredentialService.NormalizeProvider(TranslatorProvider)}.";
            StatusMessage = TranslatorCredentialStatus;
        }
        catch (CredentialStorageException exception)
        {
            logger.Warning(exception.Message);
            TranslatorCredentialStatus = exception.Message;
            StatusMessage = "Translator credentials were not deleted.";
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Translator credential delete failed.");
            TranslatorCredentialStatus = "Translator credential delete failed. Check logs for details.";
            StatusMessage = "Translator credentials were not deleted.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadAsync()
    {
        if (isLoaded)
        {
            return;
        }

        isLoaded = true;
        LoadHotkeyBindings(globalHotkeyService.LoadConfiguredHotkeys());
        UpdateGlobalHotkeyStatus(globalHotkeyService.RegisterConfiguredHotkeys());
        await RefreshProfilesAsync();
        _ = CheckForUpdatesOnStartupAsync();
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

    public void PickScreenZone()
    {
        if (!CanPickScreenZone())
        {
            return;
        }

        ClearSurfaceInteractionState();

        try
        {
            StatusMessage = "Select an OCR zone on the screen.";
            var selection = screenRegionPickerService.PickRegion();
            if (selection is null)
            {
                StatusMessage = "Screen zone selection canceled.";
                return;
            }

            var zone = OcrZoneEditorViewModel.CreateDefault(OcrZones.Count + 1);
            AttachZone(zone);
            OcrZones.Add(zone);
            ApplyScreenBoundsToZone(zone, selection);
            SelectedZone = zone;
            PersistDraftShellStateIfNeeded();
            OnPropertyChanged(nameof(ZoneSummary));
            OnPropertyChanged(nameof(ProfileSummary));
            RefreshValidationState();
            StatusMessage = $"Created zone '{zone.DisplayName}' from screen selection.";
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Screen zone selection failed.");
            StatusMessage = "Screen zone selection failed. Check logs for details.";
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
            latestCaptureRefreshMetrics = result.Metrics;
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
        var overlayWasVisibleBeforeCapture = false;
        OverlaySnapshot? overlaySnapshotBeforeCapture = null;

        try
        {
            IsBusy = true;
            overlayWasVisibleBeforeCapture = overlayService.IsVisible;
            overlaySnapshotBeforeCapture = await HideOverlayPreviewForCaptureAsync();

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
                new OcrRequest(frame, SourceLanguage.Trim(), zone.Id, BuildOcrPreprocessingSettings(), OcrEngine, OcrOrientationMode));
            latestOcrPreviewResult = result;
            ReplaceOcrPreviewTextBlocks(result.TextBlocks);
            UpdateVisibleOverlayPreview(result, overlayWasVisibleBeforeCapture);

            OcrPreviewStatus = result.TextBlocks.Count == 0
                ? $"No text recognized for '{zone.DisplayName}'."
                : $"Recognized {result.TextBlocks.Count} text block(s) for '{zone.DisplayName}'.";
            StatusMessage = OcrPreviewStatus;
            logger.Information($"OCR preview recognized {result.TextBlocks.Count} text block(s) for zone '{zone.DisplayName}'.");
        }
        catch (CaptureFrameSourceException exception)
        {
            RestoreOverlayPreviewAfterFailedCapture(overlayWasVisibleBeforeCapture, overlaySnapshotBeforeCapture);
            logger.Error(exception, "OCR preview capture failed.");
            OcrPreviewStatus = $"OCR preview capture failed: {exception.Message}";
            StatusMessage = OcrPreviewStatus;
            latestOcrPreviewResult = null;
            ReplaceOcrPreviewTextBlocks(Array.Empty<OcrTextBlock>());
        }
        catch (OcrEngineException exception)
        {
            RestoreOverlayPreviewAfterFailedCapture(overlayWasVisibleBeforeCapture, overlaySnapshotBeforeCapture);
            logger.Error(exception, "OCR preview failed.");
            OcrPreviewStatus = $"OCR preview failed: {exception.Message}";
            StatusMessage = OcrPreviewStatus;
            latestOcrPreviewResult = null;
            ReplaceOcrPreviewTextBlocks(Array.Empty<OcrTextBlock>());
        }
        catch (OperationCanceledException)
        {
            RestoreOverlayPreviewAfterFailedCapture(overlayWasVisibleBeforeCapture, overlaySnapshotBeforeCapture);
            OcrPreviewStatus = "OCR preview canceled.";
            StatusMessage = OcrPreviewStatus;
        }
        catch (Exception exception)
        {
            RestoreOverlayPreviewAfterFailedCapture(overlayWasVisibleBeforeCapture, overlaySnapshotBeforeCapture);
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

    public async Task RunTranslationPipelineAsync()
    {
        RefreshValidationState();
        if (OcrZones.Count == 0)
        {
            PipelineStatus = "Add at least one OCR zone before running the full pipeline.";
            StatusMessage = PipelineStatus;
            return;
        }

        if (HasValidationErrors)
        {
            PipelineStatus = ValidationErrors[0];
            StatusMessage = PipelineStatus;
            return;
        }

        var overlayWasVisibleBeforeCapture = false;
        OverlaySnapshot? overlaySnapshotBeforeCapture = null;

        try
        {
            IsBusy = true;
            overlayWasVisibleBeforeCapture = overlayService.IsVisible;
            overlaySnapshotBeforeCapture = await HideOverlayPreviewForCaptureAsync();

            var profile = BuildProfileFromEditor();

            PipelineStatus = $"Running full pipeline for {profile.OcrZones.Count} OCR zone(s)...";
            StatusMessage = PipelineStatus;

            var result = await translationPipelineService.RunAllZonesAsync(
                profile,
                overlaySnapshotBeforeCapture);

            ApplyBatchPipelineResult(profile, result, isLiveMode: false);
            logger.Information($"Full pipeline completed for profile '{profile.Name}' across {result.SucceededZoneCount}/{result.TotalZoneCount} OCR zones.");
        }
        catch (TranslationPipelineException exception)
        {
            RestoreOverlayPreviewAfterFailedCapture(overlayWasVisibleBeforeCapture, overlaySnapshotBeforeCapture);
            logger.Error(exception, "Full translation pipeline failed.");
            PipelineStatus = exception.Message;
            StatusMessage = PipelineStatus;
            latestOcrPreviewResult = null;
            ReplaceOcrPreviewTextBlocks(Array.Empty<OcrTextBlock>());
        }
        catch (OperationCanceledException)
        {
            RestoreOverlayPreviewAfterFailedCapture(overlayWasVisibleBeforeCapture, overlaySnapshotBeforeCapture);
            PipelineStatus = "Full translation pipeline canceled.";
            StatusMessage = PipelineStatus;
        }
        catch (Exception exception)
        {
            RestoreOverlayPreviewAfterFailedCapture(overlayWasVisibleBeforeCapture, overlaySnapshotBeforeCapture);
            logger.Error(exception, "Unexpected full translation pipeline failure.");
            PipelineStatus = "Full translation pipeline failed. Check logs for details.";
            StatusMessage = PipelineStatus;
            latestOcrPreviewResult = null;
            ReplaceOcrPreviewTextBlocks(Array.Empty<OcrTextBlock>());
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StartLiveTranslationAsync()
    {
        RefreshValidationState();
        if (IsLiveTranslationRunning)
        {
            PipelineStatus = "Live translation is already running.";
            StatusMessage = PipelineStatus;
            return;
        }

        if (OcrZones.Count == 0)
        {
            PipelineStatus = "Add at least one OCR zone before starting live translation.";
            StatusMessage = PipelineStatus;
            return;
        }

        if (HasValidationErrors)
        {
            PipelineStatus = ValidationErrors[0];
            StatusMessage = PipelineStatus;
            return;
        }

        var profile = BuildProfileFromEditor();
        var cancellationSource = new CancellationTokenSource();
        liveTranslationCancellation = cancellationSource;
        IsLiveTranslationRunning = true;
        PipelineStatus = $"Live translation running for {profile.OcrZones.Count} OCR zone(s). Waiting for stable text...";
        StatusMessage = PipelineStatus;
        _ = RunLiveTranslationLoopAsync(profile, cancellationSource);

        await Task.Yield();
    }

    public void StopLiveTranslation()
    {
        if (!IsLiveTranslationRunning || liveTranslationCancellation is null)
        {
            return;
        }

        PipelineStatus = "Stopping live translation...";
        StatusMessage = PipelineStatus;
        liveTranslationCancellation.Cancel();
        NotifyCommandStateChanged();
    }

    private async Task RunLiveTranslationLoopAsync(GameProfile profile, CancellationTokenSource cancellationSource)
    {
        var cancellationToken = cancellationSource.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var overlayWasVisibleBeforeCapture = false;
                OverlaySnapshot? overlaySnapshotBeforeCapture = null;

                try
                {
                    overlayWasVisibleBeforeCapture = overlayService.IsVisible;
                    overlaySnapshotBeforeCapture = await HideOverlayPreviewForCaptureAsync(notifyUi: false);

                    var result = await translationPipelineService.RunAllZonesAsync(
                        profile,
                        overlaySnapshotBeforeCapture,
                        LiveTranslationRunOptions,
                        cancellationToken);

                    ApplyBatchPipelineResult(profile, result, isLiveMode: true);
                    await Task.Delay(LiveTranslationPollingInterval, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (TranslationPipelineException exception)
                {
                    RestoreOverlayPreviewAfterFailedCapture(overlayWasVisibleBeforeCapture, overlaySnapshotBeforeCapture);
                    logger.Error(exception, "Live translation pipeline failed.");
                    PipelineStatus = exception.Message;
                    StatusMessage = PipelineStatus;
                    await DelayAfterLiveTranslationFailureAsync(cancellationToken);
                }
                catch (Exception exception)
                {
                    RestoreOverlayPreviewAfterFailedCapture(overlayWasVisibleBeforeCapture, overlaySnapshotBeforeCapture);
                    logger.Error(exception, "Unexpected live translation pipeline failure.");
                    PipelineStatus = "Live translation failed. Check logs for details.";
                    StatusMessage = PipelineStatus;
                    await DelayAfterLiveTranslationFailureAsync(cancellationToken);
                }
            }
        }
        finally
        {
            if (ReferenceEquals(liveTranslationCancellation, cancellationSource))
            {
                liveTranslationCancellation.Dispose();
                liveTranslationCancellation = null;
                IsLiveTranslationRunning = false;
                PipelineStatus = "Live translation stopped.";
                StatusMessage = PipelineStatus;
                NotifyCommandStateChanged();
            }
        }
    }

    private static async Task DelayAfterLiveTranslationFailureAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async Task CleanupTranslationCacheAsync()
    {
        try
        {
            IsBusy = true;
            TranslationCacheStatus = "Cleaning expired translation cache entries...";
            StatusMessage = TranslationCacheStatus;

            var result = await translationCacheService.CleanupExpiredAsync(DateTimeOffset.UtcNow);

            TranslationCacheStatus = $"Translation cache cleanup removed {result.TotalEntryCount} expired entr{(result.TotalEntryCount == 1 ? "y" : "ies")}.";
            StatusMessage = TranslationCacheStatus;
            logger.Information($"Translation cache cleanup removed {result.TotalEntryCount} expired entries.");
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Translation cache cleanup failed.");
            TranslationCacheStatus = "Translation cache cleanup failed. Check logs for details.";
            StatusMessage = TranslationCacheStatus;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task CheckForUpdatesAsync()
    {
        return CheckForUpdatesAsync(ApplicationUpdateCheckMode.Manual);
    }

    public Task CheckForUpdatesOnStartupAsync()
    {
        return CheckForUpdatesAsync(ApplicationUpdateCheckMode.Startup);
    }

    private async Task CheckForUpdatesAsync(ApplicationUpdateCheckMode checkMode)
    {
        try
        {
            IsBusy = true;
            UpdateStatus = checkMode == ApplicationUpdateCheckMode.Startup
                ? "Checking for application updates at startup..."
                : "Checking for application updates...";
            StatusMessage = UpdateStatus;

            var result = await applicationUpdateService.CheckForUpdatesAsync(checkMode);

            UpdateStatus = result.RestartRecommended
                ? $"{result.Message} Restart the app to use an applied update."
                : result.Message;
            StatusMessage = UpdateStatus;
            logger.Information($"Application update check completed: {result.Status}.");
        }
        catch (OperationCanceledException)
        {
            UpdateStatus = "Application update check canceled.";
            StatusMessage = UpdateStatus;
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Application update check failed.");
            UpdateStatus = "Application update check failed. Check logs for details.";
            StatusMessage = UpdateStatus;
        }
        finally
        {
            IsBusy = false;
        }
    }


    public void ApplyGlobalHotkeys()
    {
        if (!CanApplyGlobalHotkeys())
        {
            GlobalHotkeyStatus = "Fix invalid global hotkeys before applying.";
            StatusMessage = GlobalHotkeyStatus;
            return;
        }

        try
        {
            var bindings = HotkeyBindings.Select(binding => binding.ToModel()).ToArray();
            globalHotkeyService.SaveConfiguredHotkeys(bindings);
            var result = globalHotkeyService.RegisterHotkeys(bindings);
            UpdateGlobalHotkeyStatus(result);
        }
        catch (Exception exception)
        {
            logger.Warning(exception.Message);
            GlobalHotkeyStatus = "Global hotkeys were not applied. Check values and try again.";
            StatusMessage = GlobalHotkeyStatus;
        }
    }

    public void ResetGlobalHotkeys()
    {
        LoadHotkeyBindings(globalHotkeyService.DefaultHotkeys);
        ApplyGlobalHotkeys();
    }

    private void LoadHotkeyBindings(IEnumerable<GlobalHotkeyBinding> bindings)
    {
        foreach (var binding in HotkeyBindings)
        {
            binding.PropertyChanged -= OnHotkeyBindingPropertyChanged;
        }

        HotkeyBindings.Clear();

        foreach (var binding in bindings.Select(HotkeyBindingViewModel.FromModel))
        {
            binding.PropertyChanged += OnHotkeyBindingPropertyChanged;
            HotkeyBindings.Add(binding);
        }

        OnPropertyChanged(nameof(HasGlobalHotkeyValidationErrors));
        NotifyCommandStateChanged();
    }

    private void OnHotkeyBindingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasGlobalHotkeyValidationErrors));
        NotifyCommandStateChanged();
    }

    private bool CanApplyGlobalHotkeys()
    {
        return !IsBusy && HotkeyBindings.Count > 0 && !HasGlobalHotkeyValidationErrors;
    }

    private void UpdateGlobalHotkeyStatus(GlobalHotkeyConfigurationResult result)
    {
        var conflictDetails = result.Statuses
            .Where(status => !status.IsRegistered)
            .Select(status => status.ErrorCode is null
                ? status.Message
                : $"{status.Message} (Win32 error {status.ErrorCode})")
            .ToArray();

        GlobalHotkeyStatus = conflictDetails.Length == 0
            ? result.Summary
            : $"{result.Summary} {string.Join(" ", conflictDetails)}";
        StatusMessage = GlobalHotkeyStatus;
        logger.Information(GlobalHotkeyStatus);
    }

    private void OnGlobalHotkeyPressed(object? sender, GlobalHotkeyPressedEventArgs e)
    {
        _ = HandleGlobalHotkeyAsync(e.Action);
    }

    private async Task HandleGlobalHotkeyAsync(GlobalHotkeyAction action)
    {
        switch (action)
        {
            case GlobalHotkeyAction.StartPausePipeline:
                if (IsLiveTranslationRunning)
                {
                    StopLiveTranslation();
                    return;
                }

                if (IsBusy)
                {
                    StatusMessage = "Pipeline hotkey received while an operation is already running.";
                    return;
                }

                await StartLiveTranslationAsync();
                break;
            case GlobalHotkeyAction.RecognizeOcrPreview:
                if (IsBusy)
                {
                    StatusMessage = "OCR hotkey received while an operation is already running.";
                    return;
                }

                await RecognizeOcrPreviewAsync();
                break;
            case GlobalHotkeyAction.ToggleOverlay:
                if (IsOverlayPreviewVisible)
                {
                    HideOverlayPreview();
                }
                else
                {
                    ShowOverlayPreview();
                }

                break;
            case GlobalHotkeyAction.ShowSettings:
                ShowMainWindowFromHotkey();
                break;
            case GlobalHotkeyAction.ExitApplication:
                System.Windows.Application.Current.Shutdown();
                break;
        }
    }

    private void ShowMainWindowFromHotkey()
    {
        var window = System.Windows.Application.Current.MainWindow;
        if (window is null)
        {
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
        StatusMessage = "Settings hotkey focused the main window.";
    }

    private async Task<OverlaySnapshot?> HideOverlayPreviewForCaptureAsync(bool notifyUi = true)
    {
        if (!overlayService.IsVisible)
        {
            return null;
        }

        var snapshot = overlayService.CurrentSnapshot;
        overlayService.Hide();
        if (notifyUi)
        {
            OnPropertyChanged(nameof(IsOverlayPreviewVisible));
            NotifyCommandStateChanged();
        }

        await Task.Yield();
        return snapshot;
    }

    public void ShowOverlayPreview()
    {
        try
        {
            var snapshot = CreateOverlayPreviewSnapshot(DateTimeOffset.UtcNow, out var snapshotSource);
            if (IsDebugOverlayEnabled)
            {
                snapshot = CreatePreviewDebugOverlaySnapshot(snapshot, snapshotSource);
            }

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

    private void ApplyBatchPipelineResult(
        GameProfile profile,
        TranslationPipelineBatchResult result,
        bool isLiveMode)
    {
        var previewEntries = CreateBatchOcrPreviewEntries(result, profile);
        var previewEntry = SelectBatchOcrPreviewEntry(previewEntries);
        if (previewEntry is not null)
        {
            var recognizedBlockCount = previewEntries.Sum(entry => entry.SourceOcrResult.TextBlocks.Count);
            UpdateCapturePreview(previewEntry.CapturedFrame);
            latestOcrPreviewResult = previewEntry.SourceOcrResult;
            ReplaceBatchOcrPreviewTextBlocks(previewEntries, previewEntry.ZoneId);
            CapturePreviewStatus = $"Captured {previewEntry.CapturedFrame.Width}x{previewEntry.CapturedFrame.Height} for '{previewEntry.ZoneName}' at {previewEntry.CapturedFrame.CapturedAt:HH:mm:ss}.";
            OcrPreviewStatus = recognizedBlockCount == 0
                ? $"No text recognized across {previewEntries.Count} OCR zone(s)."
                : $"Recognized {recognizedBlockCount} text block(s) across {previewEntries.Count} OCR zone(s). Preview image shows '{previewEntry.ZoneName}'.";
        }
        else
        {
            latestOcrPreviewResult = null;
            ReplaceOcrPreviewTextBlocks(Array.Empty<OcrTextBlock>());
            CapturePreviewStatus = "No OCR zone captured successfully.";
            OcrPreviewStatus = "No OCR results available.";
        }

        var overlaySnapshot = IsDebugOverlayEnabled
            ? CreateDebugOverlaySnapshot(result.OverlaySnapshot, result)
            : result.OverlaySnapshot;
        if (IsDebugOverlayEnabled)
        {
            overlayService.Show(overlaySnapshot);
        }

        var isWaitingForStableText = isLiveMode
            && result.RecognizedBlockCount > 0
            && result.TranslatedBlockCount == 0
            && result.SkippedTranslationCount > 0;
        OverlayPreviewStatus = isWaitingForStableText
            ? "Live translation waiting for stable OCR text; keeping previous overlay."
            : IsDebugOverlayEnabled
                ? $"{(isLiveMode ? "Live translation" : "Full pipeline")} debug overlay shown with {overlaySnapshot.DebugItems.Count} OCR box(es)."
                : $"{(isLiveMode ? "Live translation" : "Full pipeline")} overlay shown with {result.OverlaySnapshot.TextItems.Count} translated text item(s).";
        PipelineStatus = isLiveMode
            ? CreateLivePipelineStatus(result)
            : CreateBatchPipelineStatus(result);
        StatusMessage = PipelineStatus;
        OnPropertyChanged(nameof(IsOverlayPreviewVisible));
        NotifyCommandStateChanged();
    }


    private OverlaySnapshot CreateDebugOverlaySnapshot(
        OverlaySnapshot baseSnapshot,
        TranslationPipelineResult result,
        string zoneName)
    {
        var debugItems = result.SourceOcrResult.TextBlocks
            .Select((block, index) => CreateDebugItem(
                block,
                result.TranslateResponse?.TranslatedTexts.ElementAtOrDefault(index) ?? string.Empty))
            .ToArray();
        var metrics = new DebugMetricSnapshot(
            zoneName,
            debugItems.Length,
            result.TranslatedBlockCount,
            result.Timings.CaptureElapsed,
            result.Timings.OcrElapsed,
            result.Timings.TranslationElapsed,
            result.Timings.OverlayElapsed,
            result.Timings.TotalElapsed,
            latestCaptureRefreshMetrics?.FramesPerSecond,
            debugResourceMonitor.Sample(),
            result.CacheResult?.HitCount ?? 0,
            result.CacheResult?.MissCount ?? 0,
            skippedOcrCount: result.Optimization.OcrSkipped ? 1 : 0,
            skippedTranslationCount: result.Optimization.TranslationSkipped ? 1 : 0,
            debouncedZoneCount: result.Optimization.Debounced ? 1 : 0,
            frameDifferenceRatio: result.Optimization.FrameDifferenceRatio);
        var metricLines = debugMetricFormatter.Format(metrics);
        DebugOverlayStatus = $"Debug overlay shows {debugItems.Length} OCR box(es), timings, resources, and cache metrics.";

        return CreateSnapshotWithDebug(baseSnapshot, debugItems, metricLines);
    }


    private OverlaySnapshot CreateDebugOverlaySnapshot(
        OverlaySnapshot baseSnapshot,
        TranslationPipelineBatchResult result)
    {
        var debugItems = baseSnapshot.TextItems
            .Select(item => new OverlayDebugItem(item.Text, item.Text, item.X, item.Y, item.Width, item.Height))
            .ToArray();
        var metrics = new DebugMetricSnapshot(
            $"{result.SucceededZoneCount}/{result.TotalZoneCount} OCR zones",
            debugItems.Length,
            result.TranslatedBlockCount,
            SumElapsed(result.ZoneResults, zoneResult => zoneResult.Timings.CaptureElapsed),
            SumElapsed(result.ZoneResults, zoneResult => zoneResult.Timings.OcrElapsed),
            SumElapsed(result.ZoneResults, zoneResult => zoneResult.Timings.TranslationElapsed),
            SumElapsed(result.ZoneResults, zoneResult => zoneResult.Timings.OverlayElapsed),
            SumElapsed(result.ZoneResults, zoneResult => zoneResult.Timings.TotalElapsed),
            latestCaptureRefreshMetrics?.FramesPerSecond,
            debugResourceMonitor.Sample(),
            result.ZoneResults.Sum(zoneResult => zoneResult.CacheResult?.HitCount ?? 0),
            result.ZoneResults.Sum(zoneResult => zoneResult.CacheResult?.MissCount ?? 0),
            skippedOcrCount: result.SkippedOcrCount,
            skippedTranslationCount: result.SkippedTranslationCount,
            debouncedZoneCount: result.DebouncedZoneCount,
            frameDifferenceRatio: result.AverageFrameDifferenceRatio);
        var metricLines = debugMetricFormatter.Format(metrics);
        DebugOverlayStatus = $"Debug overlay shows {debugItems.Length} OCR box(es), timings, resources, and cache metrics across {result.SucceededZoneCount} zone(s).";

        return CreateSnapshotWithDebug(baseSnapshot, debugItems, metricLines);
    }

    private BatchOcrPreviewEntry? SelectBatchOcrPreviewEntry(IReadOnlyList<BatchOcrPreviewEntry> entries)
    {
        return SelectedZone is null
            ? entries.FirstOrDefault()
            : entries.FirstOrDefault(entry => string.Equals(entry.ZoneId, SelectedZone.Id, StringComparison.Ordinal))
                ?? entries.FirstOrDefault();
    }

    private static IReadOnlyList<BatchOcrPreviewEntry> CreateBatchOcrPreviewEntries(
        TranslationPipelineBatchResult result,
        GameProfile profile)
    {
        var entries = new List<BatchOcrPreviewEntry>();

        foreach (var zoneResult in result.ZoneResults)
        {
            entries.Add(new BatchOcrPreviewEntry(
                zoneResult.ZoneId,
                ResolveZoneName(profile, zoneResult.ZoneId),
                zoneResult.CapturedFrame,
                zoneResult.SourceOcrResult));
        }

        foreach (var failure in result.ZoneFailures)
        {
            if (failure.CapturedFrame is null || failure.SourceOcrResult is null)
            {
                continue;
            }

            entries.Add(new BatchOcrPreviewEntry(
                failure.ZoneId,
                string.IsNullOrWhiteSpace(failure.ZoneName) ? ResolveZoneName(profile, failure.ZoneId) : failure.ZoneName,
                failure.CapturedFrame,
                failure.SourceOcrResult));
        }

        var zoneOrder = profile.OcrZones
            .Select((zone, index) => new { zone.Id, Index = index })
            .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);

        return entries
            .OrderBy(entry => zoneOrder.TryGetValue(entry.ZoneId, out var index) ? index : int.MaxValue)
            .ThenBy(entry => entry.ZoneName, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ResolveZoneName(GameProfile profile, string zoneId)
    {
        return profile.OcrZones.FirstOrDefault(zone => string.Equals(zone.Id, zoneId, StringComparison.Ordinal))?.Name
            ?? zoneId;
    }

    private sealed record BatchOcrPreviewEntry(
        string ZoneId,
        string ZoneName,
        CapturedFrame CapturedFrame,
        OcrResult SourceOcrResult);

    private static string CreateLivePipelineStatus(TranslationPipelineBatchResult result)
    {
        if (result.SucceededZoneCount == 0)
        {
            var firstFailure = result.ZoneFailures.FirstOrDefault();
            return firstFailure is null
                ? "Live translation running: no OCR zones completed."
                : $"Live translation waiting after '{firstFailure.ZoneName}' failed during {firstFailure.Stage}. {CreatePipelineFailureDetail(firstFailure)}";
        }

        var status = result.RecognizedBlockCount switch
        {
            0 => $"Live translation running across {result.SucceededZoneCount} OCR zone(s): no text recognized.",
            _ when result.TranslatedBlockCount == 0 && result.SkippedTranslationCount > 0 =>
                $"Live translation waiting for stable OCR text ({result.RecognizedBlockCount} text block(s) recognized).",
            _ => $"Live translation updated {result.TranslatedBlockCount} translated text block(s) across {result.SucceededZoneCount} OCR zone(s).",
        };

        return result.HasFailures
            ? $"{status} {result.FailedZoneCount} of {result.TotalZoneCount} zone(s) failed."
            : status;
    }

    private static string CreateBatchPipelineStatus(TranslationPipelineBatchResult result)
    {
        if (result.SucceededZoneCount == 0)
        {
            var firstFailure = result.ZoneFailures.FirstOrDefault();
            if (firstFailure is null)
            {
                return $"Full pipeline failed for all {result.TotalZoneCount} OCR zone(s).";
            }

            var zoneName = string.IsNullOrWhiteSpace(firstFailure.ZoneName)
                ? firstFailure.ZoneId
                : firstFailure.ZoneName;

            var ocrSummary = result.RecognizedBlockCount == 0
                ? string.Empty
                : $" OCR recognized {result.RecognizedBlockCount} text block(s) before the failure.";

            return $"Full pipeline failed for all {result.TotalZoneCount} OCR zone(s). First failure: '{zoneName}' failed during {firstFailure.Stage}. {CreatePipelineFailureDetail(firstFailure)}{ocrSummary}";
        }

        var translatedStatus = result.RecognizedBlockCount == 0
            ? $"Full pipeline completed for {result.SucceededZoneCount} OCR zone(s) with no recognized text"
            : $"Full pipeline translated {result.TranslatedBlockCount} text block(s) across {result.SucceededZoneCount} OCR zone(s)";

        var status = result.HasFailures
            ? $"{translatedStatus}; {result.FailedZoneCount} of {result.TotalZoneCount} zone(s) failed."
            : $"{translatedStatus}.";

        return result.SkippedOcrCount == 0
            ? status
            : status.TrimEnd('.') + $"; skipped OCR/translation for {result.SkippedOcrCount} unchanged zone(s).";
    }

    private static string CreatePipelineFailureDetail(TranslationPipelineZoneFailure failure)
    {
        var innerMessage = failure.Exception.InnerException?.Message;
        if (string.IsNullOrWhiteSpace(innerMessage)
            || string.Equals(innerMessage, failure.Message, StringComparison.Ordinal))
        {
            return failure.Message;
        }

        return $"{failure.Message} {innerMessage.Trim()}";
    }

    private static TimeSpan SumElapsed(
        IEnumerable<TranslationPipelineResult> results,
        Func<TranslationPipelineResult, TimeSpan> selector)
    {
        return TimeSpan.FromTicks(results.Sum(result => selector(result).Ticks));
    }

    private OverlaySnapshot CreateOcrDebugOverlaySnapshot(OverlaySnapshot baseSnapshot, OcrResult result)
    {
        var debugItems = result.TextBlocks
            .Select(block => CreateDebugItem(block, string.Empty))
            .ToArray();
        var metrics = new DebugMetricSnapshot(
            SelectedZone?.DisplayName ?? "OCR preview",
            debugItems.Length,
            0,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            latestCaptureRefreshMetrics?.FramesPerSecond,
            debugResourceMonitor.Sample(),
            0,
            0);
        var metricLines = debugMetricFormatter.Format(metrics);
        DebugOverlayStatus = $"Debug overlay shows {debugItems.Length} OCR box(es).";

        return CreateSnapshotWithDebug(baseSnapshot, debugItems, metricLines);
    }

    private OverlaySnapshot CreatePreviewDebugOverlaySnapshot(OverlaySnapshot baseSnapshot, string snapshotSource)
    {
        var debugItems = baseSnapshot.TextItems
            .Select(item => new OverlayDebugItem(item.Text, string.Empty, item.X, item.Y, item.Width, item.Height))
            .ToArray();
        var metrics = new DebugMetricSnapshot(
            $"{snapshotSource} preview",
            debugItems.Length,
            baseSnapshot.TextItems.Count,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            latestCaptureRefreshMetrics?.FramesPerSecond,
            debugResourceMonitor.Sample(),
            0,
            0);
        var metricLines = debugMetricFormatter.Format(metrics);
        DebugOverlayStatus = $"Debug overlay preview shows {debugItems.Length} box(es).";

        return CreateSnapshotWithDebug(baseSnapshot, debugItems, metricLines);
    }

    private static OverlayDebugItem CreateDebugItem(OcrTextBlock block, string translatedText)
    {
        return new OverlayDebugItem(
            block.Text,
            translatedText,
            block.Bounds.X,
            block.Bounds.Y,
            block.Bounds.Width,
            block.Bounds.Height);
    }

    private static OverlaySnapshot CreateSnapshotWithDebug(
        OverlaySnapshot baseSnapshot,
        IReadOnlyList<OverlayDebugItem> debugItems,
        IReadOnlyList<string> metricLines)
    {
        return new OverlaySnapshot(
            baseSnapshot.TextItems,
            baseSnapshot.ShownAt,
            baseSnapshot.OverlaySettings,
            baseSnapshot.MaskItems,
            debugItems,
            metricLines);
    }
    private void UpdateVisibleOverlayPreview(OcrResult result, bool showWhenHidden = false)
    {
        if (!overlayService.IsVisible && !showWhenHidden)
        {
            return;
        }

        var snapshot = overlayPositioningService.CreateSnapshot(
            result,
            DateTimeOffset.UtcNow,
            overlayService.CurrentSnapshot);
        overlayService.Show(snapshot);
        OverlayPreviewStatus = $"Overlay preview updated with {snapshot.TextItems.Count} OCR text item(s).";
        OnPropertyChanged(nameof(IsOverlayPreviewVisible));
        NotifyCommandStateChanged();
        logger.Information("Overlay preview updated from OCR text blocks.");
    }

    private void RestoreOverlayPreviewAfterFailedCapture(bool wasVisible, OverlaySnapshot? snapshot)
    {
        if (!wasVisible)
        {
            return;
        }

        try
        {
            if (snapshot is not null)
            {
                overlayService.Show(snapshot);
            }

            OnPropertyChanged(nameof(IsOverlayPreviewVisible));
            NotifyCommandStateChanged();
            logger.Information("Overlay preview restored after OCR capture did not complete.");
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Overlay preview restore failed after OCR capture.");
            OverlayPreviewStatus = "Overlay preview restore failed. Check logs for details.";
            OnPropertyChanged(nameof(IsOverlayPreviewVisible));
            NotifyCommandStateChanged();
        }
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
            OcrSettings = new OcrSettings
            {
                Engine = OcrEngine.Trim(),
                OrientationMode = OcrOrientationMode,
            },
            OcrPreprocessingSettings = BuildOcrPreprocessingSettings(),
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


    private OcrPreprocessingSettings BuildOcrPreprocessingSettings()
    {
        return new OcrPreprocessingSettings
        {
            IsEnabled = OcrPreprocessingEnabled,
            Contrast = OcrPreprocessingContrast,
            Brightness = OcrPreprocessingBrightness,
            Sharpness = OcrPreprocessingSharpness,
            ThresholdingEnabled = OcrPreprocessingThresholdingEnabled,
            Threshold = (byte)Math.Clamp(OcrPreprocessingThreshold, byte.MinValue, byte.MaxValue),
            Scale = OcrPreprocessingScale,
            NoiseReductionEnabled = OcrPreprocessingNoiseReductionEnabled,
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
            OcrEngine = string.IsNullOrWhiteSpace(profile.OcrSettings.Engine)
                ? OcrSettings.Default.Engine
                : profile.OcrSettings.Engine;
            OcrOrientationMode = OcrSettings.IsSupportedOrientationMode(profile.OcrSettings.OrientationMode)
                ? profile.OcrSettings.OrientationMode
                : OcrSettings.Default.OrientationMode;
            OverlayMaskMode = profile.OverlaySettings.MaskMode;
            OverlayMaskColor = profile.OverlaySettings.MaskColor;
            OverlayOpacity = profile.OverlaySettings.Opacity;
            OverlayPadding = profile.OverlaySettings.Padding;
            OcrPreprocessingEnabled = profile.OcrPreprocessingSettings.IsEnabled;
            OcrPreprocessingContrast = profile.OcrPreprocessingSettings.Contrast;
            OcrPreprocessingBrightness = profile.OcrPreprocessingSettings.Brightness;
            OcrPreprocessingSharpness = profile.OcrPreprocessingSettings.Sharpness;
            OcrPreprocessingThresholdingEnabled = profile.OcrPreprocessingSettings.ThresholdingEnabled;
            OcrPreprocessingThreshold = profile.OcrPreprocessingSettings.Threshold;
            OcrPreprocessingScale = profile.OcrPreprocessingSettings.Scale;
            OcrPreprocessingNoiseReductionEnabled = profile.OcrPreprocessingSettings.NoiseReductionEnabled;
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
            OcrEngine = settings.GetValue<string>(DraftOcrEngineSettingKey) ?? OcrSettings.Default.Engine;
            OcrOrientationMode = settings.GetValue<OcrOrientationMode?>(DraftOcrOrientationModeSettingKey) ?? OcrSettings.Default.OrientationMode;
            OverlayMaskMode = settings.GetValue<OverlayMaskMode?>(DraftOverlayMaskModeSettingKey) ?? OverlaySettings.Default.MaskMode;
            OverlayMaskColor = settings.GetValue<string>(DraftOverlayMaskColorSettingKey) ?? OverlaySettings.Default.MaskColor;
            OverlayOpacity = settings.GetValue<double?>(DraftOverlayOpacitySettingKey) ?? OverlaySettings.Default.Opacity;
            OverlayPadding = settings.GetValue<double?>(DraftOverlayPaddingSettingKey) ?? OverlaySettings.Default.Padding;
            OcrPreprocessingEnabled = settings.GetValue<bool?>(DraftOcrPreprocessingEnabledSettingKey) ?? OcrPreprocessingSettings.Default.IsEnabled;
            OcrPreprocessingContrast = settings.GetValue<double?>(DraftOcrPreprocessingContrastSettingKey) ?? OcrPreprocessingSettings.Default.Contrast;
            OcrPreprocessingBrightness = settings.GetValue<int?>(DraftOcrPreprocessingBrightnessSettingKey) ?? OcrPreprocessingSettings.Default.Brightness;
            OcrPreprocessingSharpness = settings.GetValue<double?>(DraftOcrPreprocessingSharpnessSettingKey) ?? OcrPreprocessingSettings.Default.Sharpness;
            OcrPreprocessingThresholdingEnabled = settings.GetValue<bool?>(DraftOcrPreprocessingThresholdingSettingKey) ?? OcrPreprocessingSettings.Default.ThresholdingEnabled;
            OcrPreprocessingThreshold = settings.GetValue<int?>(DraftOcrPreprocessingThresholdSettingKey) ?? OcrPreprocessingSettings.Default.Threshold;
            OcrPreprocessingScale = settings.GetValue<double?>(DraftOcrPreprocessingScaleSettingKey) ?? OcrPreprocessingSettings.Default.Scale;
            OcrPreprocessingNoiseReductionEnabled = settings.GetValue<bool?>(DraftOcrPreprocessingNoiseReductionSettingKey) ?? OcrPreprocessingSettings.Default.NoiseReductionEnabled;
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

    private bool CanPickScreenZone()
    {
        return !IsBusy && !IsLiveTranslationRunning;
    }

    private bool CanRefreshCapturePreview()
    {
        return !IsBusy && !IsLiveTranslationRunning && SelectedZone is not null;
    }

    private bool CanRecognizeOcrPreview()
    {
        return !IsBusy
            && !IsLiveTranslationRunning
            && SelectedZone is not null
            && !string.IsNullOrWhiteSpace(SourceLanguage);
    }

    private bool CanRunTranslationPipeline()
    {
        return !IsBusy
            && !IsLiveTranslationRunning
            && OcrZones.Count > 0
            && !string.IsNullOrWhiteSpace(TranslatorProvider)
            && !string.IsNullOrWhiteSpace(SourceLanguage)
            && !string.IsNullOrWhiteSpace(TargetLanguage)
            && !string.IsNullOrWhiteSpace(OcrEngine);
    }

    private bool CanStartLiveTranslation()
    {
        return !IsBusy
            && !IsLiveTranslationRunning
            && OcrZones.Count > 0
            && !string.IsNullOrWhiteSpace(TranslatorProvider)
            && !string.IsNullOrWhiteSpace(SourceLanguage)
            && !string.IsNullOrWhiteSpace(TargetLanguage)
            && !string.IsNullOrWhiteSpace(OcrEngine);
    }

    private bool CanSelectTranslatorProvider()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(TranslatorProvider);
    }

    private bool CanSaveTranslatorCredentials()
    {
        return CanSelectTranslatorProvider()
            && TranslatorCredentialService.RequiresStoredCredentials(TranslatorProvider)
            && !string.IsNullOrWhiteSpace(TranslatorCredentialSecret)
            && !string.IsNullOrWhiteSpace(TranslatorCredentialProjectId)
            && !string.IsNullOrWhiteSpace(TranslatorCredentialEndpoint);
    }

    private void RefreshTranslatorCredentialDefaults()
    {
        if (string.IsNullOrWhiteSpace(TranslatorProvider))
        {
            HasStoredTranslatorCredentials = false;
            TranslatorCredentialStatus = "Select a translator provider to check credentials.";
            NotifyCommandStateChanged();
            return;
        }

        var defaultEndpoint = TranslatorCredentialService.GetDefaultEndpoint(TranslatorProvider);
        if (string.IsNullOrWhiteSpace(TranslatorCredentialEndpoint)
            || IsKnownDefaultTranslatorEndpoint(TranslatorCredentialEndpoint))
        {
            TranslatorCredentialEndpoint = defaultEndpoint;
        }

        if (string.IsNullOrWhiteSpace(TranslatorCredentialLocation))
        {
            TranslatorCredentialLocation = "global";
        }

        if (TranslatorCredentialService.RequiresStoredCredentials(TranslatorProvider))
        {
            HasStoredTranslatorCredentials = false;
            TranslatorCredentialStatus = "Translator credentials not checked.";
        }
        else
        {
            TranslatorCredentialSecret = string.Empty;
            HasStoredTranslatorCredentials = true;
            TranslatorCredentialStatus = $"{TranslatorCredentialService.NormalizeProvider(TranslatorProvider)} is experimental and does not use stored credentials.";
        }

        NotifyCommandStateChanged();
    }

    private static bool IsKnownDefaultTranslatorEndpoint(string endpoint)
    {
        return SupportedTranslatorProviders
            .Select(TranslatorCredentialService.GetDefaultEndpoint)
            .Any(defaultEndpoint => string.Equals(
                endpoint.Trim().TrimEnd('/'),
                defaultEndpoint.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase));
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
        settings.SetValue(DraftOcrEngineSettingKey, NormalizeOptionalText(OcrEngine));
        settings.SetValue(DraftOcrOrientationModeSettingKey, OcrOrientationMode);
        settings.SetValue(DraftOverlayMaskModeSettingKey, OverlayMaskMode);
        settings.SetValue(DraftOverlayMaskColorSettingKey, OverlayMaskColor.Trim());
        settings.SetValue(DraftOverlayOpacitySettingKey, OverlayOpacity);
        settings.SetValue(DraftOverlayPaddingSettingKey, OverlayPadding);
        settings.SetValue(DraftOcrPreprocessingEnabledSettingKey, OcrPreprocessingEnabled);
        settings.SetValue(DraftOcrPreprocessingContrastSettingKey, OcrPreprocessingContrast);
        settings.SetValue(DraftOcrPreprocessingBrightnessSettingKey, OcrPreprocessingBrightness);
        settings.SetValue(DraftOcrPreprocessingSharpnessSettingKey, OcrPreprocessingSharpness);
        settings.SetValue(DraftOcrPreprocessingThresholdingSettingKey, OcrPreprocessingThresholdingEnabled);
        settings.SetValue(DraftOcrPreprocessingThresholdSettingKey, OcrPreprocessingThreshold);
        settings.SetValue(DraftOcrPreprocessingScaleSettingKey, OcrPreprocessingScale);
        settings.SetValue(DraftOcrPreprocessingNoiseReductionSettingKey, OcrPreprocessingNoiseReductionEnabled);
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
            nameof(OcrEngine),
            !OcrSettings.IsSupportedEngine(OcrEngine)
                ? new[] { "OCR engine must be Windows or Tesseract." }
                : Array.Empty<string>());
        SetErrors(
            nameof(OcrOrientationMode),
            !OcrSettings.IsSupportedOrientationMode(OcrOrientationMode)
                ? new[] { "OCR orientation mode must be Auto, Horizontal, or Vertical." }
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

        SetErrors(
            nameof(OcrPreprocessingContrast),
            OcrPreprocessingContrast is < 0.5 or > 3
                ? new[] { "OCR preprocessing contrast must be between 0.5 and 3." }
                : Array.Empty<string>());
        SetErrors(
            nameof(OcrPreprocessingBrightness),
            OcrPreprocessingBrightness is < -100 or > 100
                ? new[] { "OCR preprocessing brightness must be between -100 and 100." }
                : Array.Empty<string>());
        SetErrors(
            nameof(OcrPreprocessingSharpness),
            OcrPreprocessingSharpness is < 0 or > 2
                ? new[] { "OCR preprocessing sharpness must be between 0 and 2." }
                : Array.Empty<string>());
        SetErrors(
            nameof(OcrPreprocessingThreshold),
            OcrPreprocessingThreshold is < 0 or > 255
                ? new[] { "OCR preprocessing threshold must be between 0 and 255." }
                : Array.Empty<string>());
        SetErrors(
            nameof(OcrPreprocessingScale),
            OcrPreprocessingScale is < 1 or > 3
                ? new[] { "OCR preprocessing scale must be between 1 and 3." }
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

    private static void ApplyScreenBoundsToZone(
        OcrZoneEditorViewModel zone,
        ScreenRegionSelectionResult selection)
    {
        var referenceWidth = Math.Max(1, selection.ReferenceWidth);
        var referenceHeight = Math.Max(1, selection.ReferenceHeight);
        var left = Math.Clamp(selection.X, 0, referenceWidth - 1);
        var top = Math.Clamp(selection.Y, 0, referenceHeight - 1);
        var right = Math.Clamp(left + Math.Max(1, selection.Width), left + 1, referenceWidth);
        var bottom = Math.Clamp(top + Math.Max(1, selection.Height), top + 1, referenceHeight);

        zone.AbsoluteX = left;
        zone.AbsoluteY = top;
        zone.AbsoluteWidth = Math.Max(1, right - left);
        zone.AbsoluteHeight = Math.Max(1, bottom - top);
        var relativeX = RoundRelativeCoordinate((double)left / referenceWidth);
        var relativeY = RoundRelativeCoordinate((double)top / referenceHeight);
        var relativeWidth = RoundRelativeCoordinate((double)(right - left) / referenceWidth);
        var relativeHeight = RoundRelativeCoordinate((double)(bottom - top) / referenceHeight);

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

    private void ReplaceBatchOcrPreviewTextBlocks(
        IReadOnlyList<BatchOcrPreviewEntry> entries,
        string previewZoneId)
    {
        OcrPreviewTextBlocks.Clear();
        OcrDebugTextBlocks.Clear();

        var includeZoneName = entries.Count > 1;
        foreach (var entry in entries)
        {
            var isVisibleOnCapturePreview = string.Equals(entry.ZoneId, previewZoneId, StringComparison.Ordinal);
            foreach (var textBlock in entry.SourceOcrResult.TextBlocks)
            {
                var displayText = includeZoneName
                    ? $"[{entry.ZoneName}] {textBlock.Text}"
                    : textBlock.Text;
                OcrPreviewTextBlocks.Add(new OcrTextBlock(displayText, textBlock.Bounds));
                OcrDebugTextBlocks.Add(new OcrDebugTextBlockViewModel(
                    displayText,
                    textBlock.Bounds,
                    isVisibleOnCapturePreview));
            }
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
        ((RelayCommand)PickScreenZoneCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DuplicateSelectedZoneCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveSelectedZoneUpCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveSelectedZoneDownCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RemoveSelectedZoneCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RefreshCapturePreviewCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)MeasureCaptureRefreshCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RecognizeOcrPreviewCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RunTranslationPipelineCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)StartLiveTranslationCommand).RaiseCanExecuteChanged();
        ((RelayCommand)StopLiveTranslationCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)CleanupTranslationCacheCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)CheckForUpdatesCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ApplyGlobalHotkeysCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ResetGlobalHotkeysCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ShowOverlayPreviewCommand).RaiseCanExecuteChanged();
        ((RelayCommand)HideOverlayPreviewCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)SaveTranslatorCredentialsCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)ValidateTranslatorCredentialsCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)DeleteTranslatorCredentialsCommand).RaiseCanExecuteChanged();
    }

    private sealed class UnavailableScreenRegionPickerService : IScreenRegionPickerService
    {
        public ScreenRegionSelectionResult? PickRegion()
        {
            return null;
        }
    }
}
