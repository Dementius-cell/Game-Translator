using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
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
        SetPropertyValue(viewModel, "OcrOrientationMode", OcrOrientationMode.Vertical);

        await InvokeTaskMethodAsync(viewModel, "SaveAsync");

        var storedProfiles = await repository.ListAsync();

        Assert.Single(storedProfiles);
        Assert.Equal("Cyberpunk 2077", storedProfiles[0].Name);
        Assert.Equal("Google", storedProfiles[0].TranslatorSettings.Provider);
        Assert.Equal(OcrOrientationMode.Vertical, storedProfiles[0].OcrSettings.OrientationMode);
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
        settings.SetValue("shell.draft.ocr.orientationMode", OcrOrientationMode.Vertical);
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
        Assert.Equal(OcrOrientationMode.Vertical, GetPropertyValue(viewModel, "OcrOrientationMode"));
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
        SetPropertyValue(viewModel, "OcrOrientationMode", OcrOrientationMode.Horizontal);
        SetPropertyValue(viewModel, "OverlayMaskMode", OverlayMaskMode.Darken);
        SetPropertyValue(viewModel, "OverlayMaskColor", "#303030");
        SetPropertyValue(viewModel, "OverlayOpacity", 0.55);
        SetPropertyValue(viewModel, "OverlayPadding", 10d);
        InvokeMethod(viewModel, "AddZone");
        var selectedZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("Selected zone was not created.");
        SetPropertyValue(selectedZone, "OverlayFontFamily", "Arial");
        SetPropertyValue(selectedZone, "OverlayFontSize", 20d);
        SetPropertyValue(selectedZone, "OverlayIsBold", false);
        SetPropertyValue(selectedZone, "OverlayIsItalic", true);
        SetPropertyValue(selectedZone, "OverlayCanExpandBeyondSource", true);

        Assert.Equal("Draft shell", settings.GetValue<string>("shell.draft.profile.name"));
        Assert.Equal("Persistent draft", settings.GetValue<string>("shell.draft.profile.description"));
        Assert.Equal("Yandex", settings.GetValue<string>("shell.draft.translator.provider"));
        Assert.Equal("ru", settings.GetValue<string>("shell.draft.translator.sourceLanguage"));
        Assert.Equal("en", settings.GetValue<string>("shell.draft.translator.targetLanguage"));
        Assert.Equal(OcrOrientationMode.Horizontal, settings.GetValue<OcrOrientationMode>("shell.draft.ocr.orientationMode"));
        Assert.Equal(OverlayMaskMode.Darken, settings.GetValue<OverlayMaskMode>("shell.draft.overlay.maskMode"));
        Assert.Equal("#303030", settings.GetValue<string>("shell.draft.overlay.maskColor"));
        Assert.Equal(0.55, settings.GetValue<double>("shell.draft.overlay.opacity"));
        Assert.Equal(10d, settings.GetValue<double>("shell.draft.overlay.padding"));
        var persistedZone = Assert.Single(settings.GetValue<OcrZone[]>("shell.draft.ocrZones") ?? Array.Empty<OcrZone>());
        Assert.Equal("Arial", persistedZone.TextStyle.FontFamily);
        Assert.Equal(20d, persistedZone.TextStyle.FontSize);
        Assert.False(persistedZone.TextStyle.IsBold);
        Assert.True(persistedZone.TextStyle.IsItalic);
        Assert.Equal(OverlayTextLayoutMode.ExpandFromSourceCenter, persistedZone.TextStyle.LayoutMode);
        Assert.Equal(
            GetPropertyValue(GetPropertyValue(viewModel, "SelectedZone")!, "Id"),
            settings.GetValue<string>("shell.draft.selectedZoneId"));
    }

    [Fact]
    public void LiveTranslationTimingPreset_WhenChanged_PersistsAppSetting()
    {
        var repository = new InMemoryProfileRepository();
        var settings = new TestSettingsService();
        var uiAssembly = LoadUiAssembly();
        var presetType = uiAssembly.GetType(
            "GameTranslator.UI.ViewModels.LiveTranslationTimingPreset",
            throwOnError: true)
            ?? throw new InvalidOperationException("LiveTranslationTimingPreset type was not found.");
        var conservativePreset = Enum.Parse(presetType, "Conservative");
        settings.SetValue("shell.live.translationTimingPreset", conservativePreset);

        var viewModel = CreateMainViewModel(repository, settings);

        Assert.Equal(conservativePreset, GetPropertyValue(viewModel, "LiveTranslationTimingPreset"));
        Assert.Contains("700 ms", Assert.IsType<string>(GetPropertyValue(viewModel, "LiveTranslationTimingSummary")));

        var fastPreset = Enum.Parse(presetType, "Fast");
        SetPropertyValue(viewModel, "LiveTranslationTimingPreset", fastPreset);

        Assert.Equal(fastPreset, settings.GetValue<object>("shell.live.translationTimingPreset"));
        var summary = Assert.IsType<string>(GetPropertyValue(viewModel, "LiveTranslationTimingSummary"));
        Assert.Contains("150 ms", summary);
        Assert.Contains("300 ms", summary);
    }

    [Fact]
    public void LanguageOptions_ExposeCommonWebTranslatorLanguages()
    {
        var viewModel = CreateMainViewModel(new InMemoryProfileRepository(), new TestSettingsService());

        var languages = Assert.IsAssignableFrom<System.Collections.IEnumerable>(GetPropertyValue(viewModel, "LanguageOptions"))
            .Cast<object>()
            .ToArray();

        Assert.Contains(languages, language => IsLanguageOption(language, "ar", "ar Arabic"));
        Assert.Contains(languages, language => IsLanguageOption(language, "hi", "hi Hindi"));
        Assert.Contains(languages, language => IsLanguageOption(language, "ja", "ja Japanese"));
        Assert.Contains(languages, language => IsLanguageOption(language, "ko", "ko Korean"));
        Assert.Contains(languages, language => IsLanguageOption(language, "ru", "ru Russian"));
        Assert.Contains(languages, language => IsLanguageOption(language, "zh-CN", "zh-CN Chinese (Simplified)"));
        Assert.Contains(languages, language => IsLanguageOption(language, "zh-TW", "zh-TW Chinese (Traditional)"));
    }

    [Fact]
    public void PickScreenZone_WhenPickerReturnsRegion_CreatesZoneFromScreenCoordinates()
    {
        var repository = new InMemoryProfileRepository();
        var settings = new TestSettingsService();
        var uiAssembly = LoadUiAssembly();
        var picker = CreateScreenRegionPicker(
            uiAssembly,
            CreateScreenRegionSelectionResult(uiAssembly, 96, 108, 384, 216, 1920, 1080));
        var viewModel = CreateMainViewModel(repository, settings, screenRegionPickerService: picker);

        ConfigureValidDraftProfile(viewModel, "Screen zone");
        InvokeMethod(viewModel, "PickScreenZone");

        var zones = Assert.IsAssignableFrom<System.Collections.IEnumerable>(GetPropertyValue(viewModel, "OcrZones"))
            .Cast<object>()
            .ToArray();
        var zone = Assert.Single(zones);
        var persistedZone = Assert.Single(settings.GetValue<OcrZone[]>("shell.draft.ocrZones") ?? Array.Empty<OcrZone>());

        Assert.Same(zone, GetPropertyValue(viewModel, "SelectedZone"));
        Assert.Equal(96, GetPropertyValue(zone, "AbsoluteX"));
        Assert.Equal(108, GetPropertyValue(zone, "AbsoluteY"));
        Assert.Equal(384, GetPropertyValue(zone, "AbsoluteWidth"));
        Assert.Equal(216, GetPropertyValue(zone, "AbsoluteHeight"));
        Assert.Equal(0.05d, GetPropertyValue(zone, "RelativeX"));
        Assert.Equal(0.1d, GetPropertyValue(zone, "RelativeY"));
        Assert.Equal(0.2d, GetPropertyValue(zone, "RelativeWidth"));
        Assert.Equal(0.2d, GetPropertyValue(zone, "RelativeHeight"));
        Assert.Equal(new AbsoluteRectangle(96, 108, 384, 216), persistedZone.AbsoluteBounds);
        Assert.Equal("Created zone 'Zone 1' from screen selection.", GetPropertyValue(viewModel, "StatusMessage"));
    }

    [Fact]
    public void PickScreenZone_WhenPickerReturnsNull_DoesNotCreateZone()
    {
        var repository = new InMemoryProfileRepository();
        var settings = new TestSettingsService();
        var uiAssembly = LoadUiAssembly();
        var picker = CreateScreenRegionPicker(uiAssembly, result: null);
        var viewModel = CreateMainViewModel(repository, settings, screenRegionPickerService: picker);

        ConfigureValidDraftProfile(viewModel, "Canceled zone");
        InvokeMethod(viewModel, "PickScreenZone");

        var zones = Assert.IsAssignableFrom<System.Collections.IEnumerable>(GetPropertyValue(viewModel, "OcrZones"));

        Assert.Empty(zones.Cast<object>());
        Assert.Equal("Screen zone selection canceled.", GetPropertyValue(viewModel, "StatusMessage"));
        Assert.Empty(settings.GetValue<OcrZone[]>("shell.draft.ocrZones") ?? Array.Empty<OcrZone>());
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
        SetPropertyValue(viewModel, "OcrOrientationMode", OcrOrientationMode.Horizontal);
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

    [Fact]
    public async Task RecognizeOcrPreviewAsync_WhenZoneSelected_CapturesZoneAndExposesTextBlocks()
    {
        var repository = new InMemoryProfileRepository();
        var frameSource = new TestCaptureFrameSource();
        var ocrEngine = new TestOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Press start", new BoundingBox(0, 0, 3, 1)),
                new OcrTextBlock("to continue", new BoundingBox(0, 1, 4, 2)),
            },
        };
        var viewModel = CreateMainViewModel(
            repository,
            new TestSettingsService(),
            frameSource: frameSource,
            ocrEngine: ocrEngine);
        ConfigureValidDraftProfile(viewModel, "OCR preview");
        InvokeMethod(viewModel, "AddZone");

        var selectedZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("Selected zone was not created.");
        SetPropertyValue(selectedZone, "AbsoluteX", 10);
        SetPropertyValue(selectedZone, "AbsoluteY", 20);
        SetPropertyValue(selectedZone, "AbsoluteWidth", 4);
        SetPropertyValue(selectedZone, "AbsoluteHeight", 3);

        await InvokeTaskMethodAsync(viewModel, "RecognizeOcrPreviewAsync");

        Assert.Equal(new[] { new CaptureRegion(10, 20, 4, 3) }, frameSource.CapturedRegions);
        var request = Assert.Single(ocrEngine.Requests);
        Assert.Equal(new CaptureRegion(10, 20, 4, 3), request.Region);
        Assert.Equal("ja", request.Language);
        Assert.Equal(GetPropertyValue(selectedZone, "Id"), request.ZoneId);
        Assert.Equal(OcrOrientationMode.Auto, request.OrientationMode);
        Assert.True((bool)(GetPropertyValue(viewModel, "HasCapturePreview") ?? false));
        Assert.True((bool)(GetPropertyValue(viewModel, "HasOcrPreview") ?? false));
        Assert.Equal(4, GetPropertyValue(viewModel, "CapturePreviewWidth"));
        Assert.Equal(3, GetPropertyValue(viewModel, "CapturePreviewHeight"));
        Assert.Equal("Press start\r\nto continue", GetPropertyValue(viewModel, "OcrPreviewText"));
        Assert.Equal("Recognized 2 text block(s) for 'Zone 1'.", GetPropertyValue(viewModel, "OcrPreviewStatus"));

        var debugBlocks = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            GetPropertyValue(viewModel, "OcrDebugTextBlocks"));
        var debugBlockArray = debugBlocks.Cast<object>().ToArray();
        Assert.Equal(2, debugBlockArray.Length);
        Assert.Equal("X 0  Y 0  W 3  H 1", GetPropertyValue(debugBlockArray[0], "CoordinatesSummary"));
        Assert.Equal("X 0  Y 1  W 4  H 2", GetPropertyValue(debugBlockArray[1], "CoordinatesSummary"));
        Assert.Equal("X 0  Y 0  W 3  H 1 | Press start", GetPropertyValue(debugBlockArray[0], "DebugLabel"));
    }

    [Fact]
    public async Task GlobalHotkeyPressed_ForRecognizeOcrPreview_CapturesSelectedZone()
    {
        var hotkeyRegistrar = new TestGlobalHotkeyRegistrar();
        var ocrEngine = new TestOcrEngine
        {
            BlocksFactory = _ => new[]
            {
                new OcrTextBlock("Fullscreen text", new BoundingBox(0, 0, 3, 1)),
            },
        };
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            ocrEngine: ocrEngine,
            hotkeyRegistrar: hotkeyRegistrar);
        ConfigureValidDraftProfile(viewModel, "OCR hotkey preview");
        InvokeMethod(viewModel, "AddZone");

        var selectedZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("Selected zone was not created.");
        SetPropertyValue(selectedZone, "AbsoluteX", 10);
        SetPropertyValue(selectedZone, "AbsoluteY", 20);
        SetPropertyValue(selectedZone, "AbsoluteWidth", 4);
        SetPropertyValue(selectedZone, "AbsoluteHeight", 3);
        InvokeMethod(viewModel, "ResetGlobalHotkeys");

        var registration = Assert.Single(
            hotkeyRegistrar.Registered,
            hotkey => hotkey.Action == GlobalHotkeyAction.RecognizeOcrPreview);
        hotkeyRegistrar.RaisePressed(registration.Id);
        await WaitForConditionAsync(() => ocrEngine.Requests.Count == 1);

        Assert.Equal(new CaptureRegion(10, 20, 4, 3), Assert.Single(ocrEngine.Requests).Region);
        Assert.Equal("Fullscreen text", GetPropertyValue(viewModel, "OcrPreviewText"));
        Assert.Equal("Recognized 1 text block(s) for 'Zone 1'.", GetPropertyValue(viewModel, "OcrPreviewStatus"));
    }

    [Fact]
    public async Task ShowOverlayPreview_WhenOcrPreviewExists_PositionsTextFromOcrBounds()
    {
        var overlay = new TestOverlayService();
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            frameSource: new TestCaptureFrameSource(),
            ocrEngine: new TestOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Press start", new BoundingBox(2, 1, 3, 2)),
                },
            },
            overlayService: overlay);
        ConfigureValidDraftProfile(viewModel, "OCR overlay positioning");
        InvokeMethod(viewModel, "AddZone");

        var selectedZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("Selected zone was not created.");
        SetPropertyValue(selectedZone, "AbsoluteX", 10);
        SetPropertyValue(selectedZone, "AbsoluteY", 20);
        SetPropertyValue(selectedZone, "AbsoluteWidth", 8);
        SetPropertyValue(selectedZone, "AbsoluteHeight", 6);

        await InvokeTaskMethodAsync(viewModel, "RecognizeOcrPreviewAsync");
        InvokeMethod(viewModel, "ShowOverlayPreview");

        Assert.True(overlay.IsVisible);
        Assert.Equal(
            "Overlay preview shown with 1 OCR text item(s).",
            GetPropertyValue(viewModel, "OverlayPreviewStatus"));
        var item = Assert.Single(overlay.CurrentSnapshot?.TextItems ?? Array.Empty<OverlayTextItem>());
        Assert.Equal("Press start", item.Text);
        Assert.Equal(12, item.X);
        Assert.Equal(21, item.Y);
        Assert.Equal(3, item.Width);
        Assert.Equal(2, item.Height);
    }

    [Fact]
    public async Task RecognizeOcrPreviewAsync_WhenOverlayVisible_UpdatesOverlayFromLatestOcrBounds()
    {
        var overlay = new TestOverlayService();
        var frameSource = new TestCaptureFrameSource
        {
            OnCapture = () => Assert.False(overlay.IsVisible),
        };
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            frameSource: frameSource,
            ocrEngine: new TestOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Updated text", new BoundingBox(1, 2, 4, 3)),
                },
            },
            overlayService: overlay);
        ConfigureValidDraftProfile(viewModel, "OCR overlay auto update");
        InvokeMethod(viewModel, "AddZone");

        var selectedZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("Selected zone was not created.");
        SetPropertyValue(selectedZone, "AbsoluteX", 30);
        SetPropertyValue(selectedZone, "AbsoluteY", 40);
        SetPropertyValue(selectedZone, "AbsoluteWidth", 8);
        SetPropertyValue(selectedZone, "AbsoluteHeight", 6);

        InvokeMethod(viewModel, "ShowOverlayPreview");
        await InvokeTaskMethodAsync(viewModel, "RecognizeOcrPreviewAsync");

        Assert.True(overlay.IsVisible);
        Assert.Equal(new[] { "Show:2", "Hide", "Show:1" }, overlay.Events);
        Assert.Equal(
            "Overlay preview updated with 1 OCR text item(s).",
            GetPropertyValue(viewModel, "OverlayPreviewStatus"));
        var item = Assert.Single(overlay.CurrentSnapshot?.TextItems ?? Array.Empty<OverlayTextItem>());
        Assert.Equal("Updated text", item.Text);
        Assert.Equal(31, item.X);
        Assert.Equal(42, item.Y);
        Assert.Equal(4, item.Width);
        Assert.Equal(3, item.Height);
    }

    [Fact]
    public async Task RecognizeOcrPreviewAsync_WhenOverlayIsExcludedFromCapture_KeepsOverlayVisibleDuringCapture()
    {
        var overlay = new TestOverlayService
        {
            IsExcludedFromCapture = true,
        };
        var frameSource = new TestCaptureFrameSource
        {
            OnCapture = () => Assert.True(overlay.IsVisible),
        };
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            frameSource: frameSource,
            ocrEngine: new TestOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Updated text", new BoundingBox(1, 2, 4, 3)),
                },
            },
            overlayService: overlay);
        ConfigureValidDraftProfile(viewModel, "OCR overlay no flicker");
        InvokeMethod(viewModel, "AddZone");

        var selectedZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("Selected zone was not created.");
        SetPropertyValue(selectedZone, "AbsoluteX", 30);
        SetPropertyValue(selectedZone, "AbsoluteY", 40);
        SetPropertyValue(selectedZone, "AbsoluteWidth", 8);
        SetPropertyValue(selectedZone, "AbsoluteHeight", 6);

        InvokeMethod(viewModel, "ShowOverlayPreview");
        await InvokeTaskMethodAsync(viewModel, "RecognizeOcrPreviewAsync");

        Assert.True(overlay.IsVisible);
        Assert.Equal(new[] { "Show:2", "Show:1" }, overlay.Events);
        Assert.Equal(
            "Overlay preview updated with 1 OCR text item(s).",
            GetPropertyValue(viewModel, "OverlayPreviewStatus"));
        var item = Assert.Single(overlay.CurrentSnapshot?.TextItems ?? Array.Empty<OverlayTextItem>());
        Assert.Equal("Updated text", item.Text);
    }

    [Fact]
    public async Task RecognizeOcrPreviewAsync_WhenOverlayVisibleAndOcrBoundsJitter_KeepsPreviousOverlayBounds()
    {
        var overlay = new TestOverlayService();
        var recognitionCount = 0;
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            frameSource: new TestCaptureFrameSource
            {
                OnCapture = () => Assert.False(overlay.IsVisible),
            },
            ocrEngine: new TestOcrEngine
            {
                BlocksFactory = _ =>
                {
                    recognitionCount++;
                    return recognitionCount == 1
                        ? new[] { new OcrTextBlock("Stable text", new BoundingBox(1, 2, 4, 3)) }
                        : new[] { new OcrTextBlock("Stable text", new BoundingBox(3, 0, 6, 5)) };
                },
            },
            overlayService: overlay);
        ConfigureValidDraftProfile(viewModel, "OCR overlay jitter");
        InvokeMethod(viewModel, "AddZone");

        var selectedZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("Selected zone was not created.");
        SetPropertyValue(selectedZone, "AbsoluteX", 30);
        SetPropertyValue(selectedZone, "AbsoluteY", 40);
        SetPropertyValue(selectedZone, "AbsoluteWidth", 8);
        SetPropertyValue(selectedZone, "AbsoluteHeight", 6);

        InvokeMethod(viewModel, "ShowOverlayPreview");
        await InvokeTaskMethodAsync(viewModel, "RecognizeOcrPreviewAsync");
        await InvokeTaskMethodAsync(viewModel, "RecognizeOcrPreviewAsync");

        Assert.True(overlay.IsVisible);
        Assert.Equal(new[] { "Show:2", "Hide", "Show:1", "Hide", "Show:1" }, overlay.Events);
        var item = Assert.Single(overlay.CurrentSnapshot?.TextItems ?? Array.Empty<OverlayTextItem>());
        Assert.Equal("Stable text", item.Text);
        Assert.Equal(31, item.X);
        Assert.Equal(42, item.Y);
        Assert.Equal(4, item.Width);
        Assert.Equal(3, item.Height);
    }

    [Fact]
    public async Task RecognizeOcrPreviewAsync_WhenOverlayVisibleAndOcrFails_RestoresPreviousOverlay()
    {
        var overlay = new TestOverlayService();
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            frameSource: new TestCaptureFrameSource
            {
                OnCapture = () => Assert.False(overlay.IsVisible),
            },
            ocrEngine: new TestOcrEngine
            {
                Failure = new OcrEngineException("ocr engine unavailable"),
            },
            overlayService: overlay);
        ConfigureValidDraftProfile(viewModel, "OCR overlay restore");
        InvokeMethod(viewModel, "AddZone");

        InvokeMethod(viewModel, "ShowOverlayPreview");
        var previousSnapshot = overlay.CurrentSnapshot;
        await InvokeTaskMethodAsync(viewModel, "RecognizeOcrPreviewAsync");

        Assert.True(overlay.IsVisible);
        Assert.Same(previousSnapshot, overlay.CurrentSnapshot);
        Assert.Equal(new[] { "Show:2", "Hide", "Show:2" }, overlay.Events);
        Assert.Equal(
            "OCR preview failed: ocr engine unavailable",
            GetPropertyValue(viewModel, "OcrPreviewStatus"));
        Assert.Equal(
            "Overlay preview shown with 2 test text item(s).",
            GetPropertyValue(viewModel, "OverlayPreviewStatus"));
    }

    [Fact]
    public async Task RecognizeOcrPreviewAsync_WhenOcrFails_ReportsStatusAndLogsError()
    {
        var repository = new InMemoryProfileRepository();
        var logger = new TestApplicationLogger();
        var viewModel = CreateMainViewModel(
            repository,
            new TestSettingsService(),
            logger: logger,
            frameSource: new TestCaptureFrameSource(),
            ocrEngine: new TestOcrEngine
            {
                Failure = new OcrEngineException("ocr engine unavailable"),
            });
        ConfigureValidDraftProfile(viewModel, "OCR failure");
        InvokeMethod(viewModel, "AddZone");

        await InvokeTaskMethodAsync(viewModel, "RecognizeOcrPreviewAsync");

        Assert.Equal(
            "OCR preview failed: ocr engine unavailable",
            GetPropertyValue(viewModel, "OcrPreviewStatus"));
        Assert.False((bool)(GetPropertyValue(viewModel, "HasOcrPreview") ?? true));
        Assert.Contains(logger.Errors, error => error.Contains("OCR preview failed.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecognizeOcrPreviewAsync_DebugOutputDoesNotExposeProfileSettings()
    {
        var repository = new InMemoryProfileRepository();
        var viewModel = CreateMainViewModel(
            repository,
            new TestSettingsService(),
            frameSource: new TestCaptureFrameSource(),
            ocrEngine: new TestOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Visible game text", new BoundingBox(0, 0, 4, 2)),
                },
            });
        ConfigureValidDraftProfile(viewModel, "OCR debug privacy");
        SetPropertyValue(viewModel, "ProfileDescription", "SECRET_PROFILE_NOTE");
        SetPropertyValue(viewModel, "TranslatorProvider", "SECRET_PROVIDER_TOKEN");
        SetPropertyValue(viewModel, "TargetLanguage", "SECRET_TARGET_LANGUAGE");
        InvokeMethod(viewModel, "AddZone");

        await InvokeTaskMethodAsync(viewModel, "RecognizeOcrPreviewAsync");

        var debugBlocks = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            GetPropertyValue(viewModel, "OcrDebugTextBlocks"));
        var debugText = string.Join(
            " ",
            debugBlocks.Cast<object>().Select(block => GetPropertyValue(block, "DebugLabel")?.ToString() ?? string.Empty));
        debugText += " " + (GetPropertyValue(viewModel, "OcrPreviewStatus")?.ToString() ?? string.Empty);
        debugText += " " + (GetPropertyValue(viewModel, "OcrPreviewText")?.ToString() ?? string.Empty);

        Assert.DoesNotContain("SECRET_PROFILE_NOTE", debugText, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_PROVIDER_TOKEN", debugText, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_TARGET_LANGUAGE", debugText, StringComparison.Ordinal);
        Assert.Contains("Visible game text", debugText, StringComparison.Ordinal);
        Assert.Contains("X 0  Y 0  W 4  H 2", debugText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveTranslatorCredentialsAsync_StoresCredentialsOnlyInCredentialStorage()
    {
        var repository = new InMemoryProfileRepository();
        var settings = new TestSettingsService();
        var credentialStorage = new TestCredentialStorage();
        var viewModel = CreateMainViewModel(
            repository,
            settings,
            credentialStorage: credentialStorage);
        ConfigureValidDraftProfile(viewModel, "Credential privacy");
        SetPropertyValue(viewModel, "TranslatorCredentialProjectId", "project-a");
        SetPropertyValue(viewModel, "TranslatorCredentialLocation", "us-central1");
        SetPropertyValue(viewModel, "TranslatorCredentialEndpoint", "https://translation.test");
        SetPropertyValue(viewModel, "TranslatorCredentialSecret", "SECRET_TRANSLATOR_TOKEN");

        await InvokeTaskMethodAsync(viewModel, "SaveTranslatorCredentialsAsync");
        await InvokeTaskMethodAsync(viewModel, "SaveAsync");

        var storedCredential = await credentialStorage.ReadAsync("Google");
        var storedProfiles = await repository.ListAsync();
        var profileJson = JsonSerializer.Serialize(storedProfiles);
        var statusText = $"{GetPropertyValue(viewModel, "TranslatorCredentialStatus")} {GetPropertyValue(viewModel, "StatusMessage")}";

        Assert.NotNull(storedCredential);
        Assert.Equal("SECRET_TRANSLATOR_TOKEN", storedCredential.AccessToken);
        Assert.Equal(string.Empty, GetPropertyValue(viewModel, "TranslatorCredentialSecret"));
        Assert.True((bool)(GetPropertyValue(viewModel, "HasStoredTranslatorCredentials") ?? false));
        Assert.DoesNotContain("SECRET_TRANSLATOR_TOKEN", profileJson, StringComparison.Ordinal);
        Assert.False(settings.ContainsSerializedText("SECRET_TRANSLATOR_TOKEN"));
        Assert.DoesNotContain("SECRET_TRANSLATOR_TOKEN", statusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateTranslatorCredentialsAsync_LoadsStoredMetadataWithoutExposingSecret()
    {
        var credentialStorage = new TestCredentialStorage();
        await credentialStorage.SaveAsync(
            new TranslatorCredentialRecord(
                "Google",
                "SECRET_TRANSLATOR_TOKEN",
                "project-a",
                "us-central1",
                new Uri("https://translation.test")));
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            credentialStorage: credentialStorage);
        SetPropertyValue(viewModel, "TranslatorProvider", "Google");

        await InvokeTaskMethodAsync(viewModel, "ValidateTranslatorCredentialsAsync");

        var statusText = GetPropertyValue(viewModel, "TranslatorCredentialStatus")?.ToString() ?? string.Empty;

        Assert.Equal("project-a", GetPropertyValue(viewModel, "TranslatorCredentialProjectId"));
        Assert.Equal("us-central1", GetPropertyValue(viewModel, "TranslatorCredentialLocation"));
        Assert.Equal("https://translation.test/", GetPropertyValue(viewModel, "TranslatorCredentialEndpoint"));
        Assert.Equal(string.Empty, GetPropertyValue(viewModel, "TranslatorCredentialSecret"));
        Assert.True((bool)(GetPropertyValue(viewModel, "HasStoredTranslatorCredentials") ?? false));
        Assert.DoesNotContain("SECRET_TRANSLATOR_TOKEN", statusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteTranslatorCredentialsAsync_RemovesCredentialAndClearsSecret()
    {
        var credentialStorage = new TestCredentialStorage();
        await credentialStorage.SaveAsync(
            new TranslatorCredentialRecord(
                "Google",
                "SECRET_TRANSLATOR_TOKEN",
                "project-a",
                "global",
                new Uri("https://translation.test")));
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            credentialStorage: credentialStorage);
        SetPropertyValue(viewModel, "TranslatorProvider", "Google");
        SetPropertyValue(viewModel, "TranslatorCredentialSecret", "SECRET_TRANSLATOR_TOKEN");

        await InvokeTaskMethodAsync(viewModel, "DeleteTranslatorCredentialsAsync");

        Assert.Null(await credentialStorage.ReadAsync("Google"));
        Assert.Equal(string.Empty, GetPropertyValue(viewModel, "TranslatorCredentialSecret"));
        Assert.False((bool)(GetPropertyValue(viewModel, "HasStoredTranslatorCredentials") ?? true));
        Assert.DoesNotContain(
            "SECRET_TRANSLATOR_TOKEN",
            GetPropertyValue(viewModel, "TranslatorCredentialStatus")?.ToString() ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ShowOverlayPreview_ShowsTestTextSnapshotAndUpdatesStatus()
    {
        var overlay = new TestOverlayService();
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            overlayService: overlay);

        InvokeMethod(viewModel, "ShowOverlayPreview");

        Assert.True(overlay.IsVisible);
        Assert.True((bool)(GetPropertyValue(viewModel, "IsOverlayPreviewVisible") ?? false));
        Assert.Equal(
            "Overlay preview shown with 2 test text item(s).",
            GetPropertyValue(viewModel, "OverlayPreviewStatus"));
        Assert.NotNull(overlay.CurrentSnapshot);
        Assert.Equal(2, overlay.CurrentSnapshot.TextItems.Count);
        Assert.Contains(
            overlay.CurrentSnapshot.TextItems,
            item => string.Equals(item.Text, "Game Translator overlay test", StringComparison.Ordinal));
        Assert.All(
            overlay.CurrentSnapshot.TextItems,
            item =>
            {
                Assert.True(item.Width > 0);
                Assert.True(item.Height > 0);
            });
    }


    [Fact]
    public void ShowOverlayPreview_WhenDebugOverlayEnabled_AddsDebugItemsAndMetrics()
    {
        var overlay = new TestOverlayService();
        var settings = new TestSettingsService();
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            settings,
            overlayService: overlay);
        SetPropertyValue(viewModel, "IsDebugOverlayEnabled", true);

        InvokeMethod(viewModel, "ShowOverlayPreview");

        Assert.True(overlay.IsVisible);
        Assert.NotNull(overlay.CurrentSnapshot);
        Assert.Equal(2, overlay.CurrentSnapshot.DebugItems.Count);
        Assert.Contains(overlay.CurrentSnapshot.DebugMetricLines, line => line.StartsWith("CPU:", StringComparison.Ordinal));
        Assert.Contains(overlay.CurrentSnapshot.DebugMetricLines, line => line.StartsWith("Cache:", StringComparison.Ordinal));
        Assert.Contains(
            "Debug overlay preview shows 2 box(es).",
            GetPropertyValue(viewModel, "DebugOverlayStatus")?.ToString(),
            StringComparison.Ordinal);
    }
    [Fact]
    public void HideOverlayPreview_HidesOverlayAndUpdatesStatus()
    {
        var overlay = new TestOverlayService();
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            overlayService: overlay);

        InvokeMethod(viewModel, "ShowOverlayPreview");
        InvokeMethod(viewModel, "HideOverlayPreview");

        Assert.False(overlay.IsVisible);
        Assert.False((bool)(GetPropertyValue(viewModel, "IsOverlayPreviewVisible") ?? true));
        Assert.Equal("Overlay preview hidden.", GetPropertyValue(viewModel, "OverlayPreviewStatus"));
    }

    [Fact]
    public async Task RunTranslationPipelineAsync_UsesSelectedZoneCredentialsAndShowsTranslatedOverlay()
    {
        var credentialStorage = new TestCredentialStorage();
        await credentialStorage.SaveAsync(
            new TranslatorCredentialRecord(
                "Google",
                "SECRET_TRANSLATOR_TOKEN",
                "project-a",
                "global",
                new Uri("https://translation.test")));
        var translator = new TestTranslatorProvider("Google", new[] { "Translated subtitle" });
        var overlay = new TestOverlayService();
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            ocrEngine: new TestOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Original subtitle", new BoundingBox(0, 0, 40, 12)),
                },
            },
            translatorProvider: translator,
            overlayService: overlay,
            credentialStorage: credentialStorage);

        ConfigureValidDraftProfile(viewModel, "Pipeline draft");
        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 10d, 20d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 110d, 70d);
        var selectedZone = GetPropertyValue(viewModel, "SelectedZone")
            ?? throw new InvalidOperationException("Selected zone was not created.");
        SetPropertyValue(selectedZone, "AbsoluteX", 300);
        SetPropertyValue(selectedZone, "AbsoluteY", 200);
        SetPropertyValue(selectedZone, "AbsoluteWidth", 300);
        SetPropertyValue(selectedZone, "AbsoluteHeight", 150);
        SetPropertyValue(selectedZone, "OverlayFontFamily", "Arial");
        SetPropertyValue(selectedZone, "OverlayFontSize", 20d);
        SetPropertyValue(selectedZone, "OverlayCanExpandBeyondSource", true);

        await InvokeTaskMethodAsync(viewModel, "RunTranslationPipelineAsync");

        Assert.NotNull(translator.Request);
        Assert.Equal("SECRET_TRANSLATOR_TOKEN", translator.Request?.Credentials.AccessToken);
        Assert.True(overlay.IsVisible);
        var overlayItem = Assert.Single(overlay.CurrentSnapshot!.TextItems);
        Assert.Equal("Translated subtitle", overlayItem.Text);
        Assert.Equal(320d, overlayItem.X + overlayItem.Width / 2d, precision: 0);
        Assert.Equal(206d, overlayItem.Y + overlayItem.Height / 2d, precision: 0);
        Assert.True(overlayItem.Width > 40);
        Assert.True(overlayItem.Height > 12);
        Assert.Equal("Arial", overlayItem.TextStyle.FontFamily);
        Assert.Equal(20d, overlayItem.TextStyle.FontSize);
        Assert.Equal(OverlayTextLayoutMode.ExpandFromSourceCenter, overlayItem.TextStyle.LayoutMode);
        Assert.Contains(
            "Full pipeline translated 1 text block(s)",
            GetPropertyValue(viewModel, "PipelineStatus")?.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SECRET_TRANSLATOR_TOKEN",
            GetPropertyValue(viewModel, "PipelineStatus")?.ToString() ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTranslationPipelineAsync_WhenAllZonesFail_ShowsFirstFailureStageAndDetail()
    {
        var credentialStorage = new TestCredentialStorage();
        await credentialStorage.SaveAsync(
            new TranslatorCredentialRecord(
                "Google",
                "SECRET_TRANSLATOR_TOKEN",
                "project-a",
                "global",
                new Uri("https://translation.test")));
        var cacheRepository = new TestTranslationCacheRepository
        {
            GetFailure = new InvalidOperationException("SQLite native dependency missing."),
        };
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            ocrEngine: new TestOcrEngine
            {
                BlocksFactory = _ => new[]
                {
                    new OcrTextBlock("Original subtitle", new BoundingBox(0, 0, 40, 12)),
                },
            },
            credentialStorage: credentialStorage,
            translationCacheRepository: cacheRepository);

        ConfigureValidDraftProfile(viewModel, "Pipeline failure draft");
        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 10d, 20d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 110d, 70d);

        await InvokeTaskMethodAsync(viewModel, "RunTranslationPipelineAsync");

        var pipelineStatus = GetPropertyValue(viewModel, "PipelineStatus")?.ToString() ?? string.Empty;
        Assert.Contains("Full pipeline failed for all 1 OCR zone(s).", pipelineStatus, StringComparison.Ordinal);
        Assert.Contains("failed during Cache", pipelineStatus, StringComparison.Ordinal);
        Assert.Contains("SQLite native dependency missing.", pipelineStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_TRANSLATOR_TOKEN", pipelineStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTranslationPipelineAsync_WhenCredentialsAreMissing_ShowsOcrPreviewAcrossAllZones()
    {
        var ocrEngine = new TestOcrEngine
        {
            BlocksFactory = request => new[]
            {
                new OcrTextBlock($"Text {request.ZoneId}", new BoundingBox(0, 0, 40, 12)),
            },
        };
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            ocrEngine: ocrEngine,
            credentialStorage: new TestCredentialStorage());

        ConfigureValidDraftProfile(viewModel, "Pipeline OCR batch draft");
        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 10d, 20d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 110d, 70d);
        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 150d, 20d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 250d, 70d);
        InvokeMethodWithArguments(viewModel, "StartZoneSelection", 290d, 20d);
        InvokeMethodWithArguments(viewModel, "CompleteZoneSelection", 390d, 70d);

        await InvokeTaskMethodAsync(viewModel, "RunTranslationPipelineAsync");

        Assert.Equal(3, ocrEngine.Requests.Count);
        var ocrPreviewText = GetPropertyValue(viewModel, "OcrPreviewText")?.ToString() ?? string.Empty;
        Assert.Contains("[Zone 1] Text", ocrPreviewText, StringComparison.Ordinal);
        Assert.Contains("[Zone 2] Text", ocrPreviewText, StringComparison.Ordinal);
        Assert.Contains("[Zone 3] Text", ocrPreviewText, StringComparison.Ordinal);
        Assert.Equal(
            "Recognized 3 text block(s) across 3 OCR zone(s). Preview image shows 'Zone 3'.",
            GetPropertyValue(viewModel, "OcrPreviewStatus"));

        var pipelineStatus = GetPropertyValue(viewModel, "PipelineStatus")?.ToString() ?? string.Empty;
        Assert.Contains("failed during Credentials", pipelineStatus, StringComparison.Ordinal);
        Assert.Contains("OCR recognized 3 text block(s) before the failure.", pipelineStatus, StringComparison.Ordinal);

        var debugBlocks = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            GetPropertyValue(viewModel, "OcrDebugTextBlocks"));
        var debugBlockArray = debugBlocks.Cast<object>().ToArray();
        Assert.Equal(3, debugBlockArray.Length);
        Assert.Equal(
            1,
            debugBlockArray.Count(block => (bool)(GetPropertyValue(block, "IsVisibleOnCapturePreview") ?? false)));
    }

    [Fact]
    public async Task CleanupTranslationCacheAsync_RemovesExpiredEntriesAndUpdatesStatus()
    {
        var cacheRepository = new TestTranslationCacheRepository();
        var key = new TranslationCacheKey("Google", "en", "ru", "Hello");
        await cacheRepository.SaveAsync(
            new TranslationCacheEntry(
                key,
                "Expired",
                new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
                hitCount: 0));
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            translationCacheRepository: cacheRepository);

        await InvokeTaskMethodAsync(viewModel, "CleanupTranslationCacheAsync");

        Assert.Empty(cacheRepository.Entries);
        Assert.Contains(
            "Translation cache cleanup removed 1 expired entry.",
            GetPropertyValue(viewModel, "TranslationCacheStatus")?.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenProviderCompletes_UpdatesStatus()
    {
        var updateProvider = new TestApplicationUpdateProvider
        {
            Result = ApplicationUpdateResult.CheckCompleted(),
        };
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            updateProvider: updateProvider);

        await InvokeTaskMethodAsync(viewModel, "CheckForUpdatesAsync");

        Assert.Equal(new[] { ApplicationUpdateCheckMode.Manual }, updateProvider.CheckModes);
        Assert.Contains(
            "Squirrel.Windows update check completed",
            GetPropertyValue(viewModel, "UpdateStatus")?.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_ChecksForUpdatesAtStartup()
    {
        var updateProvider = new TestApplicationUpdateProvider();
        var viewModel = CreateMainViewModel(
            new InMemoryProfileRepository(),
            new TestSettingsService(),
            updateProvider: updateProvider);

        await InvokeTaskMethodAsync(viewModel, "LoadAsync");

        Assert.Contains(ApplicationUpdateCheckMode.Startup, updateProvider.CheckModes);
        Assert.Equal(
            "Squirrel.Windows installation was not detected; update check skipped.",
            GetPropertyValue(viewModel, "UpdateStatus"));
    }

    private static object CreateMainViewModel(
        InMemoryProfileRepository repository,
        TestSettingsService settings,
        TestDialogService? dialog = null,
        TestProfileExchangeGateway? exchangeGateway = null,
        TestApplicationLogger? logger = null,
        TestCaptureFrameSource? frameSource = null,
        TestOcrEngine? ocrEngine = null,
        TestTranslatorProvider? translatorProvider = null,
        TestOverlayService? overlayService = null,
        TestCredentialStorage? credentialStorage = null,
        TestTranslationCacheRepository? translationCacheRepository = null,
        TestApplicationUpdateProvider? updateProvider = null,
        TestGlobalHotkeyRegistrar? hotkeyRegistrar = null,
        object? screenRegionPickerService = null)
    {
        var profileService = new ProfileService(repository, new ProfileValidator());
        var profileExchangeService = new ProfileExchangeService(
            exchangeGateway ?? new TestProfileExchangeGateway(),
            new ProfileMigrationService(),
            new ProfileValidator());
        var captureService = new CaptureService(frameSource ?? new TestCaptureFrameSource());
        var ocrService = new OcrService(ocrEngine ?? new TestOcrEngine());
        var credentialService = new TranslatorCredentialService(credentialStorage ?? new TestCredentialStorage());
        var overlayPositioningService = new OverlayPositioningService();
        var overlay = overlayService ?? new TestOverlayService();
        var translationCacheService = new TranslationCacheService(
            translationCacheRepository ?? new TestTranslationCacheRepository(),
            new TranslationCacheOptions());
        var applicationUpdateService = new ApplicationUpdateService(
            updateProvider ?? new TestApplicationUpdateProvider(),
            new ApplicationUpdateOptions("https://updates.test"));
        var translationPipelineService = new TranslationPipelineService(
            captureService,
            ocrService,
            new TranslatorManager(new ITranslatorProvider[] { translatorProvider ?? new TestTranslatorProvider("Google") }),
            credentialService,
            translationCacheService,
            overlayPositioningService,
            overlay);
        var applicationLogger = logger ?? new TestApplicationLogger();
        var globalHotkeyService = new GlobalHotkeyService(settings, hotkeyRegistrar ?? new TestGlobalHotkeyRegistrar());
        var assembly = LoadUiAssembly();
        var viewModelType = assembly.GetType(
            "GameTranslator.UI.ViewModels.MainViewModel",
            throwOnError: true)
            ?? throw new InvalidOperationException("MainViewModel type was not found.");

        var constructorArguments = screenRegionPickerService is null
            ? new object[]
            {
                profileService,
                profileExchangeService,
                captureService,
                ocrService,
                credentialService,
                translationPipelineService,
                translationCacheService,
                applicationUpdateService,
                globalHotkeyService,
                new DebugMetricFormatter(),
                new TestDebugResourceMonitor(),
                overlay,
                overlayPositioningService,
                dialog ?? new TestDialogService(),
                settings,
                applicationLogger,
            }
            : new object[]
            {
                profileService,
                profileExchangeService,
                captureService,
                ocrService,
                credentialService,
                translationPipelineService,
                translationCacheService,
                applicationUpdateService,
                globalHotkeyService,
                new DebugMetricFormatter(),
                new TestDebugResourceMonitor(),
                overlay,
                overlayPositioningService,
                dialog ?? new TestDialogService(),
                screenRegionPickerService,
                settings,
                applicationLogger,
            };

        return Activator.CreateInstance(viewModelType, constructorArguments)
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

    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(condition());
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

    private static bool IsLanguageOption(object instance, string code, string displayName)
    {
        return string.Equals(GetPropertyValue(instance, "Code")?.ToString(), code, StringComparison.Ordinal)
            && string.Equals(GetPropertyValue(instance, "DisplayName")?.ToString(), displayName, StringComparison.Ordinal);
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

    private static object CreateScreenRegionSelectionResult(
        Assembly uiAssembly,
        int x,
        int y,
        int width,
        int height,
        int referenceWidth,
        int referenceHeight)
    {
        var resultType = uiAssembly.GetType(
            "GameTranslator.UI.Services.ScreenRegionSelectionResult",
            throwOnError: true)
            ?? throw new InvalidOperationException("ScreenRegionSelectionResult type was not found.");

        return Activator.CreateInstance(resultType, x, y, width, height, referenceWidth, referenceHeight)
            ?? throw new InvalidOperationException("ScreenRegionSelectionResult instance was not created.");
    }

    private static object CreateScreenRegionPicker(Assembly uiAssembly, object? result)
    {
        var pickerType = uiAssembly.GetType(
            "GameTranslator.UI.Services.IScreenRegionPickerService",
            throwOnError: true)
            ?? throw new InvalidOperationException("IScreenRegionPickerService type was not found.");
        var proxy = DispatchProxy.Create(pickerType, typeof(TestScreenRegionPickerProxy));
        ((TestScreenRegionPickerProxy)proxy).Result = result;

        return proxy;
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

        public bool ContainsSerializedText(string text)
        {
            return JsonSerializer.Serialize(values).Contains(text, StringComparison.Ordinal);
        }
    }

    private class TestScreenRegionPickerProxy : DispatchProxy
    {
        public object? Result { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (string.Equals(targetMethod?.Name, "PickRegion", StringComparison.Ordinal))
            {
                return Result;
            }

            throw new InvalidOperationException($"Unexpected screen region picker method: {targetMethod?.Name}.");
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

        public Action? OnCapture { get; init; }

        public Task<CapturedFrame> CaptureAsync(CaptureRegion region, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnCapture?.Invoke();

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

    private sealed class TestOcrEngine : IOcrEngine
    {
        private static readonly DateTimeOffset RecognizedAt = new(2026, 6, 13, 12, 0, 2, TimeSpan.Zero);

        public string EngineId { get; init; } = OcrSettings.WindowsEngineId;

        public List<OcrRequest> Requests { get; } = new();

        public Exception? Failure { get; init; }

        public Func<OcrRequest, IReadOnlyList<OcrTextBlock>>? BlocksFactory { get; init; }

        public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Failure is not null)
            {
                return Task.FromException<OcrResult>(Failure);
            }

            Requests.Add(request);

            var blocks = BlocksFactory?.Invoke(request) ?? Array.Empty<OcrTextBlock>();
            return Task.FromResult(new OcrResult(request, blocks, RecognizedAt));
        }
    }

    private sealed class TestTranslatorProvider : ITranslatorProvider
    {
        private static readonly DateTimeOffset TranslatedAt = new(2026, 6, 19, 12, 0, 3, TimeSpan.Zero);

        private readonly IReadOnlyList<string>? translatedTexts;

        public TestTranslatorProvider(string providerId, IReadOnlyList<string>? translatedTexts = null)
        {
            ProviderId = providerId;
            this.translatedTexts = translatedTexts;
        }

        public string ProviderId { get; }

        public TranslateRequest? Request { get; private set; }

        public Task<TranslateResponse> TranslateAsync(
            TranslateRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;

            return Task.FromResult(
                new TranslateResponse(
                    translatedTexts ?? request.Texts.Select(text => $"Translated {text}"),
                    TranslatedAt));
        }
    }

    private sealed class TestOverlayService : IOverlayService
    {
        public bool IsVisible { get; private set; }

        public bool IsExcludedFromCapture { get; set; }

        public OverlaySnapshot? CurrentSnapshot { get; private set; }

        public List<string> Events { get; } = new();

        public void Show(OverlaySnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            IsVisible = true;
            Events.Add($"Show:{snapshot.TextItems.Count}");
        }

        public void Hide()
        {
            IsVisible = false;
            Events.Add("Hide");
        }
    }

    private sealed class TestTranslationCacheRepository : ITranslationCacheRepository
    {
        private readonly Dictionary<TranslationCacheKey, TranslationCacheEntry> entries = new();

        public IReadOnlyDictionary<TranslationCacheKey, TranslationCacheEntry> Entries => entries;

        public Exception? GetFailure { get; init; }

        public Task<TranslationCacheEntry?> GetAsync(
            TranslationCacheKey key,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (GetFailure is not null)
            {
                throw GetFailure;
            }

            entries.TryGetValue(key, out var entry);

            return Task.FromResult(entry?.IsExpired(now) == true ? null : entry);
        }

        public Task SaveAsync(
            TranslationCacheEntry entry,
            CancellationToken cancellationToken = default)
        {
            entries[entry.Key] = entry;
            return Task.CompletedTask;
        }

        public Task<int> DeleteExpiredAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var expiredKeys = entries
                .Where(pair => pair.Value.IsExpired(now))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var key in expiredKeys)
            {
                entries.Remove(key);
            }

            return Task.FromResult(expiredKeys.Length);
        }
    }

    private sealed class TestApplicationUpdateProvider : IApplicationUpdateProvider
    {
        public ApplicationUpdateResult Result { get; set; } = ApplicationUpdateResult.NotInstalled();

        public List<ApplicationUpdateCheckMode> CheckModes { get; } = new();

        public Task<ApplicationUpdateResult> CheckForUpdatesAsync(
            ApplicationUpdateOptions options,
            ApplicationUpdateCheckMode checkMode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckModes.Add(checkMode);

            return Task.FromResult(Result);
        }
    }

    private sealed class TestCredentialStorage : ICredentialStorage
    {
        private readonly Dictionary<string, TranslatorCredentialRecord> records = new(StringComparer.OrdinalIgnoreCase);

        public Task SaveAsync(
            TranslatorCredentialRecord credential,
            CancellationToken cancellationToken = default)
        {
            records[credential.Provider] = credential;

            return Task.CompletedTask;
        }

        public Task<TranslatorCredentialRecord?> ReadAsync(
            string provider,
            CancellationToken cancellationToken = default)
        {
            records.TryGetValue(provider, out var credential);

            return Task.FromResult(credential);
        }

        public Task DeleteAsync(string provider, CancellationToken cancellationToken = default)
        {
            records.Remove(provider);

            return Task.CompletedTask;
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

    private sealed class TestGlobalHotkeyRegistrar : IGlobalHotkeyRegistrar
    {
        public event EventHandler<GlobalHotkeyRegisteredEventArgs>? HotkeyPressed;

        public List<GlobalHotkeyRegistration> Registered { get; } = new();

        public GlobalHotkeyRegistrationResult Register(GlobalHotkeyRegistration registration)
        {
            Registered.Add(registration);
            return GlobalHotkeyRegistrationResult.Success();
        }

        public void Unregister(int id)
        {
            Registered.RemoveAll(registration => registration.Id == id);
        }

        public void UnregisterAll()
        {
            Registered.Clear();
        }

        public void RaisePressed(int id)
        {
            HotkeyPressed?.Invoke(this, new GlobalHotkeyRegisteredEventArgs(id));
        }
    }
    private sealed class TestDebugResourceMonitor : IDebugResourceMonitor
    {
        public DebugResourceSnapshot Sample()
        {
            return new DebugResourceSnapshot(12.5, 128 * 1024 * 1024);
        }
    }}
