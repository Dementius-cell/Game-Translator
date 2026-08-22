using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Media;
using GameTranslator.Application.Cache;
using GameTranslator.Application.Abstractions;
using GameTranslator.Application.DependencyInjection;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Profiles;
using GameTranslator.Application.Settings;
using GameTranslator.Application.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.Tests.Smoke;

public sealed class PresentationCompositionTests
{
    [Fact]
    public void PresentationCompositionRoot_RegistersShellAndNavigationServices()
    {
        var services = InvokePresentationCompositionRoot();
        var serviceTypeNames = services
            .Select(descriptor => descriptor.ServiceType.FullName)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("GameTranslator.Application.Abstractions.INavigationService", serviceTypeNames);
        Assert.Contains("GameTranslator.Application.Abstractions.IDialogService", serviceTypeNames);
        Assert.Contains("GameTranslator.Application.Abstractions.ISettingsService", serviceTypeNames);
        Assert.Contains("GameTranslator.Application.Abstractions.IApplicationLogger", serviceTypeNames);
        Assert.Contains("GameTranslator.UI.ViewModels.ShellViewModel", serviceTypeNames);
        Assert.Contains("GameTranslator.UI.ViewModels.MainViewModel", serviceTypeNames);
        Assert.Contains("GameTranslator.UI.Views.ShellView", serviceTypeNames);
        Assert.Contains("GameTranslator.UI.MainWindow", serviceTypeNames);
    }

    [Fact]
    public void PresentationCompositionRoot_DoesNotRegisterOcrTranslationOrCaptureServices()
    {
        var services = InvokePresentationCompositionRoot();
        var registeredTypeNames = services
            .SelectMany(descriptor => new[] { descriptor.ServiceType, descriptor.ImplementationType })
            .Where(type => type is not null)
            .Select(type => type!.Name)
            .ToArray();

        var forbiddenTerms = new[] { "Ocr", "Translator", "Translation", "Capture" };

        foreach (var forbiddenTerm in forbiddenTerms)
        {
            Assert.DoesNotContain(
                registeredTypeNames,
                typeName => typeName.Contains(forbiddenTerm, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void PresentationCompositionRoot_RegistersOverlayPresentationService()
    {
        var services = InvokePresentationCompositionRoot();

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IOverlayService)
                && descriptor.ImplementationType?.FullName == "GameTranslator.UI.Services.WpfOverlayService");
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IOverlayTextMeasurer)
                && descriptor.ImplementationType?.FullName == "GameTranslator.UI.Services.WpfOverlayTextMeasurer");
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType.FullName == "GameTranslator.UI.Views.OverlayWindow");
    }

    [Fact]
    public void CombinedCompositionRoot_UsesWpfTextMeasurerForOverlayPositioning()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();
        InvokePresentationCompositionRoot(services);

        using var provider = services.BuildServiceProvider();
        var positioningService = provider.GetRequiredService<OverlayPositioningService>();
        var textMeasurerField = typeof(OverlayPositioningService).GetField(
            "textMeasurer",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(textMeasurerField);
        Assert.Equal(
            "GameTranslator.UI.Services.WpfOverlayTextMeasurer",
            textMeasurerField!.GetValue(positioningService)?.GetType().FullName);
    }

    [Fact]
    public void ShellView_CreatesAndMeasuresResponsiveVisualTreeAtSupportedWindowSizesWithoutShowingAWindow()
    {
        Exception? failure = null;
        var completed = false;
        var thread = new Thread(() =>
        {
            var rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "GameTranslator.Tests",
                Guid.NewGuid().ToString("N"));

            try
            {
                var services = new ServiceCollection();
                services.AddApplicationServices();
                services.AddSingleton(new ProfileStorageOptions(rootDirectory));
                services.AddSingleton(new SettingsStorageOptions(Path.Combine(rootDirectory, "state", "settings.json")));
                services.AddSingleton(new TranslationCacheStorageOptions(Path.Combine(rootDirectory, "cache", "translations.db")));
                InvokePresentationCompositionRoot(services);
                InvokeExternalServiceModules(services);

                using var provider = services.BuildServiceProvider();
                var uiAssembly = LoadUiAssembly();
                var shellType = uiAssembly.GetType(
                    "GameTranslator.UI.Views.ShellView",
                    throwOnError: true)
                    ?? throw new InvalidOperationException("ShellView type was not found.");
                var shell = Assert.IsAssignableFrom<FrameworkElement>(provider.GetRequiredService(shellType));
                var navigation = shell.DataContext!.GetType().GetProperty("Navigation")!.GetValue(shell.DataContext)!;
                var currentViewModel = navigation.GetType().GetProperty("CurrentViewModel")!.GetValue(navigation)!;
                var selectedTabProperty = currentViewModel.GetType().GetProperty("SelectedWorkspaceTabIndex")!;
                var tabCards = new Dictionary<int, string[]>
                {
                    [0] = new[] { "ZoneLiveWorkspaceCard" },
                    [1] = new[] { "TranslationSettingsCard" },
                    [2] = new[] { "OverlayPreviewCard", "OverlaySettingsCard" },
                    [3] = new[] { "ZoneLiveWorkspaceCard" },
                    [4] = new[] { "OcrPacksCard" },
                    [5] = new[] { "HotkeysSettingsCard" },
                };

                var supportedSizes = new[]
                {
                    new Size(1024, 640),
                    new Size(1280, 720),
                    new Size(1600, 900),
                    new Size(1920, 1080),
                    new Size(2560, 1440),
                };

                foreach (var size in supportedSizes)
                {
                    shell.Width = size.Width;
                    shell.Height = size.Height;

                    foreach (var tab in tabCards)
                    {
                        selectedTabProperty.SetValue(currentViewModel, tab.Key);
                        shell.Measure(size);
                        shell.Arrange(new Rect(new Point(0, 0), size));
                        shell.UpdateLayout();

                        foreach (var cardName in tab.Value)
                        {
                            var card = FindVisualDescendantByName<FrameworkElement>(shell, cardName);
                            Assert.NotNull(card);
                            Assert.Equal(Visibility.Visible, card!.Visibility);
                            AssertFitsHorizontally(shell, card, size.Width);
                        }
                    }

                    selectedTabProperty.SetValue(currentViewModel, 0);
                    shell.Measure(size);
                    shell.Arrange(new Rect(new Point(0, 0), size));
                    shell.UpdateLayout();

                    var responsivePanel = FindVisualDescendantByName<FrameworkElement>(shell, "ZoneResponsivePanels");
                    var surfaceCard = FindVisualDescendantByName<FrameworkElement>(shell, "ZoneSurfaceCard");
                    var preprocessingCard = FindVisualDescendantByName<FrameworkElement>(shell, "OcrPreprocessingCard");
                    var parametersPanel = FindVisualDescendantByName<FrameworkElement>(shell, "SelectedZoneParametersPanel");
                    var headerActions = FindVisualDescendantByName<FrameworkElement>(shell, "WorkspaceHeaderActions");

                    Assert.NotNull(responsivePanel);
                    Assert.NotNull(surfaceCard);
                    Assert.NotNull(preprocessingCard);
                    Assert.NotNull(parametersPanel);
                    Assert.NotNull(headerActions);
                    AssertFitsHorizontally(shell, responsivePanel!, size.Width);
                    AssertFitsHorizontally(shell, surfaceCard!, size.Width);
                    AssertFitsHorizontally(shell, preprocessingCard!, size.Width);
                    AssertFitsHorizontally(shell, parametersPanel!, size.Width);
                    AssertFitsHorizontally(shell, headerActions!, size.Width);

                    var surfaceTop = surfaceCard!.TransformToAncestor(shell).Transform(new Point()).Y;
                    var preprocessingTop = preprocessingCard!.TransformToAncestor(shell).Transform(new Point()).Y;
                    if (size.Width >= 1600)
                    {
                        Assert.InRange(Math.Abs(surfaceTop - preprocessingTop), 0, 1);
                    }
                    else
                    {
                        Assert.True(
                            Math.Abs(surfaceTop - preprocessingTop) > 1,
                            $"Zone cards did not wrap at {size.Width}x{size.Height}.");
                    }
                }

                completed = true;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Headless ShellView measure timed out.");
        Assert.Null(failure);
        Assert.True(completed);
    }

    [Fact]
    public void UiProject_DoesNotReferenceInfrastructureProject()
    {
        var references = ProjectFileReader.GetProjectReferences("src/GameTranslator.UI/GameTranslator.UI.csproj");

        Assert.DoesNotContain(
            "src/GameTranslator.Infrastructure/GameTranslator.Infrastructure.csproj",
            references);
    }

    [Fact]
    public async Task ExternalServiceModuleLoader_LoadsInfrastructureProfileStorageWithoutUiProjectReference()
    {
        var services = new ServiceCollection();
        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "GameTranslator.Tests",
            Guid.NewGuid().ToString("N"));
        services.AddSingleton(new ProfileStorageOptions(rootDirectory));
        services.AddSingleton(new SettingsStorageOptions(Path.Combine(rootDirectory, "state", "settings.json")));
        services.AddSingleton(new TranslationCacheStorageOptions(Path.Combine(rootDirectory, "cache", "translations.db")));

        var uiAssembly = LoadUiAssembly();
        var extensionType = uiAssembly.GetType(
            "GameTranslator.UI.DependencyInjection.ExternalServiceModuleLoader",
            throwOnError: true)
            ?? throw new InvalidOperationException("External service module loader type was not found.");
        var method = extensionType.GetMethod(
            "AddExternalServiceModules",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("AddExternalServiceModules method was not found.");

        var result = method.Invoke(null, new object[] { services, new[] { "GameTranslator.Infrastructure" } });

        Assert.Same(services, result);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType.FullName == "GameTranslator.Application.Profiles.IProfileRepository");
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ISettingsService));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ITranslationCacheRepository));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IApplicationUpdateProvider));

        using var provider = services.BuildServiceProvider();
        var cacheRepository = provider.GetRequiredService<ITranslationCacheRepository>();
        var deletedCount = await cacheRepository.DeleteExpiredAsync(DateTimeOffset.UtcNow);

        Assert.Equal(0, deletedCount);
    }

    [Fact]
    public void ExternalServiceModuleLoader_ConfiguresNativeDependencyResolutionForExternalModules()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot.Find(),
            "src",
            "GameTranslator.UI",
            "DependencyInjection",
            "ExternalServiceModuleLoader.cs"));

        Assert.Contains("NativeLibrary.SetDllImportResolver", source, StringComparison.Ordinal);
        Assert.Contains("\"runtimes\"", source, StringComparison.Ordinal);
        Assert.Contains("\"native\"", source, StringComparison.Ordinal);
    }

    private static IServiceCollection InvokePresentationCompositionRoot(IServiceCollection? services = null)
    {
        var uiAssembly = LoadUiAssembly();
        var extensionType = uiAssembly.GetType(
            "GameTranslator.UI.DependencyInjection.PresentationServiceCollectionExtensions",
            throwOnError: true)
            ?? throw new InvalidOperationException("Presentation service collection extension type was not found.");

        var method = extensionType.GetMethod(
            "AddPresentationServices",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("AddPresentationServices method was not found.");

        services ??= new ServiceCollection();
        var result = method.Invoke(null, new object[] { services });

        Assert.Same(services, result);

        return services;
    }

    private static void InvokeExternalServiceModules(IServiceCollection services)
    {
        var uiAssembly = LoadUiAssembly();
        var extensionType = uiAssembly.GetType(
            "GameTranslator.UI.DependencyInjection.ExternalServiceModuleLoader",
            throwOnError: true)
            ?? throw new InvalidOperationException("External service module loader type was not found.");
        var method = extensionType.GetMethod(
            "AddExternalServiceModules",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("AddExternalServiceModules method was not found.");

        method.Invoke(null, new object[] { services, new[] { "GameTranslator.Infrastructure" } });
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

        return Assembly.LoadFrom(assemblyPath);
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

    private static void AssertFitsHorizontally(FrameworkElement shell, FrameworkElement element, double availableWidth)
    {
        var origin = element.TransformToAncestor(shell).Transform(new Point());
        Assert.True(origin.X >= -0.5, $"'{element.Name}' begins outside the shell at X={origin.X:0.##}.");
        Assert.True(
            origin.X + element.ActualWidth <= availableWidth + 0.5,
            $"'{element.Name}' exceeds width {availableWidth:0}: shell={shell.ActualWidth:0.##}, x={origin.X:0.##}, width={element.ActualWidth:0.##}, right={origin.X + element.ActualWidth:0.##}.");
    }

    private static TElement? FindVisualDescendantByName<TElement>(DependencyObject parent, string name)
        where TElement : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TElement match && string.Equals(match.Name, name, StringComparison.Ordinal))
            {
                return match;
            }

            var descendant = FindVisualDescendantByName<TElement>(child, name);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
