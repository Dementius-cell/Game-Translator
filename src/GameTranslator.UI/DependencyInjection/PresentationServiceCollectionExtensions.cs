using GameTranslator.Application.Abstractions;
using GameTranslator.UI.Services;
using GameTranslator.UI.ViewModels;
using GameTranslator.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.UI.DependencyInjection;

public static class PresentationServiceCollectionExtensions
{
    public static IServiceCollection AddPresentationServices(this IServiceCollection services)
    {
        services.AddSingleton<IApplicationLogger, SerilogApplicationLogger>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ISettingsService, InMemorySettingsService>();
        services.AddSingleton<INavigationService, NavigationService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ShellViewModel>();

        services.AddSingleton<ShellView>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
