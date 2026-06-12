using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using GameTranslator.Application.Abstractions;
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
    }

    private static object CreateMainViewModel(
        InMemoryProfileRepository repository,
        TestSettingsService settings,
        TestDialogService? dialog = null,
        TestProfileExchangeGateway? exchangeGateway = null)
    {
        var profileService = new ProfileService(repository, new ProfileValidator());
        var profileExchangeService = new ProfileExchangeService(exchangeGateway ?? new TestProfileExchangeGateway(), new ProfileValidator());
        var logger = new TestApplicationLogger();
        var assembly = LoadUiAssembly();
        var viewModelType = assembly.GetType(
            "GameTranslator.UI.ViewModels.MainViewModel",
            throwOnError: true)
            ?? throw new InvalidOperationException("MainViewModel type was not found.");

        return Activator.CreateInstance(viewModelType, profileService, profileExchangeService, dialog ?? new TestDialogService(), settings, logger)
            ?? throw new InvalidOperationException("MainViewModel instance was not created.");
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

    private static object? GetPropertyValue(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(propertyName)?.GetValue(instance);
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
            "net9.0-windows",
            "GameTranslator.UI.dll");

        Assert.True(File.Exists(assemblyPath), $"UI assembly is missing. Build the solution first: {assemblyPath}");

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
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

        public Task<string?> ShowOpenFileDialogAsync(string title, string filter, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OpenFilePath);
        }

        public Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string filter, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SaveFilePath);
        }

        public Task ShowInformationAsync(string title, string message, CancellationToken cancellationToken = default)
        {
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
        public void Error(Exception exception, string message)
        {
        }

        public void Information(string message)
        {
        }

        public void Warning(string message)
        {
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
