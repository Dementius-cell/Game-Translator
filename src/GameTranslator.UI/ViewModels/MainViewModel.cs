using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
using GameTranslator.Application.Abstractions;
using GameTranslator.Application.Profiles;
using GameTranslator.Domain.Profiles;
using GameTranslator.UI.Commands;

namespace GameTranslator.UI.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string SelectedProfileSettingKey = "profiles.selectedId";
    private static readonly Regex HexColorPattern = new("^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);

    private readonly ProfileService profileService;
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
    private string statusMessage = "Loading profiles...";
    private bool isBusy;
    private bool isLoaded;

    public MainViewModel(
        ProfileService profileService,
        ISettingsService settings,
        IApplicationLogger logger)
    {
        this.profileService = profileService;
        this.settings = settings;
        this.logger = logger;
        pendingSelectedProfileId = settings.GetValue<string>(SelectedProfileSettingKey);

        Profiles = new ObservableCollection<GameProfile>();
        OcrZones = new ObservableCollection<OcrZoneEditorViewModel>();
        ValidationErrors = new ObservableCollection<string>();
        OverlayMaskModes = Enum.GetValues<OverlayMaskMode>();
        BeginCreateProfileCommand = new RelayCommand(BeginCreateProfile, () => !IsBusy);
        RefreshProfilesCommand = new AsyncRelayCommand(RefreshProfilesAsync, () => !IsBusy);
        SaveProfileCommand = new AsyncRelayCommand(SaveAsync, CanSaveProfile);
        CloneSelectedProfileCommand = new AsyncRelayCommand(CloneSelectedProfileAsync, CanCloneSelectedProfile);
        DeleteSelectedProfileCommand = new AsyncRelayCommand(DeleteSelectedProfileAsync, CanDeleteSelectedProfile);
        ResetEditorCommand = new RelayCommand(ResetEditor, () => !IsBusy);
        AddZoneCommand = new RelayCommand(AddZone, () => !IsBusy);
        RemoveSelectedZoneCommand = new RelayCommand(RemoveSelectedZone, CanRemoveSelectedZone);

        BeginCreateProfile();
        StatusMessage = "Ready to manage game profiles.";
    }

    public string ApplicationName => "Game Translator";

    public string CurrentStage => "Sprint 2";

    public ObservableCollection<GameProfile> Profiles { get; }

    public ObservableCollection<OcrZoneEditorViewModel> OcrZones { get; }

    public ObservableCollection<string> ValidationErrors { get; }

    public IReadOnlyList<OverlayMaskMode> OverlayMaskModes { get; }

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

            OnPropertyChanged(nameof(HasSelectedProfile));
            OnPropertyChanged(nameof(ActiveProfileName));
            OnPropertyChanged(nameof(EditorTitle));
            OnPropertyChanged(nameof(ProfileSummary));
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
                ValidateEditor();
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
                ValidateEditor();
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
                ValidateEditor();
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
                ValidateEditor();
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
                ValidateEditor();
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
                ValidateEditor();
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
                ValidateEditor();
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

    public bool HasValidationErrors => ValidationErrors.Count != 0;

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

            return $"schema {schemaVersion} | zones {OcrZones.Count} | overlay {OverlayMaskMode}";
        }
    }

    public string ZoneSummary => $"{OcrZones.Count} zone(s)";

    public ICommand BeginCreateProfileCommand { get; }

    public ICommand RefreshProfilesCommand { get; }

    public ICommand SaveProfileCommand { get; }

    public ICommand CloneSelectedProfileCommand { get; }

    public ICommand DeleteSelectedProfileCommand { get; }

    public ICommand ResetEditorCommand { get; }

    public ICommand AddZoneCommand { get; }

    public ICommand RemoveSelectedZoneCommand { get; }

    public async Task LoadAsync()
    {
        if (isLoaded)
        {
            return;
        }

        isLoaded = true;
        await RefreshProfilesAsync();
    }

    public void BeginCreateProfile()
    {
        selectedProfile = null;
        editingProfileId = null;

        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(HasSelectedProfile));
        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(EditorTitle));

        LoadDraftValues();
        StatusMessage = "Drafting a new profile.";
        ValidateEditor();
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
        ValidateEditor();
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
        ValidateEditor();
    }

    public void AddZone()
    {
        var zone = OcrZoneEditorViewModel.CreateDefault(OcrZones.Count + 1);
        AttachZone(zone);
        OcrZones.Add(zone);
        SelectedZone = zone;
        OnPropertyChanged(nameof(ZoneSummary));
        OnPropertyChanged(nameof(ProfileSummary));
        ValidateEditor();
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
        OnPropertyChanged(nameof(ZoneSummary));
        OnPropertyChanged(nameof(ProfileSummary));
        ValidateEditor();
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

    private async Task RunProfileOperationAsync(string activityMessage, Func<Task> action)
    {
        try
        {
            IsBusy = true;
            StatusMessage = activityMessage;
            await action();
        }
        catch (ProfileValidationException exception)
        {
            var message = string.Join(" ", exception.Errors.Select(error => error.Message));
            logger.Warning(message);
            StatusMessage = message;
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
        OnPropertyChanged(nameof(ProfileSummary));
        OnPropertyChanged(nameof(ZoneSummary));
        ValidateEditor();
    }

    private void LoadDraftValues()
    {
        ProfileName = string.Empty;
        ProfileDescription = string.Empty;
        TranslatorProvider = string.Empty;
        SourceLanguage = string.Empty;
        TargetLanguage = string.Empty;
        OverlayMaskMode = OverlaySettings.Default.MaskMode;
        OverlayMaskColor = OverlaySettings.Default.MaskColor;
        OverlayOpacity = OverlaySettings.Default.Opacity;
        OverlayPadding = OverlaySettings.Default.Padding;
        ReplaceZones(Array.Empty<OcrZoneEditorViewModel>());
        OnPropertyChanged(nameof(ProfileSummary));
        OnPropertyChanged(nameof(ZoneSummary));
        ValidateEditor();
    }

    private bool CanSaveProfile()
    {
        return !IsBusy && !HasValidationErrors;
    }

    private bool CanCloneSelectedProfile()
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
        OnPropertyChanged(nameof(ProfileSummary));
        ValidateEditor();
    }

    private void ValidateEditor()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            errors.Add("Profile name is required.");
        }

        if (string.IsNullOrWhiteSpace(TranslatorProvider))
        {
            errors.Add("Translator provider is required.");
        }

        if (string.IsNullOrWhiteSpace(SourceLanguage))
        {
            errors.Add("Source language is required.");
        }

        if (string.IsNullOrWhiteSpace(TargetLanguage))
        {
            errors.Add("Target language is required.");
        }

        if (!HexColorPattern.IsMatch(OverlayMaskColor.Trim()))
        {
            errors.Add("Overlay mask color must use #RRGGBB or #AARRGGBB format.");
        }

        if (OverlayOpacity is < 0 or > 1)
        {
            errors.Add("Overlay opacity must be between 0 and 1.");
        }

        if (OverlayPadding < 0)
        {
            errors.Add("Overlay padding must be zero or greater.");
        }

        for (var index = 0; index < OcrZones.Count; index++)
        {
            var zone = OcrZones[index];
            var zoneLabel = string.IsNullOrWhiteSpace(zone.Name) ? $"Zone {index + 1}" : zone.Name.Trim();

            if (string.IsNullOrWhiteSpace(zone.Name))
            {
                errors.Add($"{zoneLabel}: name is required.");
            }

            if (zone.AbsoluteWidth <= 0 || zone.AbsoluteHeight <= 0)
            {
                errors.Add($"{zoneLabel}: absolute width and height must be positive.");
            }

            if (zone.RelativeWidth <= 0 || zone.RelativeHeight <= 0)
            {
                errors.Add($"{zoneLabel}: relative width and height must be positive.");
            }

            if (zone.RelativeX < 0 || zone.RelativeY < 0 || zone.RelativeX >= 1 || zone.RelativeY >= 1)
            {
                errors.Add($"{zoneLabel}: relative X and Y must stay within 0..1.");
            }

            if (zone.RelativeX + zone.RelativeWidth > 1 || zone.RelativeY + zone.RelativeHeight > 1)
            {
                errors.Add($"{zoneLabel}: relative bounds must fit within 0..1.");
            }
        }

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

                if (firstBounds.Intersects(secondBounds))
                {
                    errors.Add($"OCR zones '{firstZone.DisplayName}' and '{secondZone.DisplayName}' overlap.");
                }
            }
        }

        ReplaceValidationErrors(errors);
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

    private void NotifyCommandStateChanged()
    {
        ((RelayCommand)BeginCreateProfileCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RefreshProfilesCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)SaveProfileCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)CloneSelectedProfileCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)DeleteSelectedProfileCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ResetEditorCommand).RaiseCanExecuteChanged();
        ((RelayCommand)AddZoneCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RemoveSelectedZoneCommand).RaiseCanExecuteChanged();
    }
}
