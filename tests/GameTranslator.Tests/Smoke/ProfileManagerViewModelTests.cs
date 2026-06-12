using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using GameTranslator.Application.Abstractions;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Profiles;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Smoke;

public sealed class ProfileManagerViewModelTests
{
    [Fact]
    public async Task LoadAsync_RestoresSelectedProfileFromSettings()
    {
        var repository = new InMemoryProfileRepository();
        var alpha = CreateProfile("Alpha", "Google", "en", "ru");
        var beta = CreateProfile("Beta", "Azure", "ja", "en");
        await repository.SaveAsync(alpha);
        await repository.SaveAsync(beta);

        var settings = new TestSettingsService();
        settings.SetValue("profiles.selectedId", beta.Id);

        var viewModel = CreateMainViewModel(repository, settings);
        await InvokeTaskMethodAsync(viewModel, "LoadAsync");

        var selectedProfile = GetPropertyValue(viewModel, "SelectedProfile");

        Assert.NotNull(selectedProfile);
        Assert.Equal(beta.Id, GetPropertyValue(selectedProfile!, "Id"));
        Assert.Equal("Beta", GetPropertyValue(viewModel, "ActiveProfileName"));
    }

    [Fact]
    public async Task SaveAsync_WhenDraftIsValid_CreatesProfileAndPersistsSelection()
    {
        var repository = new InMemoryProfileRepository();
        var settings = new TestSettingsService();
        var viewModel = CreateMainViewModel(repository, settings);

        InvokeMethod(viewModel, "BeginCreateProfile");
        SetPropertyValue(viewModel, "ProfileName", "Cyberpunk 2077");
        SetPropertyValue(viewModel, "ProfileDescription", "Main subtitles profile.");
        SetPropertyValue(viewModel, "TranslatorProvider", "Google");
        SetPropertyValue(viewModel, "SourceLanguage", "ja");
        SetPropertyValue(viewModel, "TargetLanguage", "en");

        await InvokeTaskMethodAsync(viewModel, "SaveAsync");

        var storedProfiles = await repository.ListAsync();

        Assert.Single(storedProfiles);
        Assert.Equal("Cyberpunk 2077", storedProfiles[0].Name);
        Assert.Equal("Google", storedProfiles[0].TranslatorSettings.Provider);
        Assert.Equal(storedProfiles[0].Id, settings.GetValue<string>("profiles.selectedId"));
        Assert.Equal(storedProfiles[0].Id, GetPropertyValue(GetPropertyValue(viewModel, "SelectedProfile")!, "Id"));
    }

    [Fact]
    public void BeginCreateProfile_RestoresDraftTranslatorAndOverlayDefaultsFromSettings()
    {
        var repository = new InMemoryProfileRepository();
        var settings = new TestSettingsService();
        settings.SetValue("shell.draft.profile.name", "Draft profile");
        settings.SetValue("shell.draft.profile.description", "Draft description");
        settings.SetValue("shell.draft.translator.provider", "Azure");
        settings.SetValue("shell.draft.translator.sourceLanguage", "ja");
        settings.SetValue("shell.draft.translator.targetLanguage", "en");
        settings.SetValue("shell.draft.overlay.maskMode", OverlayMaskMode.Darken);
        settings.SetValue("shell.draft.overlay.maskColor", "#202020");
        settings.SetValue("shell.draft.overlay.opacity", 0.65);
        settings.SetValue("shell.draft.overlay.padding", 12d);
        settings.SetValue("shell.draft.ocrZones", new[]
        {
            new OcrZone
            {
                Id = "zone-a",
                Name = "Draft zone",
                AbsoluteBounds = new AbsoluteRectangle(10, 20, 300, 80),
                RelativeBounds = new RelativeRectangle(0.1, 0.2, 0.3, 0.1),
            },
        });
        settings.SetValue("shell.draft.selectedZoneId", "zone-a");

        var viewModel = CreateMainViewModel(repository, settings);
        InvokeMethod(viewModel, "BeginCreateProfile");

        Assert.Equal("Draft profile", GetPropertyValue(viewModel, "ProfileName"));
        Assert.Equal("Draft description", GetPropertyValue(viewModel, "ProfileDescription"));
        Assert.Equal("Azure", GetPropertyValue(viewModel, "TranslatorProvider"));
        Assert.Equal("ja", GetPropertyValue(viewModel, "SourceLanguage"));
        Assert.Equal("en", GetPropertyValue(viewModel, "TargetLanguage"));
        Assert.Equal(OverlayMaskMode.Darken, GetPropertyValue(viewModel, "OverlayMaskMode"));
        Assert.Equal("#202020", GetPropertyValue(viewModel, "OverlayMaskColor"));
        Assert.Equal(0.65, GetPropertyValue(viewModel, "OverlayOpacity"));
        Assert.Equal(12d, GetPropertyValue(viewModel, "OverlayPadding"));
        var selectedZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("Selected draft zone was not restored.");
        Assert.Equal("zone-a", GetPropertyValue(selectedZone, "Id"));
        Assert.Equal("X 10  Y 20  W 300  H 80", GetPropertyValue(selectedZone, "AbsoluteBoundsSummary"));
        Assert.Equal(3d, GetPropertyValue(selectedZone, "RelativeAreaPercent"));
    }

    [Fact]
    public void DraftEditor_WhenTranslatorAndOverlayChange_PersistsShellDefaults()
    {
        var repository = new InMemoryProfileRepository();
        var settings = new TestSettingsService();
        var viewModel = CreateMainViewModel(repository, settings);

        InvokeMethod(viewModel, "BeginCreateProfile");
        SetPropertyValue(viewModel, "ProfileName", "Draft shell");
        SetPropertyValue(viewModel, "ProfileDescription", "Persistent draft");
        SetPropertyValue(viewModel, "TranslatorProvider", "Yandex");
        SetPropertyValue(viewModel, "SourceLanguage", "ru");
        SetPropertyValue(viewModel, "TargetLanguage", "en");
        SetPropertyValue(viewModel, "OverlayMaskMode", OverlayMaskMode.Darken);
        SetPropertyValue(viewModel, "OverlayMaskColor", "#303030");
        SetPropertyValue(viewModel, "OverlayOpacity", 0.55);
        SetPropertyValue(viewModel, "OverlayPadding", 10d);
        InvokeMethod(viewModel, "AddZone");

        Assert.Equal("Draft shell", settings.GetValue<string>("shell.draft.profile.name"));
        Assert.Equal("Persistent draft", settings.GetValue<string>("shell.draft.profile.description"));
        Assert.Equal("Yandex", settings.GetValue<string>("shell.draft.translator.provider"));
        Assert.Equal("ru", settings.GetValue<string>("shell.draft.translator.sourceLanguage"));
        Assert.Equal("en", settings.GetValue<string>("shell.draft.translator.targetLanguage"));
        Assert.Equal(OverlayMaskMode.Darken, settings.GetValue<OverlayMaskMode>("shell.draft.overlay.maskMode"));
        Assert.Equal("#303030", settings.GetValue<string>("shell.draft.overlay.maskColor"));
        Assert.Equal(0.55, settings.GetValue<double>("shell.draft.overlay.opacity"));
        Assert.Equal(10d, settings.GetValue<double>("shell.draft.overlay.padding"));
        Assert.Single(settings.GetValue<OcrZone[]>("shell.draft.ocrZones") ?? Array.Empty<OcrZone>());
        Assert.Equal(
            GetPropertyValue(GetPropertyValue(viewModel, "SelectedZone")!, "Id"),
            settings.GetValue<string>("shell.draft.selectedZoneId"));
    }

    [Fact]
    public void InvalidZoneField_ExposesFieldLevelValidationErrors()
    {
        var repository = new InMemoryProfileRepository();
        var viewModel = CreateMainViewModel(repository, new TestSettingsService());

        InvokeMethod(viewModel, "BeginCreateProfile");
        SetPropertyValue(viewModel, "ProfileName", "Zone errors");
        SetPropertyValue(viewModel, "TranslatorProvider", "Google");
        SetPropertyValue(viewModel, "SourceLanguage", "ja");
        SetPropertyValue(viewModel, "TargetLanguage", "en");
        InvokeMethod(viewModel, "AddZone");

        var selectedZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("Selected zone was not created.");
        SetPropertyValue(selectedZone, "AbsoluteWidth", 0);

        var getErrors = selectedZone.GetType().GetMethod("GetErrors")
            ?? throw new InvalidOperationException("GetErrors method was not found.");
        var errors = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            getErrors.Invoke(selectedZone, new object?[] { "AbsoluteWidth" }));

        Assert.Contains(
            errors.Cast<object>().Select(error => error.ToString()),
            error => string.Equals(error, "Absolute width and height must be positive.", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateAndReorderZone_PersistDraftZoneOrderAndSelection()
    {
        var repository = new InMemoryProfileRepository();
        var settings = new TestSettingsService();
        var viewModel = CreateMainViewModel(repository, settings);

        InvokeMethod(viewModel, "BeginCreateProfile");
        SetPropertyValue(viewModel, "ProfileName", "Zone manager");
        SetPropertyValue(viewModel, "TranslatorProvider", "Google");
        SetPropertyValue(viewModel, "SourceLanguage", "ja");
        SetPropertyValue(viewModel, "TargetLanguage", "en");
        InvokeMethod(viewModel, "AddZone");
        var firstZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("First zone was not created.");
        SetPropertyValue(firstZone, "Name", "Primary");
        InvokeMethod(viewModel, "DuplicateSelectedZone");

        var duplicatedZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("Duplicated zone was not selected.");
        Assert.Equal("Primary Copy", GetPropertyValue(duplicatedZone, "Name"));

        InvokeMethod(viewModel, "MoveSelectedZoneUp");

        var persistedZones = settings.GetValue<OcrZone[]>("shell.draft.ocrZones")
            ?? throw new InvalidOperationException("Persisted zones were not found.");

        Assert.Equal(2, persistedZones.Length);
        Assert.Equal("Primary Copy", persistedZones[0].Name);
        Assert.Equal("Primary", persistedZones[1].Name);
        Assert.Equal(GetPropertyValue(duplicatedZone, "Id"), settings.GetValue<string>("shell.draft.selectedZoneId"));
    }

    [Fact]
    public async Task SaveAsync_PersistsOverlayAndZoneEdits()
    {
        var repository = new InMemoryProfileRepository();
        var viewModel = CreateMainViewModel(repository, new TestSettingsService());

        InvokeMethod(viewModel, "BeginCreateProfile");
        SetPropertyValue(viewModel, "ProfileName", "NieR");
        SetPropertyValue(viewModel, "TranslatorProvider", "Google");
        SetPropertyValue(viewModel, "SourceLanguage", "ja");
        SetPropertyValue(viewModel, "TargetLanguage", "en");
        SetPropertyValue(viewModel, "OverlayMaskMode", OverlayMaskMode.Darken);
        SetPropertyValue(viewModel, "OverlayMaskColor", "#101010");
        SetPropertyValue(viewModel, "OverlayOpacity", 0.75);
        SetPropertyValue(viewModel, "OverlayPadding", 8d);
        InvokeMethod(viewModel, "AddZone");

        var selectedZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("Selected zone was not created.");
        SetPropertyValue(selectedZone, "Name", "Subtitles");
        SetPropertyValue(selectedZone, "AbsoluteX", 10);
        SetPropertyValue(selectedZone, "AbsoluteY", 20);
        SetPropertyValue(selectedZone, "AbsoluteWidth", 300);
        SetPropertyValue(selectedZone, "AbsoluteHeight", 80);
        SetPropertyValue(selectedZone, "RelativeX", 0.1);
        SetPropertyValue(selectedZone, "RelativeY", 0.2);
        SetPropertyValue(selectedZone, "RelativeWidth", 0.4);
        SetPropertyValue(selectedZone, "RelativeHeight", 0.1);

        await InvokeTaskMethodAsync(viewModel, "SaveAsync");

        var storedProfile = Assert.Single(await repository.ListAsync());
        Assert.Equal(OverlayMaskMode.Darken, storedProfile.OverlaySettings.MaskMode);
        Assert.Equal("#101010", storedProfile.OverlaySettings.MaskColor);
        Assert.Equal(0.75, storedProfile.OverlaySettings.Opacity);
        Assert.Equal(8d, storedProfile.OverlaySettings.Padding);
        Assert.Single(storedProfile.OcrZones);
        Assert.Equal("Subtitles", storedProfile.OcrZones[0].Name);
        Assert.Equal(new AbsoluteRectangle(10, 20, 300, 80), storedProfile.OcrZones[0].AbsoluteBounds);
    }

    [Fact]
    public async Task SaveAsync_WithInvalidOverlayAndTranslatorInputs_DoesNotPersistProfile()
    {
        var repository = new InMemoryProfileRepository();
        var viewModel = CreateMainViewModel(repository, new TestSettingsService());

        InvokeMethod(viewModel, "BeginCreateProfile");
        SetPropertyValue(viewModel, "ProfileName", "Broken profile");
        SetPropertyValue(viewModel, "TranslatorProvider", string.Empty);
        SetPropertyValue(viewModel, "SourceLanguage", string.Empty);
        SetPropertyValue(viewModel, "TargetLanguage", string.Empty);
        SetPropertyValue(viewModel, "OverlayMaskColor", "red");
        SetPropertyValue(viewModel, "OverlayOpacity", 1.5);
        SetPropertyValue(viewModel, "OverlayPadding", -5d);

        await InvokeTaskMethodAsync(viewModel, "SaveAsync");

        Assert.Empty(await repository.ListAsync());
        Assert.True((bool)(GetPropertyValue(viewModel, "HasValidationErrors") ?? false));

        var validationErrors = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            GetPropertyValue(viewModel, "ValidationErrors"));
        Assert.Contains(
            validationErrors.Cast<object>().Select(error => error.ToString()),
            error => string.Equals(error, "Overlay opacity must be between 0 and 1.", StringComparison.Ordinal));
    }

    [Fact]
    public void AddZone_WithOverlappingBounds_ProducesValidationError()
    {
        var repository = new InMemoryProfileRepository();
        var viewModel = CreateMainViewModel(repository, new TestSettingsService());

        InvokeMethod(viewModel, "BeginCreateProfile");
        SetPropertyValue(viewModel, "ProfileName", "Overlap test");
        SetPropertyValue(viewModel, "TranslatorProvider", "Google");
        SetPropertyValue(viewModel, "SourceLanguage", "ja");
        SetPropertyValue(viewModel, "TargetLanguage", "en");
        InvokeMethod(viewModel, "AddZone");
        SetPropertyValue(GetPropertyValue(viewModel, "SelectedZone")!, "Name", "Zone A");
        SetPropertyValue(GetPropertyValue(viewModel, "SelectedZone")!, "AbsoluteWidth", 100);
        SetPropertyValue(GetPropertyValue(viewModel, "SelectedZone")!, "AbsoluteHeight", 40);
        InvokeMethod(viewModel, "AddZone");

        var secondZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("Second zone was not selected.");
        SetPropertyValue(secondZone, "Name", "Zone B");
        SetPropertyValue(secondZone, "AbsoluteX", 20);
        SetPropertyValue(secondZone, "AbsoluteY", 10);
        SetPropertyValue(secondZone, "AbsoluteWidth", 120);
        SetPropertyValue(secondZone, "AbsoluteHeight", 30);

        Assert.True((bool)(GetPropertyValue(viewModel, "HasValidationErrors") ?? false));

        var validationErrors = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            GetPropertyValue(viewModel, "ValidationErrors"));
        Assert.Contains(
            validationErrors.Cast<object>().Select(error => error.ToString()),
            error => string.Equals(error, "OCR zones 'Zone A' and 'Zone B' overlap.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InteractiveZoneSelection_CreatesZoneAndPersistsCoordinatesOnSave()
    {
        var repository = new InMemoryProfileRepository();
        var viewModel = CreateMainViewModel(repository, new TestSettingsService());

        InvokeMethod(viewModel, "BeginCreateProfile");
        SetPropertyValue(viewModel, "ProfileName", "Surface zone");
        SetPropertyValue(viewModel, "TranslatorProvider", "Google");
        SetPropertyValue(viewModel, "SourceLanguage", "ja");
        SetPropertyValue(viewModel, "TargetLanguage", "en");

        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 10d, 20d);
        InvokeMethodWithArguments(viewModel, "UpdateZoneSelection", 110d, 70d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 110d, 70d);
        await InvokeTaskMethodAsync(viewModel, "SaveAsync");

        var storedProfile = Assert.Single(await repository.ListAsync());
        var storedZone = Assert.Single(storedProfile.OcrZones);

        Assert.Equal(new AbsoluteRectangle(30, 60, 300, 150), storedZone.AbsoluteBounds);
        Assert.Equal(new RelativeRectangle(0.0156, 0.0556, 0.1563, 0.1389), storedZone.RelativeBounds);
    }

    [Fact]
    public async Task InteractiveZoneResize_UpdatesSelectedZoneBounds()
    {
        var repository = new InMemoryProfileRepository();
        var viewModel = CreateMainViewModel(repository, new TestSettingsService());

        InvokeMethod(viewModel, "BeginCreateProfile");
        SetPropertyValue(viewModel, "ProfileName", "Resize zone");
        SetPropertyValue(viewModel, "TranslatorProvider", "Google");
        SetPropertyValue(viewModel, "SourceLanguage", "ja");
        SetPropertyValue(viewModel, "TargetLanguage", "en");

        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 10d, 20d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 110d, 70d);
        InvokeMethod(viewModel, "StartSelectedZoneResize");
        InvokeMethodWithArguments(viewModel, "UpdateSelectedZoneResize", 150d, 100d);
        InvokeMethodWithArguments(viewModel, "CompleteSelectedZoneResize", 150d, 100d);
        await InvokeTaskMethodAsync(viewModel, "SaveAsync");

        var storedProfile = Assert.Single(await repository.ListAsync());
        var storedZone = Assert.Single(storedProfile.OcrZones);

        Assert.Equal(new AbsoluteRectangle(30, 60, 420, 240), storedZone.AbsoluteBounds);
    }

    [Fact]
    public async Task InteractiveZoneSelection_WhenDraggedInReverse_CreatesNormalizedBounds()
    {
        var repository = new InMemoryProfileRepository();
        var viewModel = CreateMainViewModel(repository, new TestSettingsService());

        ConfigureValidDraftProfile(viewModel, "Reverse drag zone");

        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 110d, 70d);
        InvokeMethodWithArguments(viewModel, "UpdateZoneSelection", 10d, 20d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 10d, 20d);
        await InvokeTaskMethodAsync(viewModel, "SaveAsync");

        var storedProfile = Assert.Single(await repository.ListAsync());
        var storedZone = Assert.Single(storedProfile.OcrZones);

        Assert.Equal(new AbsoluteRectangle(30, 60, 300, 150), storedZone.AbsoluteBounds);
        Assert.Equal(new RelativeRectangle(0.0156, 0.0556, 0.1563, 0.1389), storedZone.RelativeBounds);
    }

    [Fact]
    public async Task InteractiveZoneSelection_WhenDraggedOutsideSurface_ClampsToReferenceBounds()
    {
        var repository = new InMemoryProfileRepository();
        var viewModel = CreateMainViewModel(repository, new TestSettingsService());

        ConfigureValidDraftProfile(viewModel, "Clamped create zone");

        InvokeMethodWithArguments(viewModel, "StartZoneSelection", -20d, -30d);
        InvokeMethodWithArguments(viewModel, "UpdateZoneSelection", 700d, 420d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 700d, 420d);
        await InvokeTaskMethodAsync(viewModel, "SaveAsync");

        var storedProfile = Assert.Single(await repository.ListAsync());
        var storedZone = Assert.Single(storedProfile.OcrZones);

        Assert.Equal(new AbsoluteRectangle(0, 0, 1920, 1080), storedZone.AbsoluteBounds);
        Assert.Equal(new RelativeRectangle(0, 0, 1, 1), storedZone.RelativeBounds);
    }

    [Fact]
    public async Task InteractiveZoneResize_WhenDraggedOutsideSurface_ClampsToReferenceBounds()
    {
        var repository = new InMemoryProfileRepository();
        var viewModel = CreateMainViewModel(repository, new TestSettingsService());

        ConfigureValidDraftProfile(viewModel, "Clamped resize zone");

        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 100d, 100d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 200d, 200d);
        InvokeMethod(viewModel, "StartSelectedZoneResize");
        InvokeMethodWithArguments(viewModel, "UpdateSelectedZoneResize", 800d, 500d);
        InvokeMethodWithArguments(viewModel, "CompleteSelectedZoneResize", 800d, 500d);

        Assert.False(
            (bool)(GetPropertyValue(viewModel, "HasValidationErrors") ?? false),
            $"{GetPropertyValue(GetPropertyValue(viewModel, "SelectedZone")!, "RelativeBoundsSummary")} | {string.Join(" | ", GetValidationErrorMessages(viewModel))}");

        await InvokeTaskMethodAsync(viewModel, "SaveAsync");

        var storedProfile = Assert.Single(await repository.ListAsync());
        var storedZone = Assert.Single(storedProfile.OcrZones);

        Assert.Equal(new AbsoluteRectangle(300, 300, 1620, 780), storedZone.AbsoluteBounds);
        Assert.Equal(1920, storedZone.AbsoluteBounds.X + storedZone.AbsoluteBounds.Width);
        Assert.Equal(1080, storedZone.AbsoluteBounds.Y + storedZone.AbsoluteBounds.Height);
    }

    [Fact]
    public void InteractiveZoneSelection_WhenDragIsTooSmall_DoesNotCreateBrokenZone()
    {
        var repository = new InMemoryProfileRepository();
        var viewModel = CreateMainViewModel(repository, new TestSettingsService());

        ConfigureValidDraftProfile(viewModel, "Tiny drag zone");

        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 100d, 100d);
        InvokeMethodWithArguments(viewModel, "UpdateZoneSelection", 102d, 103d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 102d, 103d);

        var zones = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            GetPropertyValue(viewModel, "OcrZones"));

        Assert.Empty(zones.Cast<object>());
        Assert.Equal("Zone selection canceled.", GetPropertyValue(viewModel, "StatusMessage"));
    }

    [Fact]
    public async Task InteractiveZoneSelection_WhenCreatedZoneOverlaps_BlocksSave()
    {
        var repository = new InMemoryProfileRepository();
        var viewModel = CreateMainViewModel(repository, new TestSettingsService());

        ConfigureValidDraftProfile(viewModel, "Overlapping create zone");

        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 10d, 10d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 110d, 70d);
        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 50d, 30d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 150d, 90d);
        await InvokeTaskMethodAsync(viewModel, "SaveAsync");

        Assert.Empty(await repository.ListAsync());
        Assert.True((bool)(GetPropertyValue(viewModel, "HasValidationErrors") ?? false));

        var validationErrors = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            GetPropertyValue(viewModel, "ValidationErrors"));
        Assert.Contains(
            validationErrors.Cast<object>().Select(error => error.ToString()),
            error => error is not null && error.Contains("overlap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InteractiveZoneResize_WhenResizedZoneOverlaps_BlocksSave()
    {
        var repository = new InMemoryProfileRepository();
        var viewModel = CreateMainViewModel(repository, new TestSettingsService());

        ConfigureValidDraftProfile(viewModel, "Overlapping resize zone");

        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 10d, 10d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 60d, 60d);
        var firstZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("First zone was not selected.");

        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 120d, 10d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 170d, 60d);
        InvokeMethodWithArguments(viewModel, "SelectZone", GetPropertyValue(firstZone, "Id"));
        InvokeMethod(viewModel, "StartSelectedZoneResize");
        InvokeMethodWithArguments(viewModel, "UpdateSelectedZoneResize", 140d, 60d);
        InvokeMethodWithArguments(viewModel, "CompleteSelectedZoneResize", 140d, 60d);
        await InvokeTaskMethodAsync(viewModel, "SaveAsync");

        Assert.Empty(await repository.ListAsync());
        Assert.True((bool)(GetPropertyValue(viewModel, "HasValidationErrors") ?? false));

        var validationErrors = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            GetPropertyValue(viewModel, "ValidationErrors"));
        Assert.Contains(
            validationErrors.Cast<object>().Select(error => error.ToString()),
            error => error is not null && error.Contains("overlap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemoveSelectedZone_UpdatesSelectionAndDraftState()
    {
        var repository = new InMemoryProfileRepository();
        var settings = new TestSettingsService();
        var viewModel = CreateMainViewModel(repository, settings);

        ConfigureValidDraftProfile(viewModel, "Delete selected zone");

        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 10d, 10d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 60d, 60d);
        var firstZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("First zone was not selected.");
        var firstZoneId = Assert.IsType<string>(GetPropertyValue(firstZone, "Id"));

        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 120d, 10d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 170d, 60d);
        InvokeMethod(viewModel, "RemoveSelectedZone");

        var selectedZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("A remaining zone should be selected.");
        var persistedZones = settings.GetValue<OcrZone[]>("shell.draft.ocrZones")
            ?? throw new InvalidOperationException("Persisted draft zones were not found.");

        Assert.Equal(firstZoneId, GetPropertyValue(selectedZone, "Id"));
        Assert.True((bool)(GetPropertyValue(selectedZone, "IsSelected") ?? false));
        Assert.Single(persistedZones);
        Assert.Equal(firstZoneId, persistedZones[0].Id);
        Assert.Equal(firstZoneId, settings.GetValue<string>("shell.draft.selectedZoneId"));
    }

    [Fact]
    public async Task CloneAndDeleteSelectedProfileAsync_UpdateProfileCollection()
    {
        var repository = new InMemoryProfileRepository();
        var source = CreateProfile("Persona 5", "Google", "ja", "ru");
        await repository.SaveAsync(source);

        var viewModel = CreateMainViewModel(repository, new TestSettingsService());
        await InvokeTaskMethodAsync(viewModel, "LoadAsync");
        await InvokeTaskMethodAsync(viewModel, "CloneSelectedProfileAsync");

        var profilesAfterClone = await repository.ListAsync();
        Assert.Equal(2, profilesAfterClone.Count);

        await InvokeTaskMethodAsync(viewModel, "DeleteSelectedProfileAsync");

        var profilesAfterDelete = await repository.ListAsync();
        Assert.Single(profilesAfterDelete);
        Assert.Equal(source.Id, profilesAfterDelete[0].Id);
    }

    [Fact]
    public async Task ImportProfileAsync_WhenDialogReturnsPath_ImportsProfileAndSelectsIt()
    {
        var repository = new InMemoryProfileRepository();
        var dialog = new TestDialogService
        {
            OpenFilePath = "import.json",
        };
        var exchangeGateway = new TestProfileExchangeGateway
        {
            ImportedProfile = CreateProfile("Imported profile", "Azure", "ja", "en"),
        };
        var viewModel = CreateMainViewModel(repository, new TestSettingsService(), dialog, exchangeGateway);

        await InvokeTaskMethodAsync(viewModel, "ImportProfileAsync");

        var storedProfiles = await repository.ListAsync();
        var storedProfile = Assert.Single(storedProfiles);
        Assert.Equal("Imported profile", storedProfile.Name);
        Assert.Equal(storedProfile.Id, GetPropertyValue(GetPropertyValue(viewModel, "SelectedProfile")!, "Id"));
        Assert.Contains(dialog.InformationMessages, message => message.Contains("Imported profile", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportProfileAsync_WhenNameConflictsAndDialogReturnsYes_ReplacesExistingProfile()
    {
        var repository = new InMemoryProfileRepository();
        var existingProfile = CreateProfile("Shared profile", "Google", "ja", "en");
        await repository.SaveAsync(existingProfile);

        var dialog = new TestDialogService
        {
            OpenFilePath = "import.json",
            YesNoCancelChoice = DialogChoice.Yes,
        };
        var exchangeGateway = new TestProfileExchangeGateway
        {
            ImportedProfile = CreateProfile("Shared profile", "Azure", "ja", "ru"),
        };
        var viewModel = CreateMainViewModel(repository, new TestSettingsService(), dialog, exchangeGateway);
        await InvokeTaskMethodAsync(viewModel, "LoadAsync");

        await InvokeTaskMethodAsync(viewModel, "ImportProfileAsync");

        var storedProfiles = await repository.ListAsync();
        var storedProfile = Assert.Single(storedProfiles);
        Assert.Equal(existingProfile.Id, storedProfile.Id);
        Assert.Equal("Azure", storedProfile.TranslatorSettings.Provider);
        Assert.Equal("ru", storedProfile.TranslatorSettings.TargetLanguage);
    }

    [Fact]
    public async Task ImportProfileAsync_WhenNameConflictsAndDialogReturnsNo_KeepsBothProfiles()
    {
        var repository = new InMemoryProfileRepository();
        await repository.SaveAsync(CreateProfile("Shared profile", "Google", "ja", "en"));

        var dialog = new TestDialogService
        {
            OpenFilePath = "import.json",
            YesNoCancelChoice = DialogChoice.No,
        };
        var exchangeGateway = new TestProfileExchangeGateway
        {
            ImportedProfile = CreateProfile("Shared profile", "Azure", "ja", "ru"),
        };
        var viewModel = CreateMainViewModel(repository, new TestSettingsService(), dialog, exchangeGateway);
        await InvokeTaskMethodAsync(viewModel, "LoadAsync");

        await InvokeTaskMethodAsync(viewModel, "ImportProfileAsync");

        var storedProfiles = await repository.ListAsync();
        Assert.Equal(2, storedProfiles.Count);
        Assert.Contains(storedProfiles, profile => string.Equals(profile.Name, "Shared profile", StringComparison.Ordinal));
        Assert.Contains(storedProfiles, profile => string.Equals(profile.Name, "Shared profile Imported 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportProfileAsync_WhenNameConflictsAndDialogReturnsCancel_DoesNotChangeProfiles()
    {
        var repository = new InMemoryProfileRepository();
        var existingProfile = CreateProfile("Shared profile", "Google", "ja", "en");
        await repository.SaveAsync(existingProfile);

        var dialog = new TestDialogService
        {
            OpenFilePath = "import.json",
            YesNoCancelChoice = DialogChoice.Cancel,
        };
        var exchangeGateway = new TestProfileExchangeGateway
        {
            ImportedProfile = CreateProfile("Shared profile", "Azure", "ja", "ru"),
        };
        var viewModel = CreateMainViewModel(repository, new TestSettingsService(), dialog, exchangeGateway);
        await InvokeTaskMethodAsync(viewModel, "LoadAsync");

        await InvokeTaskMethodAsync(viewModel, "ImportProfileAsync");

        var storedProfiles = await repository.ListAsync();
        var storedProfile = Assert.Single(storedProfiles);
        Assert.Equal(existingProfile.Id, storedProfile.Id);
        Assert.Equal("Google", storedProfile.TranslatorSettings.Provider);
        Assert.Equal("Profile import canceled.", GetPropertyValue(viewModel, "StatusMessage"));
    }

    [Fact]
    public async Task ExportSelectedProfileAsync_WhenProfileSelected_ExportsSelectedProfile()
    {
        var repository = new InMemoryProfileRepository();
        var profile = CreateProfile("Export me", "Google", "ja", "en");
        await repository.SaveAsync(profile);

        var dialog = new TestDialogService
        {
            SaveFilePath = "export.json",
        };
        var exchangeGateway = new TestProfileExchangeGateway();
        var viewModel = CreateMainViewModel(repository, new TestSettingsService(), dialog, exchangeGateway);
        await InvokeTaskMethodAsync(viewModel, "LoadAsync");

        await InvokeTaskMethodAsync(viewModel, "ExportSelectedProfileAsync");

        Assert.NotNull(exchangeGateway.ExportedProfile);
        Assert.Equal("Export me", exchangeGateway.ExportedProfile!.Name);
        Assert.Equal("export.json", exchangeGateway.ExportedPath);
        Assert.Contains(dialog.InformationMessages, message => message.Contains("export.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshCapturePreviewAsync_WhenZoneSelected_CapturesSelectedZoneAndExposesPreview()
    {
        var repository = new InMemoryProfileRepository();
        var frameSource = new TestCaptureFrameSource();
        var viewModel = CreateMainViewModel(
            repository,
            new TestSettingsService(),
            frameSource: frameSource);
        ConfigureValidDraftProfile(viewModel, "Capture preview");
        InvokeMethod(viewModel, "AddZone");

        var selectedZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("Selected zone was not created.");
        SetPropertyValue(selectedZone, "AbsoluteX", 10);
        SetPropertyValue(selectedZone, "AbsoluteY", 20);
        SetPropertyValue(selectedZone, "AbsoluteWidth", 4);
        SetPropertyValue(selectedZone, "AbsoluteHeight", 3);

        await InvokeTaskMethodAsync(viewModel, "RefreshCapturePreviewAsync");

        Assert.Equal(new[] { new CaptureRegion(10, 20, 4, 3) }, frameSource.CapturedRegions);
        Assert.True((bool)(GetPropertyValue(viewModel, "HasCapturePreview") ?? false));
        Assert.NotNull(GetPropertyValue(viewModel, "CapturePreviewImage"));
        Assert.Equal("Captured 4x3 at 12:00:01.", GetPropertyValue(viewModel, "CapturePreviewStatus"));
    }

    [Fact]
    public async Task RefreshCapturePreviewAsync_WhenCaptureFails_ReportsStatusAndLogsError()
    {
        var repository = new InMemoryProfileRepository();
        var logger = new TestApplicationLogger();
        var viewModel = CreateMainViewModel(
            repository,
            new TestSettingsService(),
            logger: logger,
            frameSource: new TestCaptureFrameSource
            {
                Failure = new CaptureFrameSourceException("capture source unavailable"),
            });
        ConfigureValidDraftProfile(viewModel, "Capture failure");
        InvokeMethod(viewModel, "AddZone");

        await InvokeTaskMethodAsync(viewModel, "RefreshCapturePreviewAsync");

        Assert.Equal(
            "Capture preview failed: capture source unavailable",
            GetPropertyValue(viewModel, "CapturePreviewStatus"));
        Assert.False((bool)(GetPropertyValue(viewModel, "HasCapturePreview") ?? true));
        Assert.Contains(logger.Errors, error => error.Contains("Capture preview failed.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MeasureCaptureRefreshAsync_WhenZoneSelected_CapturesThirtyFramesAndReportsFps()
    {
        var repository = new InMemoryProfileRepository();
        var frameSource = new TestCaptureFrameSource();
        var viewModel = CreateMainViewModel(
            repository,
            new TestSettingsService(),
            frameSource: frameSource);
        ConfigureValidDraftProfile(viewModel, "Capture refresh");
        InvokeMethod(viewModel, "AddZone");

        var selectedZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("Selected zone was not created.");
        SetPropertyValue(selectedZone, "AbsoluteX", 10);
        SetPropertyValue(selectedZone, "AbsoluteY", 20);
        SetPropertyValue(selectedZone, "AbsoluteWidth", 4);
        SetPropertyValue(selectedZone, "AbsoluteHeight", 3);

        await InvokeTaskMethodAsync(viewModel, "MeasureCaptureRefreshAsync");

        Assert.Equal(30, frameSource.CapturedRegions.Count);
        Assert.All(frameSource.CapturedRegions, region => Assert.Equal(new CaptureRegion(10, 20, 4, 3), region));
        Assert.True((bool)(GetPropertyValue(viewModel, "HasCapturePreview") ?? false));

        var summary = Assert.IsType<string>(GetPropertyValue(viewModel, "CaptureRefreshMetricsSummary"));
        Assert.Contains("30 frames", summary, StringComparison.Ordinal);
        Assert.Contains("FPS", summary, StringComparison.Ordinal);
        Assert.Contains("target 30+", summary, StringComparison.Ordinal);
    }

    private static object CreateMainViewModel(
        InMemoryProfileRepository repository,
        TestSettingsService settings,
        TestDialogService? dialog = null,
        TestProfileExchangeGateway? exchangeGateway = null,
        TestApplicationLogger? logger = null,
        TestCaptureFrameSource? frameSource = null)
    {
        var profileService = new ProfileService(repository, new ProfileValidator());
        var profileExchangeService = new ProfileExchangeService(
            exchangeGateway ?? new TestProfileExchangeGateway(),
            new ProfileMigrationService(),
            new ProfileValidator());
        var captureService = new CaptureService(frameSource ?? new TestCaptureFrameSource());
        var applicationLogger = logger ?? new TestApplicationLogger();
        var assembly = LoadUiAssembly();
        var viewModelType = assembly.GetType(
            "GameTranslator.UI.ViewModels.MainViewModel",
            throwOnError: true)
            ?? throw new InvalidOperationException("MainViewModel type was not found.");

        return Activator.CreateInstance(
                viewModelType,
                profileService,
                profileExchangeService,
                captureService,
                dialog ?? new TestDialogService(),
                settings,
                applicationLogger)
            ?? throw new InvalidOperationException("MainViewModel instance was not created.");
    }

    private static void ConfigureValidDraftProfile(object viewModel, string name)
    {
        InvokeMethod(viewModel, "BeginCreateProfile");
        SetPropertyValue(viewModel, "ProfileName", name);
        SetPropertyValue(viewModel, "TranslatorProvider", "Google");
        SetPropertyValue(viewModel, "SourceLanguage", "ja");
        SetPropertyValue(viewModel, "TargetLanguage", "en");
    }

    private static Task InvokeTaskMethodAsync(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found.");
        var result = method.Invoke(instance, Array.Empty<object?>())
            ?? throw new InvalidOperationException($"Method '{methodName}' returned null.");

        return (Task)result;
    }

    private static void InvokeMethod(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found.");

        method.Invoke(instance, Array.Empty<object?>());
    }

    private static void InvokeMethodWithArguments(object instance, string methodName, params object?[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found.");

        method.Invoke(instance, arguments);
    }

    private static object? GetPropertyValue(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(propertyName)?.GetValue(instance);
    }

    private static IReadOnlyList<string> GetValidationErrorMessages(object viewModel)
    {
        var validationErrors = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            GetPropertyValue(viewModel, "ValidationErrors"));

        return validationErrors.Cast<object>()
            .Select(error => error.ToString() ?? string.Empty)
            .ToArray();
    }

    private static void SetPropertyValue(object instance, string propertyName, object? value)
    {
        var property = instance.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Property '{propertyName}' was not found.");

        property.SetValue(instance, value);
    }

    private static Assembly LoadUiAssembly()
    {
        var root = RepositoryRoot.Find();
        var configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        var assemblyPath = Path.Combine(
            root,
            "src",
            "GameTranslator.UI",
            "bin",
            configuration,
            "net9.0-windows10.0.19041.0",
            "GameTranslator.UI.dll");

        Assert.True(File.Exists(assemblyPath), $"UI assembly is missing. Build the solution first: {assemblyPath}");
        LoadOutputDependencies(Path.GetDirectoryName(assemblyPath)!);

        var loadedAssembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(
            assembly => string.Equals(assembly.GetName().Name, "GameTranslator.UI", StringComparison.Ordinal));
        if (loadedAssembly is not null)
        {
            return loadedAssembly;
        }

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    }

    private static void LoadOutputDependencies(string outputDirectory)
    {
        foreach (var dependencyPath in Directory.EnumerateFiles(outputDirectory, "*.dll"))
        {
            var assemblyName = Path.GetFileNameWithoutExtension(dependencyPath);
            if (string.Equals(assemblyName, "GameTranslator.UI", StringComparison.Ordinal)
                || AssemblyLoadContext.Default.Assemblies.Any(assembly =>
                    string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal)))
            {
                continue;
            }

            try
            {
                AssemblyLoadContext.Default.LoadFromAssemblyPath(dependencyPath);
            }
            catch (FileLoadException)
            {
            }
            catch (BadImageFormatException)
            {
            }
        }
    }

    private static GameProfile CreateProfile(
        string name,
        string provider,
        string sourceLanguage,
        string targetLanguage)
    {
        return new GameProfile
        {
            Name = name,
            TranslatorSettings = new TranslatorSettings
            {
                Provider = provider,
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage,
            },
        };
    }

    private sealed class TestSettingsService : ISettingsService
    {
        private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);

        public TValue? GetValue<TValue>(string key)
        {
            return values.TryGetValue(key, out var value)
                ? (TValue?)value
                : default;
        }

        public void SetValue<TValue>(string key, TValue? value)
        {
            values[key] = value;
        }
    }

    private sealed class TestDialogService : IDialogService
    {
        public string? OpenFilePath { get; set; }

        public string? SaveFilePath { get; set; }

        public DialogChoice YesNoCancelChoice { get; set; } = DialogChoice.Yes;

        public List<string> InformationMessages { get; } = new();

        public Task<string?> ShowOpenFileDialogAsync(string title, string filter, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OpenFilePath);
        }

        public Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string filter, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SaveFilePath);
        }

        public Task<DialogChoice> ShowYesNoCancelDialogAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(YesNoCancelChoice);
        }

        public Task ShowInformationAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            InformationMessages.Add($"{title}|{message}");
            return Task.CompletedTask;
        }
    }

    private sealed class TestProfileExchangeGateway : IProfileExchangeGateway
    {
        public GameProfile? ImportedProfile { get; set; }

        public GameProfile? ExportedProfile { get; private set; }

        public string? ExportedPath { get; private set; }

        public Task ExportAsync(GameProfile profile, string filePath, CancellationToken cancellationToken = default)
        {
            ExportedProfile = profile;
            ExportedPath = filePath;
            return Task.CompletedTask;
        }

        public Task<GameProfile> ImportAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                ImportedProfile ?? throw new InvalidOperationException("ImportedProfile was not configured."));
        }
    }

    private sealed class TestApplicationLogger : IApplicationLogger
    {
        public List<string> Errors { get; } = new();

        public void Error(Exception exception, string message)
        {
            Errors.Add(message);
        }

        public void Information(string message)
        {
        }

        public void Warning(string message)
        {
        }
    }

    private sealed class TestCaptureFrameSource : ICaptureFrameSource
    {
        private static readonly DateTimeOffset FrameTime = new(2026, 6, 12, 12, 0, 1, TimeSpan.Zero);

        public List<CaptureRegion> CapturedRegions { get; } = new();

        public Exception? Failure { get; init; }

        public Task<CapturedFrame> CaptureAsync(CaptureRegion region, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Failure is not null)
            {
                return Task.FromException<CapturedFrame>(Failure);
            }

            CapturedRegions.Add(region);

            var stride = checked(region.Width * 4);
            var pixels = Enumerable.Repeat((byte)127, checked(stride * region.Height)).ToArray();

            return Task.FromResult(
                new CapturedFrame(
                    region,
                    region.Width,
                    region.Height,
                    stride,
                    "Bgra32",
                    pixels,
                    FrameTime));
        }
    }

    private sealed class InMemoryProfileRepository : IProfileRepository
    {
        private readonly Dictionary<string, GameProfile> profiles = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<GameProfile>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<GameProfile>>(
                profiles.Values.OrderBy(profile => profile.Name, StringComparer.Ordinal).ToArray());
        }

        public Task<GameProfile?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            profiles.TryGetValue(id, out var profile);
            return Task.FromResult(profile);
        }

        public Task SaveAsync(GameProfile profile, CancellationToken cancellationToken = default)
        {
            profiles[profile.Id] = profile;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            profiles.Remove(id);
            return Task.CompletedTask;
        }
    }
}
