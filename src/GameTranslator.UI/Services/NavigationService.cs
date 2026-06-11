using GameTranslator.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace GameTranslator.UI.Services;

public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider serviceProvider;

    public NavigationService(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public object? CurrentViewModel { get; private set; }

    public void NavigateTo<TViewModel>()
        where TViewModel : class
    {
        CurrentViewModel = serviceProvider.GetRequiredService<TViewModel>();
    }
}
