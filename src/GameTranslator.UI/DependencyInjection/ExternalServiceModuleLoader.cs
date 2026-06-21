using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using GameTranslator.Application.Composition;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GameTranslator.UI.DependencyInjection;

public static class ExternalServiceModuleLoader
{
    private static readonly object NativeResolverLock = new();
    private static readonly Dictionary<string, string> NativeResolverDirectories = new(StringComparer.Ordinal);

    public static IServiceCollection AddExternalServiceModules(
        this IServiceCollection services,
        params string[] assemblyNames)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblyNames);

        foreach (var assemblyName in assemblyNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            var assembly = LoadAssembly(assemblyName);
            if (assembly is null)
            {
                Log.Warning("Optional service module assembly {AssemblyName} was not found.", assemblyName);
                continue;
            }

            LoadModuleDependencies(assembly);

            foreach (var module in CreateModules(assembly))
            {
                module.RegisterServices(services);
                Log.Information("Registered service module {ModuleType}.", module.GetType().FullName);
            }
        }

        return services;
    }

    private static Assembly? LoadAssembly(string assemblyName)
    {
        try
        {
            return Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (FileNotFoundException)
        {
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");

            return File.Exists(assemblyPath)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath)
                : null;
        }
    }

    private static void LoadModuleDependencies(Assembly assembly)
    {
        var moduleDirectory = Path.GetDirectoryName(assembly.Location);
        if (string.IsNullOrWhiteSpace(moduleDirectory) || !Directory.Exists(moduleDirectory))
        {
            return;
        }

        var dependencyAssemblies = new List<Assembly> { assembly };
        LoadNativeDependencies(moduleDirectory);

        foreach (var dependencyPath in Directory.EnumerateFiles(moduleDirectory, "*.dll"))
        {
            var dependencyName = Path.GetFileNameWithoutExtension(dependencyPath);
            var loadedDependency = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(loadedAssembly =>
                string.Equals(loadedAssembly.GetName().Name, dependencyName, StringComparison.Ordinal));
            if (loadedDependency is not null)
            {
                dependencyAssemblies.Add(loadedDependency);
                continue;
            }

            try
            {
                dependencyAssemblies.Add(AssemblyLoadContext.Default.LoadFromAssemblyPath(dependencyPath));
            }
            catch (BadImageFormatException)
            {
            }
            catch (FileLoadException)
            {
            }
        }

        foreach (var dependencyAssembly in dependencyAssemblies.Distinct())
        {
            RegisterNativeDependencyResolver(dependencyAssembly, moduleDirectory);
        }
    }

    private static void LoadNativeDependencies(string moduleDirectory)
    {
        foreach (var nativeDirectory in GetNativeDependencyDirectories(moduleDirectory))
        {
            if (!Directory.Exists(nativeDirectory))
            {
                continue;
            }

            foreach (var nativeDependencyPath in Directory.EnumerateFiles(nativeDirectory, $"*{GetNativeLibraryExtension()}"))
            {
                NativeLibrary.TryLoad(nativeDependencyPath, out _);
            }
        }
    }

    private static void RegisterNativeDependencyResolver(Assembly assembly, string moduleDirectory)
    {
        if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.FullName))
        {
            return;
        }

        lock (NativeResolverLock)
        {
            NativeResolverDirectories[assembly.FullName] = moduleDirectory;
        }

        try
        {
            NativeLibrary.SetDllImportResolver(assembly, ResolveNativeDependency);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static IntPtr ResolveNativeDependency(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (string.IsNullOrWhiteSpace(libraryName) || string.IsNullOrWhiteSpace(assembly.FullName))
        {
            return IntPtr.Zero;
        }

        string? moduleDirectory;
        lock (NativeResolverLock)
        {
            NativeResolverDirectories.TryGetValue(assembly.FullName, out moduleDirectory);
        }

        if (string.IsNullOrWhiteSpace(moduleDirectory))
        {
            return IntPtr.Zero;
        }

        foreach (var candidatePath in CreateNativeLibraryCandidatePaths(moduleDirectory, libraryName))
        {
            if (File.Exists(candidatePath) && NativeLibrary.TryLoad(candidatePath, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> CreateNativeLibraryCandidatePaths(
        string moduleDirectory,
        string libraryName)
    {
        foreach (var candidateName in CreateNativeLibraryCandidateNames(libraryName))
        {
            yield return Path.Combine(moduleDirectory, candidateName);

            foreach (var nativeDirectory in GetNativeDependencyDirectories(moduleDirectory))
            {
                yield return Path.Combine(nativeDirectory, candidateName);
            }
        }
    }

    private static IEnumerable<string> CreateNativeLibraryCandidateNames(string libraryName)
    {
        var trimmedName = libraryName.Trim();
        yield return trimmedName;

        if (!Path.HasExtension(trimmedName))
        {
            yield return trimmedName + GetNativeLibraryExtension();

            if (!OperatingSystem.IsWindows() && !trimmedName.StartsWith("lib", StringComparison.Ordinal))
            {
                yield return "lib" + trimmedName + GetNativeLibraryExtension();
            }
        }
    }

    private static IEnumerable<string> GetNativeDependencyDirectories(string moduleDirectory)
    {
        yield return Path.Combine(moduleDirectory, "runtimes", GetRuntimeIdentifier(), "native");
    }

    private static string GetRuntimeIdentifier()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;

        if (OperatingSystem.IsWindows())
        {
            return architecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.X86 => "win-x86",
                Architecture.Arm => "win-arm",
                Architecture.Arm64 => "win-arm64",
                _ => "win",
            };
        }

        if (OperatingSystem.IsLinux())
        {
            return architecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                _ => "linux",
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return architecture switch
            {
                Architecture.X64 => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _ => "osx",
            };
        }

        return architecture.ToString().ToLowerInvariant();
    }

    private static string GetNativeLibraryExtension()
    {
        return OperatingSystem.IsWindows()
            ? ".dll"
            : OperatingSystem.IsMacOS()
                ? ".dylib"
                : ".so";
    }

    private static IEnumerable<IApplicationServiceModule> CreateModules(Assembly assembly)
    {
        return assembly
            .GetTypes()
            .Where(type =>
                typeof(IApplicationServiceModule).IsAssignableFrom(type)
                && !type.IsAbstract
                && !type.IsInterface)
            .Select(type => Activator.CreateInstance(type))
            .OfType<IApplicationServiceModule>();
    }
}
