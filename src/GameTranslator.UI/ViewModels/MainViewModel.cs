using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Text;
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
using GameTranslator.Application.Translation;
using GameTranslator.Application.Updates;
using GameTranslator.Domain.Profiles;
using GameTranslator.UI.Commands;
using GameTranslator.UI.Services;

namespace GameTranslator.UI.ViewModels;

public sealed record LanguageOption(string Code, string Name)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} {Name}";

    public override string ToString()
    {
        return DisplayName;
    }
}

public sealed record OcrPreprocessingPresetOption(
    string Id,
    string DisplayName,
    string Description,
    OcrPreprocessingSettings? Settings)
{
    public override string ToString()
    {
        return DisplayName;
    }
}

public sealed record TranslationGroupingModeOption(TranslationGroupingMode Mode, string DisplayName)
{
    public override string ToString()
    {
        return DisplayName;
    }
}

public sealed record ContentLayoutModeOption(ContentLayoutMode Mode, string DisplayName)
{
    public override string ToString()
    {
        return DisplayName;
    }
}

public sealed record CandidateDetectorPresetOption(TextCandidateDetectorPreset Preset, string DisplayName)
{
    public override string ToString()
    {
        return DisplayName;
    }
}

public enum PipelineStatusSeverity
{
    Normal,
    Warning,
    Error,
}

public sealed class MainViewModel : ValidatableObservableObject
{
    private const int ZonesOcrWorkspaceTabIndex = 0;
    private const int TranslationWorkspaceTabIndex = 1;
    private const int OverlayWorkspaceTabIndex = 2;
    private const int LiveDiagnosticsWorkspaceTabIndex = 3;
    private const int OcrPacksWorkspaceTabIndex = 4;
    private const int HotkeysSettingsWorkspaceTabIndex = 5;
    private const string SelectedProfileSettingKey = "profiles.selectedId";
    private const string ProfileFileDialogFilter = "Game Translator profile (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*";
    private const string DebugInfoFileDialogFilter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
    private const string LiveDiagnosticsDirectorySettingKey = "diagnostics.live.directory";
    private const int MaximumLiveDiagnosticsReportUtf8Bytes = 99_000_000;
    private const int LiveDiagnosticsReportPrefixUtf8Bytes = 8_192;
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
    private const string LiveTranslationTimingPresetSettingKey = "shell.live.translationTimingPreset";
    private const string WelcomeTourCompletedSettingKey = "shell.welcomeTour.completed.v1";
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
        "GoogleWeb",
        "BingWeb",
        "YandexWeb",
    };

    private static readonly LanguageOption[] SupportedLanguageOptions = BuildSupportedLanguageOptions();
    private static readonly LanguageOption[] SupportedOcrLanguageOptions = BuildSupportedOcrLanguageOptions();
    private static readonly string WindowsOcrLanguagePackHelpMessage = string.Join(
        Environment.NewLine,
        new[]
        {
            "Run Windows PowerShell as Administrator and install OCR-only Windows capabilities:",
            string.Empty,
            "$langs = @('ja-JP', 'ko-KR', 'th-TH', 'zh-CN', 'zh-HK', 'zh-TW')",
            "foreach ($lang in $langs) {",
            "    Get-WindowsCapability -Online |",
            "        Where-Object { $_.Name -Like \"Language.OCR~~~$lang~*\" -and $_.State -ne \"Installed\" } |",
            "        Add-WindowsCapability -Online",
            "}",
            string.Empty,
            "This installs only Language.OCR capabilities.",
            "It does not add Language.Basic packs and should not add keyboard input languages.",
            "If Windows reports that a capability is not found, skip that language on this Windows build.",
            "After installing, sign out or restart before checking again.",
        });

    private static readonly OcrPreprocessingPresetOption[] SupportedOcrPreprocessingPresetOptions =
    {
        new("custom", "Custom", "Manual OCR preprocessing values.", null),
        new("off", "Off", "No OCR preprocessing.", OcrPreprocessingSettings.Default),
        new(
            "fast",
            "Light (1.25x)",
            "Light contrast and scaling for readable UI text.",
            new OcrPreprocessingSettings
            {
                IsEnabled = true,
                Contrast = 1.15,
                Brightness = 0,
                Sharpness = 0.25,
                ThresholdingEnabled = false,
                Threshold = 128,
                Scale = 1.25,
                NoiseReductionEnabled = false,
            }),
        new(
            "balanced",
            "Standard (1.5x)",
            "Moderate cleanup for subtitles, dialogue, and mixed UI.",
            new OcrPreprocessingSettings
            {
                IsEnabled = true,
                Contrast = 1.35,
                Brightness = 5,
                Sharpness = 0.6,
                ThresholdingEnabled = false,
                Threshold = 128,
                Scale = 1.5,
                NoiseReductionEnabled = true,
            }),
        new(
            "aggressive",
            "Strong (2x)",
            "Strong cleanup for small, low-contrast, or noisy text.",
            new OcrPreprocessingSettings
            {
                IsEnabled = true,
                Contrast = 1.7,
                Brightness = 10,
                Sharpness = 1.2,
                ThresholdingEnabled = true,
                Threshold = 150,
                Scale = 2,
                NoiseReductionEnabled = true,
            }),
        new(
            "small-source-heavy",
            "Tiny source - heavy (3x)",
            "Explicit 3x upscale and strong cleanup for tiny source text; slower and not automatic.",
            new OcrPreprocessingSettings
            {
                IsEnabled = true,
                Contrast = 1.9,
                Brightness = 8,
                Sharpness = 1.4,
                ThresholdingEnabled = true,
                Threshold = 152,
                Scale = 3,
                NoiseReductionEnabled = true,
            }),
    };

    private static readonly OcrOrientationMode[] SupportedOcrOrientations =
    {
        OcrOrientationMode.Auto,
        OcrOrientationMode.Horizontal,
        OcrOrientationMode.Vertical,
    };

    private static readonly LiveTranslationTimingPreset[] SupportedLiveTranslationTimingPresets =
    {
        LiveTranslationTimingPreset.Fast,
        LiveTranslationTimingPreset.Balanced,
        LiveTranslationTimingPreset.Conservative,
    };

    private static readonly WelcomeTourStepDefinition[] WelcomeTourSteps =
    {
        new(
            "Добро пожаловать в Game Translator",
            "Приложение захватывает выбранную область экрана, находит текст, распознаёт его, переводит и показывает перевод поверх оригинала. Сам тур не запускает захват и ничего не отправляет переводчику.",
            "Нажимайте «Далее»: нужная рабочая вкладка под этой карточкой будет открываться автоматически.",
            ZonesOcrWorkspaceTabIndex),
        new(
            "1. Создайте профиль игры",
            "Нажмите New, задайте понятное имя и при необходимости описание. Один профиль хранит зоны, языки, переводчик, OCR и внешний вид оверлея для конкретной игры или типа страниц.",
            "После настройки нажмите Save profile. Несохранённый черновик также хранится локально, но для постоянной работы лучше сохранить профиль.",
            ZonesOcrWorkspaceTabIndex),
        new(
            "2. Выберите переводчик и языки",
            "Во вкладке Translation выберите Provider, Source language и Target language. Официальным Google, Azure и Yandex нужны credentials; диагностические GoogleWeb, BingWeb и YandexWeb работают без сохранённых ключей.",
            "Провайдеры не переключаются автоматически. Проверьте правильность исходного языка: от него также зависит выбор OCR-настроек по умолчанию.",
            TranslationWorkspaceTabIndex),
        new(
            "3. Выделите OCR-зону",
            "Вернитесь в Zones & OCR и нажмите Pick screen. Для типовых облачков выделите область игры или страницы, где они появляются: оставьте небольшой запас по краям, но не захватывайте лишние панели и декоративный текст.",
            "Если текст появляется в нескольких постоянных местах, создайте отдельные зоны. Сохранённые зоны не должны пересекаться; внутри зоны detector сам найдёт текущие облачка.",
            ZonesOcrWorkspaceTabIndex),
        new(
            "4. Настройте OCR для облачков",
            "Для зоны выберите OCR language и Orientation. При первом выборе языка нажмите Check OCR language, чтобы проверить наличие локального языкового пакета. Horizontal подходит обычным горизонтальным репликам; Vertical — вертикальному японскому или китайскому тексту; Auto оставьте, когда направление должен определить профиль.",
            "Если проверка показала Missing, нажмите Install OCR language, дождитесь завершения установки и снова нажмите Check OCR language. Безопасная отправная точка: Detector preset = Standard, candidate grouping = Auto, preprocessing = Light 1,25× или Standard 1,5×. Ручные лимиты и строгие китайские пресеты нужны только для сравнительного теста.",
            ZonesOcrWorkspaceTabIndex),
        new(
            "5. Проверьте маску и читаемость",
            "Во вкладке Overlay выберите Solid или Darken, цвет и непрозрачность маски. Для каждой зоны можно выбрать шрифт, размер, жирность и курсив. Show test overlay показывает результат без запуска live-перевода.",
            "Маска должна полностью закрывать оригинал, а перевод — читаться на фоне игры. Для начала используйте непрозрачность 1 и умеренный Padding.",
            OverlayWorkspaceTabIndex),
        new(
            "6. Сохраните профиль и запустите live",
            "Нажмите Save profile, затем откройте Live & Diagnostics и выберите Start live. Готовые стабильные текстовые регионы будут переводиться независимо; Stop live завершает сеанс и сохраняет локальный отчёт.",
            "Если перевод не появился, сначала проверьте Pipeline status, OCR language packs и Open live reports. Отчёты автоматически не загружаются в интернет.",
            LiveDiagnosticsWorkspaceTabIndex),
    };

    private static readonly TranslationGroupingModeOption[] SupportedTranslationGroupingModeOptions =
    {
        new(TranslationGroupingMode.BlockByBlock, "Menu / block-by-block"),
        new(TranslationGroupingMode.WholeZone, "Book / dialog whole-zone"),
        new(TranslationGroupingMode.NearbyBlocks, "Comic / nearby groups"),
    };

    private static readonly ContentLayoutModeOption[] SupportedContentLayoutModeOptions =
    {
        new(ContentLayoutMode.DialogComic, "Dialog / Comic"),
    };

    private static readonly CandidateDetectorPresetOption[] SupportedCandidateDetectorPresetOptions =
    {
        new(TextCandidateDetectorPreset.Standard, "Standard — box 0.60"),
        new(TextCandidateDetectorPreset.ChineseExperimental, "Chinese test — box 0.65"),
        new(TextCandidateDetectorPreset.ChineseStrictExperimental, "Chinese strict test — box 0.70"),
    };

    private static LanguageOption[] BuildSupportedLanguageOptions()
    {
        var options = new List<LanguageOption>
        {
            new("af", "Afrikaans"), new("am", "Amharic"), new("ar", "Arabic"), new("az", "Azerbaijani"),
            new("be", "Belarusian"), new("bg", "Bulgarian"), new("bn", "Bengali"), new("bs", "Bosnian"),
            new("ca", "Catalan"), new("ceb", "Cebuano"), new("co", "Corsican"), new("cs", "Czech"),
            new("cy", "Welsh"), new("da", "Danish"), new("de", "German"), new("el", "Greek"),
            new("en", "English"), new("eo", "Esperanto"), new("es", "Spanish"), new("et", "Estonian"),
            new("eu", "Basque"), new("fa", "Persian"), new("fi", "Finnish"), new("fr", "French"),
            new("fy", "Frisian"), new("ga", "Irish"), new("gd", "Scottish Gaelic"), new("gl", "Galician"),
            new("gu", "Gujarati"), new("ha", "Hausa"), new("haw", "Hawaiian"), new("he", "Hebrew"),
            new("hi", "Hindi"), new("hmn", "Hmong"), new("hr", "Croatian"), new("ht", "Haitian Creole"),
            new("hu", "Hungarian"), new("hy", "Armenian"), new("id", "Indonesian"), new("ig", "Igbo"),
            new("is", "Icelandic"), new("it", "Italian"), new("ja", "Japanese"), new("jv", "Javanese"),
            new("ka", "Georgian"), new("kk", "Kazakh"), new("km", "Khmer"), new("kn", "Kannada"),
            new("ko", "Korean"), new("ku", "Kurdish"), new("ky", "Kyrgyz"), new("la", "Latin"),
            new("lb", "Luxembourgish"), new("lo", "Lao"), new("lt", "Lithuanian"), new("lv", "Latvian"),
            new("mg", "Malagasy"), new("mi", "Maori"), new("mk", "Macedonian"), new("ml", "Malayalam"),
            new("mn", "Mongolian"), new("mr", "Marathi"), new("ms", "Malay"), new("mt", "Maltese"),
            new("my", "Myanmar (Burmese)"), new("ne", "Nepali"), new("nl", "Dutch"), new("no", "Norwegian"),
            new("ny", "Chichewa"), new("or", "Odia"), new("pa", "Punjabi"), new("pl", "Polish"),
            new("ps", "Pashto"), new("pt", "Portuguese"), new("ro", "Romanian"), new("ru", "Russian"),
            new("sd", "Sindhi"), new("si", "Sinhala"), new("sk", "Slovak"), new("sl", "Slovenian"),
            new("sm", "Samoan"), new("sn", "Shona"), new("so", "Somali"), new("sq", "Albanian"),
            new("sr", "Serbian"), new("st", "Sesotho"), new("su", "Sundanese"), new("sv", "Swedish"),
            new("sw", "Swahili"), new("ta", "Tamil"), new("te", "Telugu"), new("tg", "Tajik"),
            new("th", "Thai"), new("tr", "Turkish"), new("uk", "Ukrainian"), new("ur", "Urdu"),
            new("uz", "Uzbek"), new("vi", "Vietnamese"), new("xh", "Xhosa"), new("yi", "Yiddish"),
            new("yo", "Yoruba"), new("zh-CN", "Chinese (Simplified)"), new("zh-TW", "Chinese (Traditional)"),
            new("zu", "Zulu"),
        };

        return options.ToArray();
    }

    private static LanguageOption[] BuildSupportedOcrLanguageOptions()
    {
        var options = new List<LanguageOption>
        {
            new(string.Empty, "Inherit translator source language"),
        };
        options.AddRange(SupportedLanguageOptions);

        foreach (var language in TesseractLanguageCatalog.Languages)
        {
            if (options.Any(option => string.Equals(option.Code, language.Code, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            options.Add(new LanguageOption(language.Code, $"{language.Name} (Tesseract OCR)"));
        }

        return options.ToArray();
    }

    private static OcrLanguagePackChecklistItemViewModel[] CreateDefaultOcrLanguagePackChecklistItems()
    {
        return new[]
        {
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.WindowsEngineId,
                "en-US",
                OcrOrientationMode.Horizontal,
                "Windows OCR English",
                "Base horizontal Windows OCR"),
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.WindowsEngineId,
                "ja-JP",
                OcrOrientationMode.Horizontal,
                "Windows OCR Japanese",
                "Horizontal Windows OCR"),
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.WindowsEngineId,
                "ko-KR",
                OcrOrientationMode.Horizontal,
                "Windows OCR Korean",
                "Horizontal Windows OCR"),
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.WindowsEngineId,
                "th-TH",
                OcrOrientationMode.Horizontal,
                "Windows OCR Thai",
                "May be unavailable on some Windows builds"),
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.WindowsEngineId,
                "zh-CN",
                OcrOrientationMode.Horizontal,
                "Windows OCR Chinese simplified",
                "Horizontal Windows OCR"),
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.WindowsEngineId,
                "zh-HK",
                OcrOrientationMode.Horizontal,
                "Windows OCR Chinese Hong Kong",
                "Traditional Windows OCR"),
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.WindowsEngineId,
                "zh-TW",
                OcrOrientationMode.Horizontal,
                "Windows OCR Chinese traditional",
                "Traditional Windows OCR"),
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.TesseractEngineId,
                "eng",
                OcrOrientationMode.Horizontal,
                "Tesseract English",
                "eng.traineddata"),
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.TesseractEngineId,
                "jpn",
                OcrOrientationMode.Horizontal,
                "Tesseract Japanese",
                "jpn.traineddata"),
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.TesseractEngineId,
                "jpn",
                OcrOrientationMode.Vertical,
                "Tesseract Japanese vertical",
                "jpn_vert.traineddata"),
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.TesseractEngineId,
                "tha",
                OcrOrientationMode.Horizontal,
                "Tesseract Thai",
                "tha.traineddata"),
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.TesseractEngineId,
                "kor",
                OcrOrientationMode.Horizontal,
                "Tesseract Korean",
                "kor.traineddata"),
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.TesseractEngineId,
                "chi_sim",
                OcrOrientationMode.Horizontal,
                "Tesseract Chinese simplified",
                "chi_sim.traineddata"),
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.TesseractEngineId,
                "chi_sim",
                OcrOrientationMode.Vertical,
                "Tesseract Chinese simplified vertical",
                "chi_sim_vert.traineddata"),
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.TesseractEngineId,
                "chi_tra",
                OcrOrientationMode.Horizontal,
                "Tesseract Chinese traditional",
                "chi_tra.traineddata"),
            new OcrLanguagePackChecklistItemViewModel(
                OcrSettings.TesseractEngineId,
                "chi_tra",
                OcrOrientationMode.Vertical,
                "Tesseract Chinese traditional vertical",
                "chi_tra_vert.traineddata"),
        };
    }

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
    private readonly IOcrLanguagePackService ocrLanguagePackService;
    private readonly ISettingsService settings;
    private readonly IApplicationLogger logger;
    private readonly string liveDiagnosticsDirectory;
    private readonly GlobalHotkeyService globalHotkeyService;
    private readonly DebugMetricFormatter debugMetricFormatter;
    private readonly IDebugResourceMonitor debugResourceMonitor;
    private readonly IReadOnlyList<string> installedFontFamilies = LoadInstalledFontFamilies();

    private string? pendingSelectedProfileId;
    private string? editingProfileId;
    private GameProfile? selectedProfile;
    private bool isProfileRenameActive;
    private string profileRenameText = string.Empty;
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
    private OcrPreprocessingPresetOption selectedOcrPreprocessingPreset = SupportedOcrPreprocessingPresetOptions[0];
    private bool isApplyingOcrPreprocessingPreset;
    private bool isSyncingOcrPreprocessingPresetSelection;
    private int selectedWorkspaceTabIndex;
    private OcrZoneEditorViewModel? selectedZone;
    private OcrResult? latestOcrPreviewResult;
    private IReadOnlyList<CandidateGroupingDebugEntry> latestCandidateGroupingEntries = Array.Empty<CandidateGroupingDebugEntry>();
    private readonly List<LiveCandidateLifecycleEvent> latestCandidateLifecycleEvents = new();
    private int droppedCandidateLifecycleEventCount;
    private LiveTranslationTiming? lastLiveTranslationTiming;
    private LiveTranslationTimingPreset? lastLiveTranslationTimingPreset;
    private string lastLiveOcrPreprocessingPreset = "(not started)";
    private string lastLiveTranslatorProvider = "(not started)";
    private string lastLiveSourceLanguage = "(not started)";
    private string lastLiveTargetLanguage = "(not started)";
    private CaptureRefreshMetrics? latestCaptureRefreshMetrics;
    private ImageSource? capturePreviewImage;
    private string capturePreviewStatus = "No capture preview yet.";
    private string captureRefreshMetricsSummary = "Refresh rate not measured.";
    private string ocrPreviewStatus = "No OCR preview yet.";
    private string ocrLanguagePackStatus = "OCR language pack status not checked.";
    private string overlayPreviewStatus = "Overlay preview hidden.";
    private string pipelineStatus = "Full translation pipeline not run yet.";
    private PipelineStatusSeverity pipelineStatusSeverity;
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
    private double debugVerticalSourceWidthMultiplier = 2;
    private LiveTranslationTimingPreset liveTranslationTimingPreset = LiveTranslationTimingPreset.Balanced;
    private bool isWelcomeTourVisible;
    private int welcomeTourStepIndex;
    private bool suppressDraftStatePersistence;
    private bool isZoneSelectionActive;
    private bool isZoneResizeActive;
    private bool isZoneMoveActive;
    private double zoneSelectionStartX;
    private double zoneSelectionStartY;
    private double zoneSelectionPreviewX;
    private double zoneSelectionPreviewY;
    private double zoneSelectionPreviewWidth;
    private double zoneSelectionPreviewHeight;
    private int zoneResizeOriginalAbsoluteX;
    private int zoneResizeOriginalAbsoluteY;
    private double zoneMoveStartSurfaceX;
    private double zoneMoveStartSurfaceY;
    private double zoneMoveOriginalSurfaceX;
    private double zoneMoveOriginalSurfaceY;
    private double zoneMoveOriginalSurfaceWidth;
    private double zoneMoveOriginalSurfaceHeight;
    private CancellationTokenSource? liveTranslationCancellation;
    private CandidatePipelineReadiness? lastCandidatePipelineReadiness;
    private string? lastLiveTranslationFailureKind;
    private int liveDiagnosticsSequence;

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
            UnavailableOcrLanguagePackService.Instance,
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
            screenRegionPickerService,
            UnavailableOcrLanguagePackService.Instance,
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
        IOcrLanguagePackService ocrLanguagePackService,
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
        this.ocrLanguagePackService = ocrLanguagePackService;
        this.settings = settings;
        this.logger = logger;
        liveDiagnosticsDirectory = ResolveLiveDiagnosticsDirectory(settings);
        debugVerticalSourceWidthMultiplier = overlayPositioningService.SessionVerticalSourceWidthMultiplier;
        globalHotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
        pendingSelectedProfileId = settings.GetValue<string>(SelectedProfileSettingKey);
        isDebugOverlayEnabled = settings.GetValue<bool?>(DebugOverlayEnabledSettingKey) ?? false;
        liveTranslationTimingPreset = NormalizeLiveTranslationTimingPreset(
            settings.GetValue<LiveTranslationTimingPreset?>(LiveTranslationTimingPresetSettingKey)
            ?? LiveTranslationTimingPreset.Balanced);

        Profiles = new ObservableCollection<GameProfile>();
        OcrZones = new ObservableCollection<OcrZoneEditorViewModel>();
        OcrPreviewTextBlocks = new ObservableCollection<OcrTextBlock>();
        OcrDebugTextBlocks = new ObservableCollection<OcrDebugTextBlockViewModel>();
        OcrLanguagePackChecklistItems = new ObservableCollection<OcrLanguagePackChecklistItemViewModel>(
            CreateDefaultOcrLanguagePackChecklistItems());
        HotkeyBindings = new ObservableCollection<HotkeyBindingViewModel>();
        ValidationErrors = new ObservableCollection<string>();
        OverlayMaskModes = Enum.GetValues<OverlayMaskMode>();
        TranslatorProviderOptions = SupportedTranslatorProviders;
        LanguageOptions = SupportedLanguageOptions;
        OcrLanguageOptions = SupportedOcrLanguageOptions;
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
        CollectDebugInfoCommand = new AsyncRelayCommand(CollectDebugInfoAsync, CanCollectDebugInfo);
        OpenLiveDiagnosticsFolderCommand = new RelayCommand(OpenLiveDiagnosticsFolder, () => !IsBusy);
        CheckOcrLanguagePackCommand = new AsyncRelayCommand(CheckOcrLanguagePackAsync, CanManageOcrLanguagePack);
        InstallOcrLanguagePackCommand = new AsyncRelayCommand(InstallOcrLanguagePackAsync, CanManageOcrLanguagePack);
        CheckOcrLanguagePackChecklistCommand = new AsyncRelayCommand(
            CheckOcrLanguagePackChecklistAsync,
            CanManageOcrLanguagePackChecklist);
        InstallTesseractLanguagePackChecklistCommand = new AsyncRelayCommand(
            InstallTesseractLanguagePackChecklistAsync,
            CanManageOcrLanguagePackChecklist);
        ShowWindowsOcrLanguagePackHelpCommand = new AsyncRelayCommand(
            ShowWindowsOcrLanguagePackHelpAsync,
            CanManageOcrLanguagePackChecklist);
        RunTranslationPipelineCommand = new AsyncRelayCommand(RunTranslationPipelineAsync, CanRunTranslationPipeline);
        StartLiveTranslationCommand = new AsyncRelayCommand(StartLiveTranslationAsync, CanStartLiveTranslation);
        StopLiveTranslationCommand = new RelayCommand(StopLiveTranslation, CanStopLiveTranslation);
        CleanupTranslationCacheCommand = new AsyncRelayCommand(CleanupTranslationCacheAsync, () => !IsBusy);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsBusy);
        ApplyGlobalHotkeysCommand = new RelayCommand(ApplyGlobalHotkeys, CanApplyGlobalHotkeys);
        ResetGlobalHotkeysCommand = new RelayCommand(ResetGlobalHotkeys, () => !IsBusy);
        ShowOverlayPreviewCommand = new RelayCommand(ShowOverlayPreview, () => !IsBusy);
        HideOverlayPreviewCommand = new RelayCommand(HideOverlayPreview, () => !IsBusy && IsOverlayPreviewVisible);
        SaveTranslatorCredentialsCommand = new AsyncRelayCommand(SaveTranslatorCredentialsAsync, CanSaveTranslatorCredentials);
        ValidateTranslatorCredentialsCommand = new AsyncRelayCommand(ValidateTranslatorCredentialsAsync, CanSelectTranslatorProvider);
        DeleteTranslatorCredentialsCommand = new AsyncRelayCommand(DeleteTranslatorCredentialsAsync, CanSelectTranslatorProvider);
        ShowWelcomeTourCommand = new RelayCommand(ShowWelcomeTour);
        CloseWelcomeTourCommand = new RelayCommand(CloseWelcomeTour);
        PreviousWelcomeTourStepCommand = new RelayCommand(PreviousWelcomeTourStep);
        NextWelcomeTourStepCommand = new RelayCommand(NextWelcomeTourStep);

        BeginCreateProfile();
        StatusMessage = "Ready to manage game profiles.";
    }

    public string ApplicationName => "Game Translator";

    public double ZoneSurfaceWidth => OcrZoneEditorViewModel.PreviewSurfaceWidth;

    public double ZoneSurfaceHeight => OcrZoneEditorViewModel.PreviewSurfaceHeight;

    public string ZoneSurfaceSummary => $"Reference surface {OcrZoneEditorViewModel.ReferenceSurfaceWidth}x{OcrZoneEditorViewModel.ReferenceSurfaceHeight}";

    public ObservableCollection<GameProfile> Profiles { get; }

    public ObservableCollection<OcrZoneEditorViewModel> OcrZones { get; }

    public ObservableCollection<OcrTextBlock> OcrPreviewTextBlocks { get; }

    public ObservableCollection<OcrDebugTextBlockViewModel> OcrDebugTextBlocks { get; }

    public ObservableCollection<OcrLanguagePackChecklistItemViewModel> OcrLanguagePackChecklistItems { get; }

    public ObservableCollection<HotkeyBindingViewModel> HotkeyBindings { get; }

    public ObservableCollection<string> ValidationErrors { get; }

    public IReadOnlyList<OverlayMaskMode> OverlayMaskModes { get; }

    public IReadOnlyList<string> TranslatorProviderOptions { get; }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public IReadOnlyList<LanguageOption> OcrLanguageOptions { get; }

    public GameProfile? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (!SetProperty(ref selectedProfile, value))
            {
                return;
            }

            ClearProfileRenameState();
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
                OnPropertyChanged(nameof(RequiresStoredTranslatorCredentials));
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
            value = NormalizeTranslatorLanguageTag(value);
            if (SetProperty(ref sourceLanguage, value))
            {
                EnsureOcrOrientationMatchesConfiguredLanguages();
                OnPropertyChanged(nameof(TranslatorSettingsSummary));
                OnPropertyChanged(nameof(ProfileSummary));
                OnPropertyChanged(nameof(IsOcrOrientationSelectionEnabled));
                OnPropertyChanged(nameof(OcrOrientationSelectionToolTip));
                ResetOcrLanguagePackStatus();
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
                NotifyCommandStateChanged();
            }
        }
    }

    public string TargetLanguage
    {
        get => targetLanguage;
        set
        {
            value = NormalizeTranslatorLanguageTag(value);
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

    public bool HasOcrEngineValidationError => GetErrors(nameof(OcrEngine)).Cast<string>().Any();

    public string? OcrEngineValidationMessage => GetErrors(nameof(OcrEngine)).Cast<string>().FirstOrDefault();

    public string OcrEngineBorderBrush => HasOcrEngineValidationError ? "#C84B4B" : "#9AA5B1";

    public IReadOnlyList<OcrOrientationMode> OcrOrientations => SupportedOcrOrientations;

    public bool IsOcrOrientationSelectionEnabled => SupportsVerticalOcrLayout();

    public bool IsVerticalOcrOrientation => OcrOrientationMode == OcrOrientationMode.Vertical;

    public bool IsHorizontalOcrOrientation => !IsVerticalOcrOrientation;

    public string OcrOrientationSelectionToolTip => IsOcrOrientationSelectionEnabled
        ? "Vertical OCR is available for Japanese and Chinese zones."
        : "The configured OCR language has no supported vertical layout; horizontal OCR is selected automatically.";

    public IReadOnlyList<OcrPreprocessingPresetOption> OcrPreprocessingPresets => SupportedOcrPreprocessingPresetOptions;

    public IReadOnlyList<string> InstalledFontFamilies => installedFontFamilies;

    public IReadOnlyList<LiveTranslationTimingPreset> LiveTranslationTimingPresets => SupportedLiveTranslationTimingPresets;

    public IReadOnlyList<TranslationGroupingModeOption> TranslationGroupingModeOptions => SupportedTranslationGroupingModeOptions;

    public IReadOnlyList<ContentLayoutModeOption> ContentLayoutModeOptions => SupportedContentLayoutModeOptions;

    public IReadOnlyList<CandidateDetectorPresetOption> CandidateDetectorPresetOptions => SupportedCandidateDetectorPresetOptions;

    public int SelectedWorkspaceTabIndex
    {
        get => selectedWorkspaceTabIndex;
        set
        {
            var normalizedValue = Math.Clamp(value, ZonesOcrWorkspaceTabIndex, HotkeysSettingsWorkspaceTabIndex);
            if (!SetProperty(ref selectedWorkspaceTabIndex, normalizedValue))
            {
                return;
            }

            OnPropertyChanged(nameof(IsZonesOcrTabSelected));
            OnPropertyChanged(nameof(IsTranslationTabSelected));
            OnPropertyChanged(nameof(IsOverlayTabSelected));
            OnPropertyChanged(nameof(IsLiveDiagnosticsTabSelected));
            OnPropertyChanged(nameof(IsOcrPacksTabSelected));
            OnPropertyChanged(nameof(IsHotkeysSettingsTabSelected));
            OnPropertyChanged(nameof(IsZonesOcrOrLiveDiagnosticsTabSelected));
        }
    }

    public bool IsProfileRenameActive
    {
        get => isProfileRenameActive;
        private set => SetProperty(ref isProfileRenameActive, value);
    }

    public string ProfileRenameText
    {
        get => profileRenameText;
        set => SetProperty(ref profileRenameText, value);
    }

    public bool IsZonesOcrTabSelected => SelectedWorkspaceTabIndex == ZonesOcrWorkspaceTabIndex;

    public bool IsTranslationTabSelected => SelectedWorkspaceTabIndex == TranslationWorkspaceTabIndex;

    public bool IsOverlayTabSelected => SelectedWorkspaceTabIndex == OverlayWorkspaceTabIndex;

    public bool IsLiveDiagnosticsTabSelected => SelectedWorkspaceTabIndex == LiveDiagnosticsWorkspaceTabIndex;

    public bool IsOcrPacksTabSelected => SelectedWorkspaceTabIndex == OcrPacksWorkspaceTabIndex;

    public bool IsHotkeysSettingsTabSelected => SelectedWorkspaceTabIndex == HotkeysSettingsWorkspaceTabIndex;

    public bool IsZonesOcrOrLiveDiagnosticsTabSelected =>
        IsZonesOcrTabSelected || IsLiveDiagnosticsTabSelected;

    public bool IsWelcomeTourVisible
    {
        get => isWelcomeTourVisible;
        private set => SetProperty(ref isWelcomeTourVisible, value);
    }

    public int WelcomeTourStepIndex => welcomeTourStepIndex;

    public int WelcomeTourStepNumber => welcomeTourStepIndex + 1;

    public int WelcomeTourStepCount => WelcomeTourSteps.Length;

    public double WelcomeTourProgress => WelcomeTourStepNumber;

    public string WelcomeTourStepIndicator => $"Шаг {WelcomeTourStepNumber} из {WelcomeTourStepCount}";

    public string WelcomeTourTitle => CurrentWelcomeTourStep.Title;

    public string WelcomeTourBody => CurrentWelcomeTourStep.Body;

    public string WelcomeTourGuidance => CurrentWelcomeTourStep.Guidance;

    public string WelcomeTourTargetElementName => welcomeTourStepIndex switch
    {
        0 => "WorkspaceHeaderActions",
        1 => "ProfileRail",
        2 => "TranslationSettingsCard",
        3 => "WelcomeTourPickScreenButton",
        4 => "OcrPreprocessingCard",
        5 => "OverlaySettingsCard",
        _ => "WelcomeTourLiveControls",
    };

    public bool WelcomeTourCanGoBack => welcomeTourStepIndex > 0;

    public string WelcomeTourPrimaryActionText =>
        welcomeTourStepIndex == WelcomeTourSteps.Length - 1 ? "Готово" : "Далее";

    public LiveTranslationTimingPreset LiveTranslationTimingPreset
    {
        get => liveTranslationTimingPreset;
        set
        {
            var normalizedValue = NormalizeLiveTranslationTimingPreset(value);
            if (SetProperty(ref liveTranslationTimingPreset, normalizedValue))
            {
                settings.SetValue(LiveTranslationTimingPresetSettingKey, normalizedValue);
                OnPropertyChanged(nameof(LiveTranslationTimingSummary));
            }
        }
    }

    public string LiveTranslationTimingSummary => LiveTranslationTimingPreset switch
    {
        LiveTranslationTimingPreset.Fast => "Fast: poll every 100 ms; require 2 matching grouping and OCR observations, each held for at least 200 ms.",
        LiveTranslationTimingPreset.Conservative => "Conservative: poll every 160 ms; require 4 matching grouping and OCR observations, each held for at least 320 ms.",
        _ => "Balanced: poll every 125 ms; require 3 matching grouping and OCR observations, each held for at least 250 ms.",
    };

    public string OcrEngine
    {
        get => ocrEngine;
        set
        {
            var normalizedEngine = NormalizeOcrEngine(value);
            if (SetProperty(ref ocrEngine, normalizedEngine))
            {
                OnPropertyChanged(nameof(ProfileSummary));
                ResetOcrLanguagePackStatus();
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
            var normalizedValue = NormalizeOcrOrientationMode(value);
            if (SetProperty(ref ocrOrientationMode, normalizedValue))
            {
                OnPropertyChanged(nameof(IsVerticalOcrOrientation));
                OnPropertyChanged(nameof(IsHorizontalOcrOrientation));
                OnPropertyChanged(nameof(ProfileSummary));
                ResetOcrLanguagePackStatus();
                PersistDraftShellStateIfNeeded();
                RefreshValidationState();
                NotifyCommandStateChanged();
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


    public OcrPreprocessingPresetOption SelectedOcrPreprocessingPreset
    {
        get => selectedOcrPreprocessingPreset;
        set
        {
            var normalizedValue = value ?? SupportedOcrPreprocessingPresetOptions[0];
            if (!SetProperty(ref selectedOcrPreprocessingPreset, normalizedValue))
            {
                return;
            }

            OnPropertyChanged(nameof(OcrPreprocessingPresetSummary));
            if (isSyncingOcrPreprocessingPresetSelection || normalizedValue.Settings is null)
            {
                return;
            }

            ApplyOcrPreprocessingSettings(normalizedValue.Settings);
        }
    }

    public string OcrPreprocessingPresetSummary => SelectedOcrPreprocessingPreset.Description;

    public bool OcrPreprocessingEnabled
    {
        get => ocrPreprocessingEnabled;
        set
        {
            if (SetProperty(ref ocrPreprocessingEnabled, value))
            {
                PersistDraftShellStateIfNeeded();
                OnPropertyChanged(nameof(ProfileSummary));
                SyncOcrPreprocessingPresetSelection();
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
                SyncOcrPreprocessingPresetSelection();
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
                SyncOcrPreprocessingPresetSelection();
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
                SyncOcrPreprocessingPresetSelection();
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
                SyncOcrPreprocessingPresetSelection();
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
                SyncOcrPreprocessingPresetSelection();
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
                SyncOcrPreprocessingPresetSelection();
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
                SyncOcrPreprocessingPresetSelection();
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
            ResetOcrLanguagePackStatus();
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

    public string OcrLanguagePackStatus
    {
        get => ocrLanguagePackStatus;
        private set => SetProperty(ref ocrLanguagePackStatus, value);
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

    public PipelineStatusSeverity PipelineStatusSeverity
    {
        get => pipelineStatusSeverity;
        private set => SetProperty(ref pipelineStatusSeverity, value);
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

    public bool RequiresStoredTranslatorCredentials =>
        !string.IsNullOrWhiteSpace(TranslatorProvider)
        && !TranslatorCredentialService.IsRemovedProvider(TranslatorProvider)
        && TranslatorCredentialService.RequiresStoredCredentials(TranslatorProvider);

    public double DebugVerticalSourceWidthMultiplier
    {
        get => debugVerticalSourceWidthMultiplier;
        set
        {
            var normalizedMultiplier = overlayPositioningService.SetSessionVerticalSourceWidthMultiplier(value);
            if (SetProperty(ref debugVerticalSourceWidthMultiplier, normalizedMultiplier) && IsDebugOverlayEnabled)
            {
                DebugOverlayStatus = $"Vertical source width: {normalizedMultiplier:0.0}x for this session.";
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

    public ICommand CollectDebugInfoCommand { get; }

    public ICommand OpenLiveDiagnosticsFolderCommand { get; }

    public ICommand CheckOcrLanguagePackCommand { get; }

    public ICommand InstallOcrLanguagePackCommand { get; }

    public ICommand CheckOcrLanguagePackChecklistCommand { get; }

    public ICommand InstallTesseractLanguagePackChecklistCommand { get; }

    public ICommand ShowWindowsOcrLanguagePackHelpCommand { get; }

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

    public ICommand ShowWelcomeTourCommand { get; }

    public ICommand CloseWelcomeTourCommand { get; }

    public ICommand PreviousWelcomeTourStepCommand { get; }

    public ICommand NextWelcomeTourStepCommand { get; }

    public async Task SaveTranslatorCredentialsAsync()
    {
        if (!CanSelectTranslatorProvider())
        {
            TranslatorCredentialStatus = "Select a translator provider to save credentials.";
            return;
        }

        if (TranslatorCredentialService.IsRemovedProvider(TranslatorProvider))
        {
            TranslatorCredentialStatus = CreateRemovedTranslatorProviderStatus(TranslatorProvider);
            StatusMessage = TranslatorCredentialStatus;
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

        if (TranslatorCredentialService.IsRemovedProvider(TranslatorProvider))
        {
            HasStoredTranslatorCredentials = false;
            TranslatorCredentialStatus = CreateRemovedTranslatorProviderStatus(TranslatorProvider);
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

        if (TranslatorCredentialService.IsRemovedProvider(TranslatorProvider))
        {
            TranslatorCredentialSecret = string.Empty;
            HasStoredTranslatorCredentials = false;
            TranslatorCredentialStatus = CreateRemovedTranslatorProviderStatus(TranslatorProvider);
            StatusMessage = TranslatorCredentialStatus;
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
        if (!(settings.GetValue<bool?>(WelcomeTourCompletedSettingKey) ?? false))
        {
            ShowWelcomeTour();
        }

        _ = CheckForUpdatesOnStartupAsync();
    }

    private WelcomeTourStepDefinition CurrentWelcomeTourStep => WelcomeTourSteps[welcomeTourStepIndex];

    private void ShowWelcomeTour()
    {
        SetWelcomeTourStep(0);
        IsWelcomeTourVisible = true;
    }

    private void CloseWelcomeTour()
    {
        IsWelcomeTourVisible = false;
        settings.SetValue(WelcomeTourCompletedSettingKey, true);
    }

    private void PreviousWelcomeTourStep()
    {
        if (!IsWelcomeTourVisible || welcomeTourStepIndex == 0)
        {
            return;
        }

        SetWelcomeTourStep(welcomeTourStepIndex - 1);
    }

    private void NextWelcomeTourStep()
    {
        if (!IsWelcomeTourVisible)
        {
            return;
        }

        if (welcomeTourStepIndex == WelcomeTourSteps.Length - 1)
        {
            CloseWelcomeTour();
            return;
        }

        SetWelcomeTourStep(welcomeTourStepIndex + 1);
    }

    private void SetWelcomeTourStep(int stepIndex)
    {
        var normalizedStepIndex = Math.Clamp(stepIndex, 0, WelcomeTourSteps.Length - 1);
        if (welcomeTourStepIndex != normalizedStepIndex)
        {
            welcomeTourStepIndex = normalizedStepIndex;
            OnPropertyChanged(nameof(WelcomeTourStepIndex));
            OnPropertyChanged(nameof(WelcomeTourStepNumber));
            OnPropertyChanged(nameof(WelcomeTourProgress));
            OnPropertyChanged(nameof(WelcomeTourStepIndicator));
            OnPropertyChanged(nameof(WelcomeTourTitle));
            OnPropertyChanged(nameof(WelcomeTourBody));
            OnPropertyChanged(nameof(WelcomeTourGuidance));
            OnPropertyChanged(nameof(WelcomeTourTargetElementName));
            OnPropertyChanged(nameof(WelcomeTourCanGoBack));
            OnPropertyChanged(nameof(WelcomeTourPrimaryActionText));
        }

        SelectedWorkspaceTabIndex = CurrentWelcomeTourStep.WorkspaceTabIndex;
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
        ClearZoneMoveState();

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
        ClearZoneMoveState();
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

    public void StartSelectedZoneMove(double surfaceX, double surfaceY)
    {
        if (IsBusy || SelectedZone is null)
        {
            return;
        }

        ClearZoneSelectionPreview();
        ClearZoneResizeState();
        zoneMoveStartSurfaceX = Math.Clamp(surfaceX, 0, ZoneSurfaceWidth);
        zoneMoveStartSurfaceY = Math.Clamp(surfaceY, 0, ZoneSurfaceHeight);
        zoneMoveOriginalSurfaceX = SelectedZone.SurfaceX;
        zoneMoveOriginalSurfaceY = SelectedZone.SurfaceY;
        zoneMoveOriginalSurfaceWidth = SelectedZone.SurfaceWidth;
        zoneMoveOriginalSurfaceHeight = SelectedZone.SurfaceHeight;
        isZoneMoveActive = true;
        StatusMessage = $"Move '{SelectedZone.DisplayName}' by dragging it on the surface.";
    }

    public void UpdateSelectedZoneMove(double surfaceX, double surfaceY)
    {
        if (!isZoneMoveActive || SelectedZone is null)
        {
            return;
        }

        var deltaX = surfaceX - zoneMoveStartSurfaceX;
        var deltaY = surfaceY - zoneMoveStartSurfaceY;
        var maxLeft = Math.Max(0, ZoneSurfaceWidth - zoneMoveOriginalSurfaceWidth);
        var maxTop = Math.Max(0, ZoneSurfaceHeight - zoneMoveOriginalSurfaceHeight);
        var left = Math.Clamp(zoneMoveOriginalSurfaceX + deltaX, 0, maxLeft);
        var top = Math.Clamp(zoneMoveOriginalSurfaceY + deltaY, 0, maxTop);

        ApplySurfaceBoundsToZone(
            SelectedZone,
            left,
            top,
            zoneMoveOriginalSurfaceWidth,
            zoneMoveOriginalSurfaceHeight);
        PersistDraftShellStateIfNeeded();
        OnPropertyChanged(nameof(ProfileSummary));
        RefreshValidationState();
    }

    public void CompleteSelectedZoneMove(double surfaceX, double surfaceY)
    {
        if (!isZoneMoveActive)
        {
            return;
        }

        UpdateSelectedZoneMove(surfaceX, surfaceY);
        ClearZoneMoveState();

        if (SelectedZone is not null)
        {
            StatusMessage = $"Moved zone '{SelectedZone.DisplayName}'.";
        }
    }

    public void BeginProfileRename(string profileId)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        var profile = Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, profileId, StringComparison.Ordinal));
        if (profile is null)
        {
            return;
        }

        if (!ReferenceEquals(SelectedProfile, profile))
        {
            SelectedProfile = profile;
        }

        ProfileRenameText = profile.Name;
        IsProfileRenameActive = true;
        StatusMessage = "Enter a new profile name. Press Enter to save or Escape to cancel.";
    }

    public async Task CommitProfileRenameAsync()
    {
        if (!IsProfileRenameActive || IsBusy || SelectedProfile is null)
        {
            return;
        }

        var newName = ProfileRenameText.Trim();
        if (newName.Length == 0)
        {
            StatusMessage = "Profile name cannot be empty.";
            return;
        }

        if (Profiles.Any(profile =>
                !string.Equals(profile.Id, SelectedProfile.Id, StringComparison.Ordinal)
                && string.Equals(profile.Name.Trim(), newName, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"A profile named '{newName}' already exists.";
            return;
        }

        if (string.Equals(SelectedProfile.Name, newName, StringComparison.Ordinal))
        {
            ClearProfileRenameState();
            StatusMessage = "Profile name was not changed.";
            return;
        }

        var profileId = SelectedProfile.Id;
        var renamedProfile = SelectedProfile with { Name = newName };
        ClearProfileRenameState();
        var succeeded = await RunProfileOperationAsync(
            $"Renaming profile to '{newName}'...",
            async () =>
            {
                await profileService.UpdateAsync(renamedProfile);
                await RefreshProfilesAsync(profileId);
            });

        if (succeeded)
        {
            StatusMessage = $"Profile renamed to '{newName}'.";
        }
    }

    public void CancelProfileRename()
    {
        if (!IsProfileRenameActive)
        {
            return;
        }

        ClearProfileRenameState();
        StatusMessage = "Profile rename canceled.";
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
        EnsureOcrOrientationMatchesConfiguredLanguages();
        OnPropertyChanged(nameof(IsOcrOrientationSelectionEnabled));
        OnPropertyChanged(nameof(OcrOrientationSelectionToolTip));
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

    public async Task CheckOcrLanguagePackAsync()
    {
        if (!CanManageOcrLanguagePack())
        {
            OcrLanguagePackStatus = "Select an OCR engine, OCR zone, and OCR language first.";
            StatusMessage = OcrLanguagePackStatus;
            return;
        }

        var ocrLanguage = ResolveSelectedOcrLanguage();
        var zoneName = SelectedZone?.DisplayName ?? "selected zone";
        try
        {
            IsBusy = true;
            OcrLanguagePackStatus = $"Checking {OcrEngine} OCR language '{ocrLanguage}' for '{zoneName}'...";
            StatusMessage = OcrLanguagePackStatus;
            var status = await ocrLanguagePackService.CheckAsync(
                OcrEngine.Trim(),
                ocrLanguage,
                OcrOrientationMode);

            OcrLanguagePackStatus = status.Message;
            StatusMessage = OcrLanguagePackStatus;
            RefreshValidationState();
        }
        catch (Exception exception)
        {
            logger.Error(exception, "OCR language pack check failed.");
            OcrLanguagePackStatus = "OCR language pack check failed. Check logs for details.";
            StatusMessage = OcrLanguagePackStatus;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task InstallOcrLanguagePackAsync()
    {
        if (!CanManageOcrLanguagePack())
        {
            OcrLanguagePackStatus = "Select an OCR engine, OCR zone, and OCR language first.";
            StatusMessage = OcrLanguagePackStatus;
            return;
        }

        var ocrLanguage = ResolveSelectedOcrLanguage();
        var zoneName = SelectedZone?.DisplayName ?? "selected zone";
        try
        {
            IsBusy = true;
            OcrLanguagePackStatus = $"Installing {OcrEngine} OCR language '{ocrLanguage}' for '{zoneName}'...";
            StatusMessage = OcrLanguagePackStatus;
            var result = await ocrLanguagePackService.InstallAsync(
                OcrEngine.Trim(),
                ocrLanguage,
                OcrOrientationMode);

            if (result.ActionUri is not null)
            {
                OpenExternalUri(result.ActionUri);
            }

            OcrLanguagePackStatus = result.Message;
            StatusMessage = OcrLanguagePackStatus;
            RefreshValidationState();
        }
        catch (Exception exception)
        {
            logger.Error(exception, "OCR language pack installation failed.");
            OcrLanguagePackStatus = "OCR language pack installation failed. Check logs for details.";
            StatusMessage = OcrLanguagePackStatus;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CheckOcrLanguagePackChecklistAsync()
    {
        if (!CanManageOcrLanguagePackChecklist())
        {
            OcrLanguagePackStatus = "Stop live translation before checking OCR language packs.";
            StatusMessage = OcrLanguagePackStatus;
            return;
        }

        try
        {
            IsBusy = true;
            OcrLanguagePackStatus = "Checking common OCR language packs...";
            StatusMessage = OcrLanguagePackStatus;

            foreach (var item in OcrLanguagePackChecklistItems)
            {
                item.MarkChecking();
                try
                {
                    var status = await ocrLanguagePackService.CheckAsync(
                        item.EngineId,
                        item.LanguageTag,
                        item.OrientationMode);
                    item.ApplyStatus(status);
                }
                catch (Exception exception)
                {
                    logger.Error(exception, $"OCR language pack checklist check failed for {item.EngineId} {item.LanguageTag}.");
                    item.MarkFailed("Check failed. Check logs for details.");
                }
            }

            UpdateOcrLanguagePackChecklistSummary("OCR language pack check complete.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task InstallTesseractLanguagePackChecklistAsync()
    {
        if (!CanManageOcrLanguagePackChecklist())
        {
            OcrLanguagePackStatus = "Stop live translation before installing Tesseract OCR language packs.";
            StatusMessage = OcrLanguagePackStatus;
            return;
        }

        try
        {
            IsBusy = true;
            OcrLanguagePackStatus = "Installing missing common Tesseract traineddata files...";
            StatusMessage = OcrLanguagePackStatus;

            foreach (var item in OcrLanguagePackChecklistItems.Where(item => item.IsTesseract))
            {
                if (item.IsReady)
                {
                    continue;
                }

                item.MarkInstalling();
                try
                {
                    var result = await ocrLanguagePackService.InstallAsync(
                        item.EngineId,
                        item.LanguageTag,
                        item.OrientationMode);
                    item.ApplyInstallResult(result);
                }
                catch (Exception exception)
                {
                    logger.Error(exception, $"Tesseract OCR language pack install failed for {item.LanguageTag}.");
                    item.MarkFailed("Install failed. Check logs for details.");
                }
            }

            UpdateOcrLanguagePackChecklistSummary("Tesseract OCR language pack install complete.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ShowWindowsOcrLanguagePackHelpAsync()
    {
        if (!CanManageOcrLanguagePackChecklist())
        {
            OcrLanguagePackStatus = "Stop live translation before opening Windows OCR language pack help.";
            StatusMessage = OcrLanguagePackStatus;
            return;
        }

        OcrLanguagePackStatus = "Windows OCR packs require an elevated PowerShell command outside the app.";
        StatusMessage = OcrLanguagePackStatus;
        await dialogService.ShowInformationAsync("Windows OCR language packs", WindowsOcrLanguagePackHelpMessage);
    }

    public async Task RecognizeOcrPreviewAsync()
    {
        if (SelectedZone is null)
        {
            OcrPreviewStatus = "Select an OCR zone to recognize text.";
            StatusMessage = OcrPreviewStatus;
            return;
        }

        var zone = SelectedZone;
        var ocrLanguage = ResolveOcrLanguage(zone);
        if (string.IsNullOrWhiteSpace(ocrLanguage))
        {
            OcrPreviewStatus = "OCR language is required for preview.";
            StatusMessage = OcrPreviewStatus;
            return;
        }

        var ocrCompatibilityError = CreateOcrEngineLanguageCompatibilityError(zone);
        if (!string.IsNullOrWhiteSpace(ocrCompatibilityError))
        {
            OcrPreviewStatus = ocrCompatibilityError;
            StatusMessage = OcrPreviewStatus;
            return;
        }

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
                new OcrRequest(
                    frame,
                    ocrLanguage,
                    zone.Id,
                    BuildOcrPreprocessingSettings(),
                    OcrEngine,
                    OcrOrientationMode)
                {
                    DetectorPreset = zone.DetectorPreset,
                });
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

    public async Task CollectDebugInfoAsync()
    {
        if (IsBusy)
        {
            StatusMessage = "Debug info hotkey received while an operation is already running.";
            return;
        }

        if (!HasOcrPreview && !IsLiveTranslationRunning && CanRecognizeOcrPreview())
        {
            await RecognizeOcrPreviewAsync();
        }

        try
        {
            var debugInfo = BuildDebugInfoReport();
            var filePath = await dialogService.ShowSaveFileDialogAsync(
                "Export debug info",
                BuildDefaultDebugInfoFileName(),
                DebugInfoFileDialogFilter);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                StatusMessage = "Debug info export canceled.";
                return;
            }

            IsBusy = true;
            await File.WriteAllTextAsync(filePath, debugInfo, Encoding.UTF8);

            StatusMessage = $"Debug info exported to '{filePath}'.";
            logger.Information($"Debug info exported to '{filePath}'.");
            await dialogService.ShowInformationAsync(
                "Debug info exported",
                $"Debug info was exported to:\n{filePath}");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Debug info export canceled.";
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Debug info export failed.");
            StatusMessage = $"Debug info export failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void OpenLiveDiagnosticsFolder()
    {
        try
        {
            Directory.CreateDirectory(liveDiagnosticsDirectory);
            Process.Start(new ProcessStartInfo(liveDiagnosticsDirectory)
            {
                UseShellExecute = true,
            });
            StatusMessage = $"Opened live diagnostics folder: {liveDiagnosticsDirectory}";
            logger.Information($"Opened live diagnostics folder '{liveDiagnosticsDirectory}'.");
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Could not open the live diagnostics folder.");
            StatusMessage = "Could not open live diagnostics folder. Check logs for details.";
        }
    }

    public async Task RunTranslationPipelineAsync()
    {
        PipelineStatusSeverity = PipelineStatusSeverity.Normal;
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
            PipelineStatusSeverity = PipelineStatusSeverity.Error;
            StatusMessage = PipelineStatus;
            latestOcrPreviewResult = null;
            ReplaceOcrPreviewTextBlocks(Array.Empty<OcrTextBlock>());
        }
        catch (OperationCanceledException)
        {
            RestoreOverlayPreviewAfterFailedCapture(overlayWasVisibleBeforeCapture, overlaySnapshotBeforeCapture);
            PipelineStatus = "Full translation pipeline canceled.";
            PipelineStatusSeverity = PipelineStatusSeverity.Normal;
            StatusMessage = PipelineStatus;
        }
        catch (Exception exception)
        {
            RestoreOverlayPreviewAfterFailedCapture(overlayWasVisibleBeforeCapture, overlaySnapshotBeforeCapture);
            logger.Error(exception, "Unexpected full translation pipeline failure.");
            PipelineStatus = "Full translation pipeline failed. Check logs for details.";
            PipelineStatusSeverity = PipelineStatusSeverity.Error;
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
        PipelineStatusSeverity = PipelineStatusSeverity.Normal;
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
        var liveTiming = CreateLiveTranslationTiming(LiveTranslationTimingPreset);
        lastLiveTranslationTiming = liveTiming;
        lastLiveTranslationTimingPreset = LiveTranslationTimingPreset;
        lastLiveOcrPreprocessingPreset = SelectedOcrPreprocessingPreset.DisplayName;
        lastLiveTranslatorProvider = profile.TranslatorSettings.Provider;
        lastLiveSourceLanguage = profile.TranslatorSettings.SourceLanguage;
        lastLiveTargetLanguage = profile.TranslatorSettings.TargetLanguage;
        var cancellationSource = new CancellationTokenSource();
        liveTranslationCancellation = cancellationSource;
        lastCandidatePipelineReadiness = null;
        lastLiveTranslationFailureKind = null;
        latestCandidateLifecycleEvents.Clear();
        droppedCandidateLifecycleEventCount = 0;
        IsLiveTranslationRunning = true;
        PipelineStatus = $"Live translation running for {profile.OcrZones.Count} OCR zone(s). {CreateLiveTimingStatus(liveTiming)}";
        StatusMessage = PipelineStatus;
        QueueLiveDiagnosticsSnapshot("start-live");
        _ = RunLiveTranslationLoopAsync(profile, cancellationSource, liveTiming);

        await Task.Yield();
    }

    public void StopLiveTranslation()
    {
        if (!IsLiveTranslationRunning || liveTranslationCancellation is null)
        {
            QueueLiveDiagnosticsSnapshot("stop-live-without-session");
            if (overlayService.IsVisible)
            {
                HideLiveTranslationOverlay();
                NotifyCommandStateChanged();
            }

            return;
        }

        PipelineStatus = "Stopping live translation...";
        PipelineStatusSeverity = PipelineStatusSeverity.Normal;
        StatusMessage = PipelineStatus;
        QueueLiveDiagnosticsSnapshot("stop-live-requested");
        liveTranslationCancellation.Cancel();
        HideLiveTranslationOverlay();
        NotifyCommandStateChanged();
    }

    private async Task RunLiveTranslationLoopAsync(
        GameProfile profile,
        CancellationTokenSource cancellationSource,
        LiveTranslationTiming liveTiming)
    {
        var cancellationToken = cancellationSource.Token;
        try
        {
            using var liveSession = translationPipelineService.CreateLiveSession(
                profile,
                liveTiming.RunOptions,
                cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var refreshStopwatch = Stopwatch.StartNew();
                    var update = await liveSession.RefreshAsync();
                    ApplyLiveTranslationUpdate(profile, update, cancellationToken);
                    var remainingPollingInterval = liveTiming.PollingInterval - refreshStopwatch.Elapsed;
                    await WaitForNextLiveRefreshAsync(
                        liveSession,
                        profile,
                        remainingPollingInterval,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (TranslationPipelineException exception)
                {
                    lastLiveTranslationFailureKind = exception.GetType().Name;
                    logger.Error(exception, "Live translation pipeline failed.");
                    PipelineStatus = exception.Message;
                    PipelineStatusSeverity = PipelineStatusSeverity.Error;
                    StatusMessage = PipelineStatus;
                    await DelayAfterLiveTranslationFailureAsync(cancellationToken);
                }
                catch (Exception exception)
                {
                    lastLiveTranslationFailureKind = exception.GetType().Name;
                    logger.Error(exception, "Unexpected live translation pipeline failure.");
                    PipelineStatus = "Live translation failed. Check logs for details.";
                    PipelineStatusSeverity = PipelineStatusSeverity.Error;
                    StatusMessage = PipelineStatus;
                    await DelayAfterLiveTranslationFailureAsync(cancellationToken);
                }
            }
        }
        finally
        {
            if (ReferenceEquals(liveTranslationCancellation, cancellationSource))
            {
                if (overlayService.IsVisible)
                {
                    HideLiveTranslationOverlay();
                }
                else
                {
                    OverlayPreviewStatus = "Live translation overlay hidden.";
                    OnPropertyChanged(nameof(IsOverlayPreviewVisible));
                }

                liveTranslationCancellation.Dispose();
                liveTranslationCancellation = null;
                IsLiveTranslationRunning = false;
                PipelineStatus = "Live translation stopped.";
                PipelineStatusSeverity = PipelineStatusSeverity.Normal;
                StatusMessage = PipelineStatus;
                QueueLiveDiagnosticsSnapshot("live-stopped");
                NotifyCommandStateChanged();
            }
        }
    }

    private void ApplyLiveTranslationUpdate(
        GameProfile profile,
        LiveTranslationPipelineUpdate update,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyCandidateLifecycleTrace(
            update.CandidateLifecycleEvents,
            update.DroppedCandidateLifecycleEventCount);
        if (update.OverlayChanged)
        {
            ApplyBatchPipelineResult(profile, update.BatchResult, isLiveMode: true);
        }
        else if (update.BatchResult.HasFailures)
        {
            PipelineStatus = CreateLivePipelineStatus(update.BatchResult);
            PipelineStatusSeverity = ResolvePipelineStatusSeverity(update.BatchResult);
            StatusMessage = PipelineStatus;
            if (PipelineStatusSeverity != PipelineStatusSeverity.Normal
                && overlayService.CurrentSnapshot?.TextItems.Count is > 0)
            {
                OverlayPreviewStatus = "BingWeb is temporarily unavailable; keeping the previous overlay visible.";
            }
        }

        ApplyCandidatePipelineReadinessStatus(update.CandidateReadiness);
    }

    private async Task WaitForNextLiveRefreshAsync(
        TranslationPipelineService.LiveTranslationSession liveSession,
        GameProfile profile,
        TimeSpan remainingPollingInterval,
        CancellationToken cancellationToken)
    {
        if (remainingPollingInterval <= TimeSpan.Zero)
        {
            return;
        }

        var pollingDelay = Task.Delay(remainingPollingInterval, cancellationToken);
        while (!pollingDelay.IsCompleted)
        {
            using var completionWaitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var workCompletion = liveSession.WaitForWorkCompletionAsync(completionWaitCancellation.Token);
            var completedTask = await Task.WhenAny(pollingDelay, workCompletion);
            if (ReferenceEquals(completedTask, pollingDelay))
            {
                completionWaitCancellation.Cancel();
                try
                {
                    await workCompletion;
                }
                catch (OperationCanceledException) when (completionWaitCancellation.IsCancellationRequested)
                {
                }

                break;
            }

            await workCompletion;
            var completionUpdate = await liveSession.PublishCompletedWorkAsync();
            ApplyLiveTranslationUpdate(profile, completionUpdate, cancellationToken);
        }

        await pollingDelay;
    }

    private void HideLiveTranslationOverlay()
    {
        overlayService.Hide();
        OverlayPreviewStatus = "Live translation overlay hidden.";
        OnPropertyChanged(nameof(IsOverlayPreviewVisible));
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

    private static LiveTranslationTimingPreset NormalizeLiveTranslationTimingPreset(LiveTranslationTimingPreset preset)
    {
        return Array.IndexOf(SupportedLiveTranslationTimingPresets, preset) >= 0
            ? preset
            : LiveTranslationTimingPreset.Balanced;
    }

    private static LiveTranslationTiming CreateLiveTranslationTiming(LiveTranslationTimingPreset preset)
    {
        var normalizedPreset = NormalizeLiveTranslationTimingPreset(preset);
        var (pollingInterval, stableTextInterval, confirmationObservations) = normalizedPreset switch
        {
            LiveTranslationTimingPreset.Fast => (
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(200),
                2),
            LiveTranslationTimingPreset.Conservative => (
                TimeSpan.FromMilliseconds(160),
                TimeSpan.FromMilliseconds(320),
                4),
            _ => (
                TimeSpan.FromMilliseconds(125),
                TimeSpan.FromMilliseconds(250),
                3),
        };

        return new LiveTranslationTiming(
            pollingInterval,
            stableTextInterval,
            new TranslationPipelineRunOptions(
                requireStableTextBeforeTranslation: true,
                stableTextInterval: stableTextInterval,
                preservePreviousOverlayWhileWaitingForStableText: true,
                restorePreviousOverlayAfterCapture: true,
                enableCandidateDetectorPilot: true,
                minimumCandidateGroupingObservations: confirmationObservations,
                minimumStableTextObservations: confirmationObservations)
            {
                MinimumCandidateGroupingDuration = stableTextInterval,
            });
    }

    private static string CreateLiveTimingStatus(LiveTranslationTiming timing)
    {
        return $"Polling {timing.PollingInterval.TotalMilliseconds:0} ms; translating after "
            + $"{timing.RunOptions.MinimumCandidateGroupingObservations} matching grouping observations over at least "
            + $"{timing.RunOptions.MinimumCandidateGroupingDuration.TotalMilliseconds:0} ms and "
            + $"{timing.RunOptions.MinimumStableTextObservations} matching OCR observations over at least "
            + $"{timing.StableTextInterval.TotalMilliseconds:0} ms.";
    }

    private void ApplyCandidateLifecycleTrace(
        IReadOnlyList<LiveCandidateLifecycleEvent> lifecycleEvents,
        int droppedEventCount)
    {
        ArgumentNullException.ThrowIfNull(lifecycleEvents);
        if (droppedEventCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(droppedEventCount));
        }

        var newlyDroppedEventCount = droppedEventCount - droppedCandidateLifecycleEventCount;
        if (newlyDroppedEventCount < 0)
        {
            latestCandidateLifecycleEvents.Clear();
        }
        else if (newlyDroppedEventCount >= latestCandidateLifecycleEvents.Count)
        {
            latestCandidateLifecycleEvents.Clear();
        }
        else if (newlyDroppedEventCount > 0)
        {
            latestCandidateLifecycleEvents.RemoveRange(0, newlyDroppedEventCount);
        }

        latestCandidateLifecycleEvents.AddRange(lifecycleEvents);
        droppedCandidateLifecycleEventCount = droppedEventCount;
    }

    private void ApplyCandidatePipelineReadinessStatus(CandidatePipelineReadiness readiness)
    {
        lastCandidatePipelineReadiness = readiness;
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
                if (IsLiveTranslationRunning || overlayService.IsVisible)
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
            case GlobalHotkeyAction.CollectDebugInfo:
                if (IsBusy)
                {
                    StatusMessage = "Debug info hotkey received while an operation is already running.";
                    return;
                }

                await CollectDebugInfoAsync();
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
        if (overlayService.IsExcludedFromCapture)
        {
            return snapshot;
        }

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
        latestCandidateGroupingEntries = CreateCandidateGroupingDebugEntries(previewEntries);
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
            latestCandidateGroupingEntries = Array.Empty<CandidateGroupingDebugEntry>();
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
        PipelineStatusSeverity = ResolvePipelineStatusSeverity(result);
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

    private static IReadOnlyList<CandidateGroupingDebugEntry> CreateCandidateGroupingDebugEntries(
        IEnumerable<BatchOcrPreviewEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries
            .Where(entry => entry.ZoneId.Contains(":candidate:", StringComparison.Ordinal))
            .Select(entry => new CandidateGroupingDebugEntry(
                entry.ZoneId,
                entry.SourceOcrResult.TextBlockSources
                    .SelectMany(source => source.MemberBounds)
                    .Distinct()
                    .ToArray()))
            .ToArray();
    }

    private sealed record BatchOcrPreviewEntry(
        string ZoneId,
        string ZoneName,
        CapturedFrame CapturedFrame,
        OcrResult SourceOcrResult);

    private sealed record CandidateGroupingDebugEntry(
        string CandidateId,
        IReadOnlyList<BoundingBox> MemberBounds);

    private sealed record LiveTranslationTiming(
        TimeSpan PollingInterval,
        TimeSpan StableTextInterval,
        TranslationPipelineRunOptions RunOptions);

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

        var providerDiagnostic = CreateProviderDiagnosticStatus(result);
        return result.HasFailures
            ? $"{status} {result.FailedZoneCount} of {result.TotalZoneCount} zone(s) failed.{providerDiagnostic}"
            : status + providerDiagnostic;
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

        var providerDiagnostic = CreateProviderDiagnosticStatus(result);
        status += providerDiagnostic;

        return result.SkippedOcrCount == 0
            ? status
            : status.TrimEnd('.') + $"; skipped OCR/translation for {result.SkippedOcrCount} unchanged zone(s).";
    }

    private static string CreatePipelineFailureDetail(TranslationPipelineZoneFailure failure)
    {
        var providerException = FindTranslatorProviderException(failure.Exception);
        if (providerException is not null)
        {
            return $"{failure.Message} {CreateTranslatorProviderFailureDetail(providerException)}";
        }

        var innerMessage = failure.Exception.InnerException?.Message;
        if (string.IsNullOrWhiteSpace(innerMessage)
            || string.Equals(innerMessage, failure.Message, StringComparison.Ordinal))
        {
            return failure.Message;
        }

        return $"{failure.Message} {innerMessage.Trim()}";
    }

    private static PipelineStatusSeverity ResolvePipelineStatusSeverity(TranslationPipelineBatchResult result)
    {
        var severity = PipelineStatusSeverity.Normal;
        foreach (var failure in result.ZoneFailures)
        {
            var providerException = FindTranslatorProviderException(failure.Exception);
            if (providerException is null
                || !string.Equals(providerException.ProviderId, "BingWeb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (providerException.FailureKind == TranslatorProviderFailureKind.Throttled
                || (providerException.FailureKind == TranslatorProviderFailureKind.Timeout
                    && (providerException.RetryAfter.HasValue
                        || providerException.ConsecutiveFailureCount >= 2)))
            {
                return PipelineStatusSeverity.Error;
            }

            if (providerException.FailureKind == TranslatorProviderFailureKind.Timeout)
            {
                severity = PipelineStatusSeverity.Warning;
            }
        }

        return severity;
    }

    private static string CreateProviderDiagnosticStatus(TranslationPipelineBatchResult result)
    {
        var diagnostics = result.ZoneResults
            .Select(zoneResult => zoneResult.TranslateResponse?.DiagnosticMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (diagnostics.Length > 0)
        {
            return " " + string.Join(" ", diagnostics);
        }

        var providers = result.ZoneResults
            .Select(zoneResult => zoneResult.TranslateResponse?.ProviderId)
            .Where(provider => !string.IsNullOrWhiteSpace(provider) && IsExperimentalWebProvider(provider!))
            .Select(provider => provider!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return providers.Length == 1
            ? $" Provider: {providers[0]}."
            : string.Empty;
    }

    private static TranslatorProviderException? FindTranslatorProviderException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is TranslatorProviderException providerException)
            {
                return providerException;
            }
        }

        return null;
    }

    private static string CreateTranslatorProviderFailureDetail(TranslatorProviderException exception)
    {
        var status = exception.StatusCode is null
            ? string.Empty
            : $" HTTP {(int)exception.StatusCode.Value}.";

        return $"Provider {exception.ProviderId} {FormatTranslatorProviderFailureKind(exception.FailureKind)}.{status} {exception.Message}";
    }

    private static string FormatTranslatorProviderFailureKind(TranslatorProviderFailureKind failureKind)
    {
        return failureKind switch
        {
            TranslatorProviderFailureKind.Configuration => "configuration failure",
            TranslatorProviderFailureKind.Http => "HTTP failure",
            TranslatorProviderFailureKind.Throttled => "throttled",
            TranslatorProviderFailureKind.Timeout => "timed out",
            TranslatorProviderFailureKind.EmptyResponse => "empty response",
            TranslatorProviderFailureKind.Parse => "parse failure",
            TranslatorProviderFailureKind.UnsupportedResponse => "unsupported response",
            TranslatorProviderFailureKind.ProviderCode => "provider-code failure",
            TranslatorProviderFailureKind.AllProvidersFailed => "fallback failure",
            TranslatorProviderFailureKind.Unexpected => "unexpected failure",
            _ => "failure",
        };
    }

    private static bool IsExperimentalWebProvider(string provider)
    {
        return string.Equals(provider, "GoogleWeb", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "BingWeb", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "YandexWeb", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateRemovedTranslatorProviderStatus(string provider)
    {
        return $"{provider.Trim()} is no longer supported. Choose another translator provider.";
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

    private void ApplyOcrPreprocessingSettings(OcrPreprocessingSettings settings)
    {
        isApplyingOcrPreprocessingPreset = true;
        try
        {
            OcrPreprocessingEnabled = settings.IsEnabled;
            OcrPreprocessingContrast = settings.Contrast;
            OcrPreprocessingBrightness = settings.Brightness;
            OcrPreprocessingSharpness = settings.Sharpness;
            OcrPreprocessingThresholdingEnabled = settings.ThresholdingEnabled;
            OcrPreprocessingThreshold = settings.Threshold;
            OcrPreprocessingScale = settings.Scale;
            OcrPreprocessingNoiseReductionEnabled = settings.NoiseReductionEnabled;
        }
        finally
        {
            isApplyingOcrPreprocessingPreset = false;
        }

        SyncOcrPreprocessingPresetSelection();
    }

    private void SyncOcrPreprocessingPresetSelection()
    {
        if (isApplyingOcrPreprocessingPreset)
        {
            return;
        }

        var matchingPreset = FindMatchingOcrPreprocessingPreset(BuildOcrPreprocessingSettings())
            ?? SupportedOcrPreprocessingPresetOptions[0];

        isSyncingOcrPreprocessingPresetSelection = true;
        try
        {
            SelectedOcrPreprocessingPreset = matchingPreset;
        }
        finally
        {
            isSyncingOcrPreprocessingPresetSelection = false;
        }
    }

    private static OcrPreprocessingPresetOption? FindMatchingOcrPreprocessingPreset(OcrPreprocessingSettings settings)
    {
        return SupportedOcrPreprocessingPresetOptions
            .Where(preset => preset.Settings is not null)
            .FirstOrDefault(preset => AreEquivalent(preset.Settings!, settings));
    }

    private static bool AreEquivalent(OcrPreprocessingSettings left, OcrPreprocessingSettings right)
    {
        return left.IsEnabled == right.IsEnabled
            && Math.Abs(left.Contrast - right.Contrast) < 0.001
            && left.Brightness == right.Brightness
            && Math.Abs(left.Sharpness - right.Sharpness) < 0.001
            && left.ThresholdingEnabled == right.ThresholdingEnabled
            && left.Threshold == right.Threshold
            && Math.Abs(left.Scale - right.Scale) < 0.001
            && left.NoiseReductionEnabled == right.NoiseReductionEnabled;
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
            OcrOrientationMode = OcrSettings.IsSupportedOrientationMode(profile.OcrSettings.OrientationMode)
                ? profile.OcrSettings.OrientationMode
                : OcrSettings.Default.OrientationMode;
        });

        SyncOcrPreprocessingPresetSelection();
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
            OcrOrientationMode = settings.GetValue<OcrOrientationMode?>(DraftOcrOrientationModeSettingKey) ?? OcrSettings.Default.OrientationMode;
            if (!string.IsNullOrWhiteSpace(draftSelectedZoneId))
            {
                SelectedZone = OcrZones.FirstOrDefault(zone => string.Equals(zone.Id, draftSelectedZoneId, StringComparison.Ordinal))
                    ?? OcrZones.FirstOrDefault();
            }
        });

        SyncOcrPreprocessingPresetSelection();
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
            && !HasOcrEngineLanguageCompatibilityError(SelectedZone)
            && !string.IsNullOrWhiteSpace(ResolveSelectedOcrLanguage());
    }

    private bool CanCollectDebugInfo()
    {
        return !IsBusy;
    }

    private bool CanManageOcrLanguagePack()
    {
        return !IsBusy
            && !IsLiveTranslationRunning
            && SelectedZone is not null
            && !string.IsNullOrWhiteSpace(OcrEngine)
            && !string.IsNullOrWhiteSpace(ResolveSelectedOcrLanguage());
    }

    private bool CanManageOcrLanguagePackChecklist()
    {
        return !IsBusy && !IsLiveTranslationRunning;
    }

    private bool CanRunTranslationPipeline()
    {
        return !IsBusy
            && !IsLiveTranslationRunning
            && !HasValidationErrors
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
            && !HasValidationErrors
            && OcrZones.Count > 0
            && !string.IsNullOrWhiteSpace(TranslatorProvider)
            && !string.IsNullOrWhiteSpace(SourceLanguage)
            && !string.IsNullOrWhiteSpace(TargetLanguage)
            && !string.IsNullOrWhiteSpace(OcrEngine);
    }

    private bool CanStopLiveTranslation()
    {
        return IsLiveTranslationRunning || overlayService.IsVisible;
    }

    private bool CanSelectTranslatorProvider()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(TranslatorProvider);
    }

    private bool CanSaveTranslatorCredentials()
    {
        return CanSelectTranslatorProvider()
            && RequiresStoredTranslatorCredentials
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

        if (TranslatorCredentialService.IsRemovedProvider(TranslatorProvider))
        {
            TranslatorCredentialEndpoint = string.Empty;
            TranslatorCredentialSecret = string.Empty;
            HasStoredTranslatorCredentials = false;
            TranslatorCredentialStatus = CreateRemovedTranslatorProviderStatus(TranslatorProvider);
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

    private void ResetOcrLanguagePackStatus()
    {
        OcrLanguagePackStatus = "OCR language pack status not checked.";
    }

    private void UpdateOcrLanguagePackChecklistSummary(string prefix)
    {
        var readyCount = OcrLanguagePackChecklistItems.Count(item => item.IsReady);
        var missingCount = OcrLanguagePackChecklistItems.Count(item => !item.IsReady && item.CanInstall);
        var notCheckedCount = OcrLanguagePackChecklistItems.Count(IsOcrLanguagePackChecklistItemNotChecked);
        var blockedCount = OcrLanguagePackChecklistItems.Count(item =>
            !item.IsReady && !item.CanInstall && !IsOcrLanguagePackChecklistItemNotChecked(item));
        OcrLanguagePackStatus = $"{prefix} Ready {readyCount}, missing {missingCount}, blocked {blockedCount}, not checked {notCheckedCount}.";
        StatusMessage = OcrLanguagePackStatus;
    }

    private static bool IsOcrLanguagePackChecklistItemNotChecked(OcrLanguagePackChecklistItemViewModel item)
    {
        return string.Equals(item.State, "Not checked", StringComparison.Ordinal);
    }

    private string ResolveSelectedOcrLanguage()
    {
        return SelectedZone is null ? string.Empty : ResolveOcrLanguage(SelectedZone);
    }

    private string ResolveOcrLanguage(OcrZoneEditorViewModel zone)
    {
        return string.IsNullOrWhiteSpace(zone.OcrLanguage)
            ? SourceLanguage.Trim()
            : zone.OcrLanguage.Trim();
    }

    private IEnumerable<string> CreateOcrEngineLanguageCompatibilityErrors()
    {
        if (!string.Equals(OcrEngine.Trim(), OcrSettings.WindowsEngineId, StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        foreach (var zone in OcrZones)
        {
            var error = CreateOcrEngineLanguageCompatibilityError(zone);
            if (!string.IsNullOrWhiteSpace(error))
            {
                yield return error;
            }
        }
    }

    private string CreateOcrEngineLanguageCompatibilityError(OcrZoneEditorViewModel zone)
    {
        var language = ResolveOcrLanguage(zone);
        if (!IsTesseractOnlyLanguage(language))
        {
            return string.Empty;
        }

        return $"OCR zone '{zone.DisplayName}' uses Tesseract OCR language '{language}'. Select Tesseract OCR engine or use a Windows OCR language tag such as en, ja, or zh-Hans.";
    }

    private bool HasOcrEngineLanguageCompatibilityError(OcrZoneEditorViewModel zone)
    {
        return !string.IsNullOrWhiteSpace(CreateOcrEngineLanguageCompatibilityError(zone));
    }

    private static bool IsTesseractOnlyLanguage(string language)
    {
        return TesseractLanguageCatalog.TryGetTrainedDataCode(language, out _);
    }

    private OcrOrientationMode NormalizeOcrOrientationMode(OcrOrientationMode orientationMode)
    {
        if (!Enum.IsDefined(orientationMode))
        {
            return OcrSettings.Default.OrientationMode;
        }

        return SupportsVerticalOcrLayout()
            ? orientationMode
            : OcrOrientationMode.Horizontal;
    }

    private void EnsureOcrOrientationMatchesConfiguredLanguages()
    {
        OcrOrientationMode = NormalizeOcrOrientationMode(ocrOrientationMode);
    }

    private bool SupportsVerticalOcrLayout()
    {
        var languages = OcrZones
            .Select(ResolveOcrLanguage)
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (languages.Length == 0 && !string.IsNullOrWhiteSpace(SourceLanguage))
        {
            languages = new[] { SourceLanguage };
        }

        return languages.Length == 0
            || languages.All(TesseractLanguageCatalog.SupportsVerticalTextLayout);
    }

    private static string NormalizeTranslatorLanguageTag(string languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return languageTag;
        }

        var normalizedLanguageTag = languageTag.Trim();
        if (string.Equals(normalizedLanguageTag, "zh", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-CN";
        }

        return TesseractLanguageCatalog.TryMapTrainedDataCodeToPreferredLanguageTag(normalizedLanguageTag, out var preferredLanguageTag)
            ? preferredLanguageTag
            : normalizedLanguageTag;
    }

    private static void OpenExternalUri(Uri uri)
    {
        Process.Start(new ProcessStartInfo(uri.ToString())
        {
            UseShellExecute = true,
        });
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
        OnPropertyChanged(nameof(IsOcrOrientationSelectionEnabled));
        OnPropertyChanged(nameof(OcrOrientationSelectionToolTip));
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
        if (string.Equals(e.PropertyName, nameof(OcrZoneEditorViewModel.OcrLanguage), StringComparison.Ordinal))
        {
            EnsureOcrOrientationMatchesConfiguredLanguages();
            OnPropertyChanged(nameof(IsOcrOrientationSelectionEnabled));
            OnPropertyChanged(nameof(OcrOrientationSelectionToolTip));
            ResetOcrLanguagePackStatus();
            NotifyCommandStateChanged();
        }

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
        var translatorProviderErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(TranslatorProvider))
        {
            translatorProviderErrors.Add("Translator provider is required.");
        }
        else if (TranslatorCredentialService.IsRemovedProvider(TranslatorProvider))
        {
            translatorProviderErrors.Add(CreateRemovedTranslatorProviderStatus(TranslatorProvider));
        }

        SetErrors(nameof(TranslatorProvider), translatorProviderErrors);
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
        var ocrEngineErrors = new List<string>();
        if (!OcrSettings.IsSupportedEngine(OcrEngine))
        {
            ocrEngineErrors.Add("OCR engine must be Windows or Tesseract.");
        }
        else
        {
            ocrEngineErrors.AddRange(CreateOcrEngineLanguageCompatibilityErrors());
        }

        SetErrors(nameof(OcrEngine), ocrEngineErrors);
        OnPropertyChanged(nameof(HasOcrEngineValidationError));
        OnPropertyChanged(nameof(OcrEngineValidationMessage));
        OnPropertyChanged(nameof(OcrEngineBorderBrush));
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

    private static string NormalizeOcrEngine(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.Equals(normalized, OcrSettings.WindowsEngineId, StringComparison.OrdinalIgnoreCase))
        {
            return OcrSettings.WindowsEngineId;
        }

        if (string.Equals(normalized, OcrSettings.TesseractEngineId, StringComparison.OrdinalIgnoreCase))
        {
            return OcrSettings.TesseractEngineId;
        }

        return normalized;
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

    private static string BuildDefaultDebugInfoFileName()
    {
        return $"game-translator-debug-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.txt";
    }

    private static string ResolveLiveDiagnosticsDirectory(ISettingsService settings)
    {
        var configuredDirectory = settings.GetValue<string>(LiveDiagnosticsDirectorySettingKey);
        return string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GameTranslator",
                "Diagnostics",
                "Live")
            : configuredDirectory.Trim();
    }

    private void QueueLiveDiagnosticsSnapshot(string trigger)
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var report = BuildLiveDiagnosticsReport(trigger, capturedAt);
        var sequence = Interlocked.Increment(ref liveDiagnosticsSequence);
        _ = SaveLiveDiagnosticsSnapshotAsync(trigger, capturedAt, sequence, report);
    }

    private async Task SaveLiveDiagnosticsSnapshotAsync(
        string trigger,
        DateTimeOffset capturedAt,
        int sequence,
        string report)
    {
        try
        {
            Directory.CreateDirectory(liveDiagnosticsDirectory);
            var fileName = $"game-translator-live-{trigger}-{capturedAt:yyyyMMdd-HHmmss-fff}-{sequence:D4}.txt";
            var filePath = Path.Combine(liveDiagnosticsDirectory, fileName);
            var boundedReport = LimitLiveDiagnosticsReport(report, MaximumLiveDiagnosticsReportUtf8Bytes);
            await File.WriteAllTextAsync(filePath, boundedReport, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (boundedReport.Length != report.Length)
            {
                logger.Warning(
                    $"Live diagnostics report for '{trigger}' was truncated to {MaximumLiveDiagnosticsReportUtf8Bytes} UTF-8 bytes.");
            }

            logger.Information($"Live diagnostics saved for '{trigger}' to '{filePath}'.");
        }
        catch (Exception exception)
        {
            logger.Error(exception, $"Live diagnostics could not be saved for '{trigger}'.");
        }
    }

    private string BuildLiveDiagnosticsReport(string trigger, DateTimeOffset capturedAt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Game Translator live-session diagnostics");
        builder.AppendLine($"Trigger: {trigger}");
        builder.AppendLine($"CapturedUtc: {capturedAt:O}");
        builder.AppendLine($"Directory: {liveDiagnosticsDirectory}");
        builder.AppendLine($"MaximumUtf8Bytes: {MaximumLiveDiagnosticsReportUtf8Bytes}");
        builder.AppendLine("Privacy: profile free-text fields and credential values are omitted.");
        builder.AppendLine("Storage: local files only; no diagnostics upload is performed.");
        builder.AppendLine();
        builder.Append(BuildDebugInfoReport());

        return builder.ToString();
    }

    private static string LimitLiveDiagnosticsReport(string report, int maximumUtf8Bytes)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (maximumUtf8Bytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumUtf8Bytes));
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        if (encoding.GetByteCount(report) <= maximumUtf8Bytes)
        {
            return report;
        }

        const string truncationMarker = "\r\n[diagnostics truncated: oldest report body omitted to enforce the UTF-8 size limit]\r\n";
        var markerBytes = encoding.GetByteCount(truncationMarker);
        if (markerBytes >= maximumUtf8Bytes)
        {
            return GetUtf8Prefix(report, maximumUtf8Bytes, encoding);
        }

        var prefixByteLimit = Math.Min(
            LiveDiagnosticsReportPrefixUtf8Bytes,
            (maximumUtf8Bytes - markerBytes) / 4);
        var prefix = GetUtf8Prefix(report, prefixByteLimit, encoding);
        var suffixByteLimit = maximumUtf8Bytes - markerBytes - encoding.GetByteCount(prefix);
        var suffix = GetUtf8Suffix(report, suffixByteLimit, encoding);
        return string.Concat(prefix, truncationMarker, suffix);
    }

    private static string GetUtf8Prefix(string value, int maximumUtf8Bytes, Encoding encoding)
    {
        if (maximumUtf8Bytes <= 0 || value.Length == 0)
        {
            return string.Empty;
        }

        var length = FindMaximumUtf8PrefixLength(value, maximumUtf8Bytes, encoding);
        while (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }

    private static string GetUtf8Suffix(string value, int maximumUtf8Bytes, Encoding encoding)
    {
        if (maximumUtf8Bytes <= 0 || value.Length == 0)
        {
            return string.Empty;
        }

        var low = 0;
        var high = value.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (encoding.GetByteCount(value.AsSpan(middle)) <= maximumUtf8Bytes)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        var start = low;
        while (start < value.Length && char.IsLowSurrogate(value[start]))
        {
            start++;
        }

        return value[start..];
    }

    private static int FindMaximumUtf8PrefixLength(string value, int maximumUtf8Bytes, Encoding encoding)
    {
        var low = 0;
        var high = value.Length;
        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);
            if (encoding.GetByteCount(value.AsSpan(0, middle)) <= maximumUtf8Bytes)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
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

    private void ClearZoneMoveState()
    {
        isZoneMoveActive = false;
    }

    private void ClearProfileRenameState()
    {
        IsProfileRenameActive = false;
        ProfileRenameText = string.Empty;
    }

    private void ClearSurfaceInteractionState()
    {
        ClearZoneSelectionPreview();
        ClearZoneResizeState();
        ClearZoneMoveState();
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
        latestCandidateGroupingEntries = Array.Empty<CandidateGroupingDebugEntry>();
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

    private string BuildDebugInfoReport()
    {
        var builder = new StringBuilder();
        var generatedAt = DateTimeOffset.Now;

        builder.AppendLine("Game Translator debug info");
        builder.AppendLine($"GeneratedLocal: {generatedAt:O}");
        builder.AppendLine($"Application: {ApplicationName}");
        builder.AppendLine("Privacy: profile free-text fields and credential values are omitted.");
        builder.AppendLine();

        builder.AppendLine("State");
        builder.AppendLine($"  IsBusy: {IsBusy}");
        builder.AppendLine($"  IsLiveTranslationRunning: {IsLiveTranslationRunning}");
        builder.AppendLine($"  IsOverlayServiceVisible: {overlayService.IsVisible}");
        AppendDebugLine(builder, "LastLiveTranslationFailureKind", lastLiveTranslationFailureKind ?? "(none)");
        builder.AppendLine($"  HasCapturePreview: {HasCapturePreview}");
        builder.AppendLine($"  CapturePreviewSize: {CapturePreviewWidth}x{CapturePreviewHeight}");
        builder.AppendLine($"  HasOcrPreview: {HasOcrPreview}");
        builder.AppendLine($"  OcrPreviewBlockCount: {OcrDebugTextBlocks.Count}");
        builder.AppendLine($"  IsOverlayPreviewVisible: {IsOverlayPreviewVisible}");
        builder.AppendLine($"  IsDebugOverlayEnabled: {IsDebugOverlayEnabled}");
        var liveTiming = lastLiveTranslationTiming ?? CreateLiveTranslationTiming(LiveTranslationTimingPreset);
        builder.AppendLine($"  LiveTimingPreset: {lastLiveTranslationTimingPreset?.ToString() ?? "(not started; current selection shown below)"}");
        builder.AppendLine($"  CurrentLiveTimingSelection: {LiveTranslationTimingPreset}");
        builder.AppendLine($"  LivePollingIntervalMs: {liveTiming.PollingInterval.TotalMilliseconds:0}");
        builder.AppendLine($"  RequiredGroupingObservations: {liveTiming.RunOptions.MinimumCandidateGroupingObservations}");
        builder.AppendLine($"  MinimumGroupingDurationMs: {liveTiming.RunOptions.MinimumCandidateGroupingDuration.TotalMilliseconds:0}");
        builder.AppendLine($"  StableTextIntervalMs: {liveTiming.StableTextInterval.TotalMilliseconds:0}");
        builder.AppendLine($"  RequiredOcrTextObservations: {liveTiming.RunOptions.MinimumStableTextObservations}");
        builder.AppendLine($"  OcrPreprocessingPresetAtLastStart: {lastLiveOcrPreprocessingPreset}");
        builder.AppendLine($"  CurrentOcrPreprocessingSelection: {SelectedOcrPreprocessingPreset.DisplayName}");
        builder.AppendLine($"  TranslatorProviderAtLastStart: {lastLiveTranslatorProvider}");
        builder.AppendLine($"  TranslatorLanguagesAtLastStart: {lastLiveSourceLanguage}->{lastLiveTargetLanguage}");
        builder.AppendLine();

        builder.AppendLine("Statuses");
        AppendDebugLine(builder, "CapturePreviewStatus", CapturePreviewStatus);
        AppendDebugLine(builder, "CaptureRefreshMetricsSummary", CaptureRefreshMetricsSummary);
        AppendDebugLine(builder, "OcrPreviewStatus", OcrPreviewStatus);
        AppendDebugLine(builder, "PipelineStatus", PipelineStatus);
        AppendDebugLine(builder, "OverlayPreviewStatus", OverlayPreviewStatus);
        AppendDebugLine(builder, "DebugOverlayStatus", DebugOverlayStatus);
        AppendDebugLine(builder, "GlobalHotkeyStatus", GlobalHotkeyStatus);
        AppendDebugLine(builder, "StatusMessage", StatusMessage);
        builder.AppendLine();

        builder.AppendLine("Selected zone");
        if (SelectedZone is null)
        {
            builder.AppendLine("  none");
        }
        else
        {
            var selectedZoneIndex = OcrZones.IndexOf(SelectedZone) + 1;
            builder.AppendLine($"  Index: {selectedZoneIndex}");
            builder.AppendLine($"  Id: {SelectedZone.Id}");
            builder.AppendLine($"  Absolute: {SelectedZone.AbsoluteBoundsSummary}");
            builder.AppendLine($"  Relative: {SelectedZone.RelativeBoundsSummary}");
            builder.AppendLine($"  OCR engine: {OcrEngine}");
            builder.AppendLine($"  OCR language: {ResolveSelectedOcrLanguage()}");
            builder.AppendLine($"  OCR orientation: {OcrOrientationMode}");
            builder.AppendLine($"  Overlay style: {SelectedZone.OverlayTextStyleSummary}");
            builder.AppendLine($"  Content layout: {SelectedZone.ContentLayoutModeSummary}");
            builder.AppendLine($"  Content policy: {SelectedZone.ContentLayoutPolicySummary}");
            builder.AppendLine($"  Candidate grouping: {SelectedZone.CandidateGroupingSummary}");
            builder.AppendLine($"  Legacy grouping (diagnostic only): {SelectedZone.TranslationGroupingModeSummary}");
        }

        builder.AppendLine();
        builder.AppendLine("OCR debug blocks");
        if (OcrDebugTextBlocks.Count == 0)
        {
            builder.AppendLine("  none");
        }
        else
        {
            for (var index = 0; index < OcrDebugTextBlocks.Count; index++)
            {
                var block = OcrDebugTextBlocks[index];
                builder.AppendLine(
                    $"  [{index + 1}] {block.CoordinatesSummary} VisibleOnCapturePreview={block.IsVisibleOnCapturePreview} Text={SanitizeDebugText(block.Text)}");
            }
        }

        AppendCandidateGroupingDebugInfo(builder);

        AppendLiveCandidateReadinessDebugInfo(builder);
        AppendLiveTranslatorProviderFailureDebugInfo(builder, generatedAt);
        AppendLiveCandidateLifecycleDebugInfo(builder);
        AppendOverlaySnapshotDebugInfo(builder);

        return builder.ToString();
    }

    private void AppendCandidateGroupingDebugInfo(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("Candidate grouping geometry");
        if (latestCandidateGroupingEntries.Count == 0)
        {
            builder.AppendLine("  none");
            return;
        }

        for (var index = 0; index < latestCandidateGroupingEntries.Count; index++)
        {
            var entry = latestCandidateGroupingEntries[index];
            var memberBounds = string.Join(
                "; ",
                entry.MemberBounds.Select(bounds => $"X {bounds.X} Y {bounds.Y} W {bounds.Width} H {bounds.Height}"));
            builder.AppendLine(
                $"  [{index + 1}] Id={entry.CandidateId} SourceCandidates={entry.MemberBounds.Count} MemberBounds={memberBounds}");
        }
    }

    private void AppendLiveCandidateReadinessDebugInfo(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("Live candidate pipeline");

        if (lastCandidatePipelineReadiness is null)
        {
            builder.AppendLine("  not initialized for this live session");
            return;
        }

        builder.AppendLine($"  Status: {lastCandidatePipelineReadiness.Status}");
        builder.AppendLine($"  Generation: {lastCandidatePipelineReadiness.Generation}");
        builder.AppendLine($"  RestartCount: {lastCandidatePipelineReadiness.RestartCount}");
        AppendDebugLine(
            builder,
            "UnavailableReason",
            lastCandidatePipelineReadiness.UnavailableReason ?? "(none)");
        builder.AppendLine(
            $"  NextRetryAt: {lastCandidatePipelineReadiness.NextRetryAt?.ToString("O") ?? "(none)"}");
    }

    private void AppendLiveCandidateLifecycleDebugInfo(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("Live candidate lifecycle");
        builder.AppendLine("  Text diagnostics: bounded OCR, translation-input, and translated text are recorded locally; provider responses, credentials, and frame pixels are not recorded.");
        builder.AppendLine("  Storage: local files only; no diagnostics upload is performed.");
        builder.AppendLine("  EventBufferCapacity: 131072");
        builder.AppendLine($"  RetainedEvents: {latestCandidateLifecycleEvents.Count}");
        builder.AppendLine($"  DroppedOldestEvents: {droppedCandidateLifecycleEventCount}");
        if (latestCandidateLifecycleEvents.Count == 0)
        {
            builder.AppendLine("  none");
            return;
        }

        foreach (var entry in latestCandidateLifecycleEvents)
        {
            builder.Append(
                $"  [{entry.Sequence}] Refresh={entry.RefreshSequence} At={entry.OccurredAt:O} Event={entry.Kind}");
            AppendLifecycleField(builder, "ZoneId", entry.ZoneId);
            AppendLifecycleField(builder, "CandidateId", entry.CandidateId);
            if (entry.CandidateBounds is { } candidateBounds)
            {
                AppendLifecycleField(builder, "CandidateBounds", FormatBounds(candidateBounds));
            }

            AppendLifecycleField(builder, "CandidateConfidence", FormatDiagnosticDouble(entry.CandidateConfidence));

            if (entry.SourceCandidateBounds.Count > 0)
            {
                AppendLifecycleField(builder, "SourceCandidates", entry.SourceCandidateBounds.Count.ToString());
                AppendLifecycleField(
                    builder,
                    "MemberBounds",
                    string.Join(";", entry.SourceCandidateBounds.Select(FormatBounds)));
            }

            AppendLifecycleField(
                builder,
                "WritingSystemProfile",
                entry.WritingSystemGroupingProfile?.ToString());
            AppendLifecycleField(builder, "OcrOrientation", entry.OcrOrientationMode?.ToString());
            AppendLifecycleField(builder, "DetectorPresetRequested", entry.RequestedDetectorPreset?.ToString());
            AppendLifecycleField(builder, "DetectorPresetEffective", entry.EffectiveDetectorPreset?.ToString());
            AppendLifecycleField(builder, "DetectorThreshold", FormatDiagnosticDouble(entry.DetectorThreshold));
            AppendLifecycleField(builder, "DetectorBoxThreshold", FormatDiagnosticDouble(entry.DetectorBoxThreshold));
            AppendLifecycleField(builder, "DetectorUnclipRatio", FormatDiagnosticDouble(entry.DetectorUnclipRatio));
            AppendLifecycleField(builder, "RawDetectorCandidates", entry.RawDetectorCandidateCount?.ToString(CultureInfo.InvariantCulture));
            AppendLifecycleField(builder, "DetectorConfidenceMin", FormatDiagnosticDouble(entry.MinimumDetectorConfidence));
            AppendLifecycleField(builder, "DetectorConfidenceMax", FormatDiagnosticDouble(entry.MaximumDetectorConfidence));
            AppendLifecycleField(builder, "DetectorConfidenceAverage", FormatDiagnosticDouble(entry.AverageDetectorConfidence));
            if (entry.OrderedOcrBlockBoundsCount > 0)
            {
                AppendLifecycleField(
                    builder,
                    "OrderedOcrBlocks",
                    entry.OrderedOcrBlockBoundsCount.ToString());
                AppendLifecycleField(
                    builder,
                    "OrderedOcrBoundsRetained",
                    entry.OrderedOcrBlockBounds.Count.ToString());
                AppendLifecycleField(
                    builder,
                    "OrderedOcrBoundsFingerprint",
                    entry.OrderedOcrBlockBoundsFingerprint);
                AppendLifecycleField(
                    builder,
                    "OrderedOcrBounds",
                    string.Join(";", entry.OrderedOcrBlockBounds.Select(FormatBounds)));
            }

            if (entry.OrderedGroupedMemberBoundsCount > 0)
            {
                AppendLifecycleField(
                    builder,
                    "OrderedGroupedMembers",
                    entry.OrderedGroupedMemberBoundsCount.ToString());
                AppendLifecycleField(
                    builder,
                    "OrderedGroupedMemberBoundsRetained",
                    entry.OrderedGroupedMemberBounds.Count.ToString());
                AppendLifecycleField(
                    builder,
                    "OrderedGroupedMemberBoundsFingerprint",
                    entry.OrderedGroupedMemberBoundsFingerprint);
                AppendLifecycleField(
                    builder,
                    "OrderedGroupedMemberBounds",
                    string.Join(";", entry.OrderedGroupedMemberBounds.Select(FormatBounds)));
            }

            if (entry.CandidateRevision > 0)
            {
                AppendLifecycleField(builder, "Revision", entry.CandidateRevision.ToString());
            }

            if (entry.WorkAttempt > 0)
            {
                AppendLifecycleField(builder, "WorkAttempt", entry.WorkAttempt.ToString());
            }

            if (entry.FrameCapturedAt is { } frameCapturedAt)
            {
                AppendLifecycleField(builder, "FrameCapturedAt", frameCapturedAt.ToString("O"));
            }

            if (entry.Elapsed is { } elapsed)
            {
                AppendLifecycleField(builder, "ElapsedMs", elapsed.TotalMilliseconds.ToString("F1"));
            }

            AppendLifecycleField(builder, "CandidateCount", entry.CandidateCount?.ToString());
            AppendLifecycleField(builder, "RecognizedBlocks", entry.RecognizedBlockCount?.ToString());
            AppendLifecycleField(builder, "TranslationInputGroups", entry.TranslationInputBlockCount?.ToString());
            AppendLifecycleField(builder, "TranslatedBlocks", entry.TranslatedBlockCount?.ToString());
            AppendLifecycleTextDiagnostics(
                builder,
                "OcrTextEntries",
                "OcrText",
                entry.OcrTextCount,
                entry.OcrTexts);
            AppendLifecycleTextDiagnostics(
                builder,
                "TranslationInputEntries",
                "TranslationInputText",
                entry.TranslationInputTextCount,
                entry.TranslationInputTexts);
            AppendLifecycleTextDiagnostics(
                builder,
                "TranslatedTextEntries",
                "TranslatedText",
                entry.TranslatedTextCount,
                entry.TranslatedTexts);
            AppendLifecycleField(builder, "GroupingObservations", entry.GroupingObservationCount?.ToString());
            AppendLifecycleField(builder, "RequiredGroupingObservations", entry.RequiredGroupingObservationCount?.ToString());
            if (entry.GroupingFirstObservedAt is { } groupingFirstObservedAt)
            {
                AppendLifecycleField(builder, "GroupingFirstObservedAt", groupingFirstObservedAt.ToString("O"));
            }

            if (entry.GroupingLastObservedAt is { } groupingLastObservedAt)
            {
                AppendLifecycleField(builder, "GroupingLastObservedAt", groupingLastObservedAt.ToString("O"));
            }

            if (entry.GroupingObservedDuration is { } groupingObservedDuration)
            {
                AppendLifecycleField(builder, "GroupingObservedMs", groupingObservedDuration.TotalMilliseconds.ToString("F1"));
            }

            if (entry.RequiredGroupingDuration is { } requiredGroupingDuration)
            {
                AppendLifecycleField(builder, "RequiredGroupingDurationMs", requiredGroupingDuration.TotalMilliseconds.ToString("F1"));
            }

            if (entry.TextStability is { } textStability)
            {
                AppendLifecycleField(builder, "StabilityRequired", textStability.IsRequired.ToString());
                AppendLifecycleField(builder, "StabilitySatisfied", textStability.IsStable.ToString());
                AppendLifecycleField(builder, "OcrObservations", textStability.ObservationCount.ToString());
                AppendLifecycleField(builder, "RequiredOcrObservations", textStability.RequiredObservationCount.ToString());
                AppendLifecycleField(
                    builder,
                    "RequiredStabilityDurationMs",
                    textStability.RequiredDuration.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture));
                AppendLifecycleField(
                    builder,
                    "TypewriterGrowthGuardApplied",
                    textStability.TypewriterGrowthGuardApplied.ToString());
                if (textStability.FirstObservedAt is { } firstObservedAt)
                {
                    AppendLifecycleField(builder, "StabilityFirstObservedAt", firstObservedAt.ToString("O"));
                }

                if (textStability.LastObservedAt is { } lastObservedAt)
                {
                    AppendLifecycleField(builder, "StabilityLastObservedAt", lastObservedAt.ToString("O"));
                }

                if (textStability.ObservedDuration is { } observedDuration)
                {
                    AppendLifecycleField(builder, "StabilityObservedMs", observedDuration.TotalMilliseconds.ToString("F1"));
                }
            }

            AppendLifecycleField(builder, "TranslationMemoryCacheHits", entry.TranslationMemoryCacheHitCount?.ToString());
            AppendLifecycleField(builder, "TranslationPersistentCacheHits", entry.TranslationPersistentCacheHitCount?.ToString());
            AppendLifecycleField(builder, "TranslationCacheMisses", entry.TranslationCacheMissCount?.ToString());
            AppendLifecycleField(builder, "TranslationCacheStored", entry.TranslationCacheStoredCount?.ToString());
            AppendLifecycleField(builder, "TranslationOutputsSanitized", entry.TranslationOutputSanitizedCount?.ToString());
            AppendLifecycleField(builder, "TranslationProvider", entry.TranslationProviderId);
            AppendLifecycleField(builder, "ProviderRequestStartedAt", entry.ProviderRequestStartedAt?.ToString("O"));
            AppendLifecycleField(builder, "ProviderRequestCompletedAt", entry.ProviderRequestCompletedAt?.ToString("O"));
            AppendLifecycleField(builder, "ProviderDiagnosticRequestId", entry.ProviderDiagnosticRequestId);
            AppendLifecycleField(builder, "ProviderRequestQueuedAt", entry.ProviderRequestQueuedAt?.ToString("O"));
            AppendLifecycleField(builder, "ProviderInvocationStartedAt", entry.ProviderInvocationStartedAt?.ToString("O"));
            AppendLifecycleField(builder, "ProviderInvocationCompletedAt", entry.ProviderInvocationCompletedAt?.ToString("O"));
            if (entry.ProviderRequestQueuedAt is { } queuedAt
                && entry.ProviderInvocationStartedAt is { } invocationStartedAt)
            {
                var queueWait = invocationStartedAt >= queuedAt
                    ? invocationStartedAt - queuedAt
                    : TimeSpan.Zero;
                AppendLifecycleField(
                    builder,
                    "ProviderQueueWaitMs",
                    queueWait.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture));
            }

            AppendLifecycleField(builder, "ProviderInvocationOutcome", entry.ProviderInvocationOutcome?.ToString());
            AppendLifecycleField(builder, "ProviderNetworkAttemptId", entry.ProviderNetworkAttemptId);
            AppendLifecycleField(builder, "ProviderNetworkKind", entry.ProviderNetworkRequestKind?.ToString());
            AppendLifecycleField(builder, "ProviderNetworkRequestSent", entry.ProviderNetworkRequestSent?.ToString());
            AppendLifecycleField(builder, "ProviderNetworkStartedAt", entry.ProviderNetworkRequestStartedAt?.ToString("O"));
            AppendLifecycleField(builder, "ProviderNetworkCompletedAt", entry.ProviderNetworkRequestCompletedAt?.ToString("O"));
            AppendLifecycleField(builder, "ProviderNetworkOutcome", entry.ProviderNetworkRequestOutcome?.ToString());
            AppendLifecycleField(
                builder,
                "ProviderNetworkHttpStatus",
                entry.ProviderNetworkHttpStatusCode?.ToString(CultureInfo.InvariantCulture));

            AppendLifecycleField(builder, "OverlayTextItems", entry.OverlayTextItemCount?.ToString());
            AppendLifecycleField(builder, "OverlayMaskItems", entry.OverlayMaskItemCount?.ToString());
            if (entry.FailureStage is { } failureStage)
            {
                AppendLifecycleField(builder, "FailureStage", failureStage.ToString());
                AppendLifecycleField(builder, "FailureExceptionType", entry.FailureExceptionType);
                AppendLifecycleQuotedField(builder, "FailureExceptionMessage", entry.FailureExceptionMessage);
                AppendLifecycleField(builder, "FailureRootCauseType", entry.FailureRootCauseType);
                AppendLifecycleQuotedField(builder, "FailureRootCauseMessage", entry.FailureRootCauseMessage);
                AppendLifecycleField(builder, "FailureProvider", entry.FailureProviderId);
                AppendLifecycleField(builder, "FailureProviderKind", entry.FailureProviderKind?.ToString());
                AppendLifecycleField(
                    builder,
                    "FailureHttpStatus",
                    entry.FailureProviderHttpStatusCode?.ToString(CultureInfo.InvariantCulture));
                AppendLifecycleField(builder, "FailureProviderPaused", entry.FailureProviderPaused?.ToString());
                AppendLifecycleField(
                    builder,
                    "FailureProviderRetryAfterMs",
                    entry.FailureProviderRetryAfter?.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture));
                AppendLifecycleField(
                    builder,
                    "FailureProviderNextRetryAt",
                    entry.FailureProviderNextRetryAt?.ToString("O"));
                AppendLifecycleField(
                    builder,
                    "FailureProviderConsecutiveFailures",
                    entry.FailureProviderConsecutiveFailureCount?.ToString(CultureInfo.InvariantCulture));
            }

            if (entry.CancellationReason != LiveCandidateCancellationReason.None)
            {
                AppendLifecycleField(builder, "CancellationReason", entry.CancellationReason.ToString());
            }

            builder.AppendLine();
        }
    }

    private void AppendLiveTranslatorProviderFailureDebugInfo(
        StringBuilder builder,
        DateTimeOffset observedAt)
    {
        builder.AppendLine();
        builder.AppendLine("Live translator provider failure");
        var latestFailure = latestCandidateLifecycleEvents.LastOrDefault(entry =>
            entry.FailureProviderKind.HasValue
            && !string.IsNullOrWhiteSpace(entry.FailureProviderId));
        if (latestFailure is null)
        {
            builder.AppendLine("  none observed");
            return;
        }

        var recoveredAfterNetworkSuccess = latestCandidateLifecycleEvents.Any(entry =>
            entry.Sequence > latestFailure.Sequence
            && entry.Kind == LiveCandidateLifecycleEventKind.CandidateWorkCompleted
            && entry.ProviderRequestCompletedAt.HasValue
            && string.Equals(
                entry.TranslationProviderId,
                latestFailure.FailureProviderId,
                StringComparison.OrdinalIgnoreCase));
        var stateAtReport = recoveredAfterNetworkSuccess
            ? "RecoveredAfterNetworkSuccess"
            : latestFailure.FailureProviderPaused == true
                && latestFailure.FailureProviderNextRetryAt is { } nextRetryAt
                && nextRetryAt > observedAt
                    ? "Paused"
                    : "RetryAllowed";

        AppendDebugLine(builder, "Provider", latestFailure.FailureProviderId!);
        AppendDebugLine(builder, "ObservedAt", latestFailure.OccurredAt.ToString("O"));
        AppendDebugLine(builder, "FailureKind", latestFailure.FailureProviderKind!.Value.ToString());
        AppendDebugLine(
            builder,
            "HttpStatus",
            latestFailure.FailureProviderHttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "(none)");
        AppendDebugLine(
            builder,
            "PausedAtFailure",
            latestFailure.FailureProviderPaused?.ToString() ?? "(none)");
        AppendDebugLine(
            builder,
            "RetryAfterMs",
            latestFailure.FailureProviderRetryAfter?.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)
                ?? "(none)");
        AppendDebugLine(
            builder,
            "NextRetryAt",
            latestFailure.FailureProviderNextRetryAt?.ToString("O") ?? "(none)");
        AppendDebugLine(
            builder,
            "ConsecutiveFailures",
            latestFailure.FailureProviderConsecutiveFailureCount?.ToString(CultureInfo.InvariantCulture)
                ?? "(none)");
        AppendDebugLine(builder, "StateAtReport", stateAtReport);
    }

    private static string? FormatDiagnosticDouble(double? value)
    {
        return value?.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void AppendLifecycleField(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append($" {label}={value}");
        }
    }

    private static void AppendLifecycleQuotedField(StringBuilder builder, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        builder.Append($" {label}=\"{escaped}\"");
    }

    private static void AppendLifecycleTextDiagnostics(
        StringBuilder builder,
        string countLabel,
        string textLabel,
        int count,
        IReadOnlyList<string> values)
    {
        if (count == 0)
        {
            return;
        }

        AppendLifecycleField(builder, countLabel, count.ToString());
        if (values.Count < count)
        {
            AppendLifecycleField(builder, $"{countLabel}Retained", values.Count.ToString());
        }

        AppendLifecycleQuotedField(builder, textLabel, string.Join(" || ", values));
    }

    private static string FormatBounds(BoundingBox bounds)
    {
        return $"X{bounds.X},Y{bounds.Y},W{bounds.Width},H{bounds.Height}";
    }

    private void AppendOverlaySnapshotDebugInfo(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("Overlay snapshot");

        var snapshot = overlayService.CurrentSnapshot;
        if (snapshot is null)
        {
            builder.AppendLine("  none");
            return;
        }

        builder.AppendLine($"  ShownAt: {snapshot.ShownAt:O}");
        builder.AppendLine($"  TextItems: {snapshot.TextItems.Count}");
        for (var index = 0; index < snapshot.TextItems.Count; index++)
        {
            var item = snapshot.TextItems[index];
            builder.AppendLine(
                $"  Text[{index + 1}] X {item.X} Y {item.Y} W {item.Width} H {item.Height} Font={item.TextStyle.FontFamily} {item.TextStyle.FontSize:0.##} Bold={item.TextStyle.IsBold} Italic={item.TextStyle.IsItalic} Layout={item.TextStyle.LayoutMode} Text={SanitizeDebugText(item.Text)}");
        }

        builder.AppendLine($"  MaskItems: {snapshot.MaskItems.Count}");
        for (var index = 0; index < snapshot.MaskItems.Count; index++)
        {
            var item = snapshot.MaskItems[index];
            builder.AppendLine(
                $"  Mask[{index + 1}] X {item.X} Y {item.Y} W {item.Width} H {item.Height} Mode={item.Mode} Color={item.Color} Opacity={item.Opacity:0.##}");
        }

        builder.AppendLine($"  DebugItems: {snapshot.DebugItems.Count}");
        for (var index = 0; index < snapshot.DebugItems.Count; index++)
        {
            var item = snapshot.DebugItems[index];
            builder.AppendLine(
                $"  Debug[{index + 1}] X {item.X} Y {item.Y} W {item.Width} H {item.Height} Source={SanitizeDebugText(item.SourceText)} Translated={SanitizeDebugText(item.TranslatedText)}");
        }

        builder.AppendLine($"  DebugMetricLines: {snapshot.DebugMetricLines.Count}");
        foreach (var line in snapshot.DebugMetricLines)
        {
            builder.AppendLine($"  Metric: {SanitizeDebugText(line)}");
        }
    }

    private static void AppendDebugLine(StringBuilder builder, string label, string value)
    {
        builder.AppendLine($"  {label}: {SanitizeDebugText(value)}");
    }

    private static string SanitizeDebugText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "(empty)"
            : value.Trim()
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
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
        ((AsyncRelayCommand)CollectDebugInfoCommand).RaiseCanExecuteChanged();
        ((RelayCommand)OpenLiveDiagnosticsFolderCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)CheckOcrLanguagePackCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)InstallOcrLanguagePackCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)CheckOcrLanguagePackChecklistCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)InstallTesseractLanguagePackChecklistCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)ShowWindowsOcrLanguagePackHelpCommand).RaiseCanExecuteChanged();
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

    private static IReadOnlyList<string> LoadInstalledFontFamilies()
    {
        try
        {
            var fonts = Fonts.SystemFontFamilies
                .Select(fontFamily => fontFamily.Source)
                .Where(fontFamily => !string.IsNullOrWhiteSpace(fontFamily))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return fonts.Length == 0
                ? new[] { OcrZoneTextStyle.DefaultFontFamily }
                : fonts;
        }
        catch
        {
            return new[] { OcrZoneTextStyle.DefaultFontFamily };
        }
    }

    private sealed record WelcomeTourStepDefinition(
        string Title,
        string Body,
        string Guidance,
        int WorkspaceTabIndex);

    private sealed class UnavailableScreenRegionPickerService : IScreenRegionPickerService
    {
        public ScreenRegionSelectionResult? PickRegion()
        {
            return null;
        }
    }
}
