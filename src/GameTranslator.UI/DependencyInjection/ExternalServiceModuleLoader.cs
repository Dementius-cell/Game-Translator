using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using GameTranslator.Application.Composition;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GameTranslator.UI.DependencyInjection;

public static class ExternalServiceModuleLoader
{
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
