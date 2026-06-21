using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using GameTranslator.Application.Cache;
using GameTranslator.Application.Abstractions;
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
            descriptor => descriptor.ServiceType.FullName == "GameTranslator.UI.Views.OverlayWindow");
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

    private static IServiceCollection InvokePresentationCompositionRoot()
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

        var services = new ServiceCollection();
        var result = method.Invoke(null, new object[] { services });

        Assert.Same(services, result);

        return services;
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
}
