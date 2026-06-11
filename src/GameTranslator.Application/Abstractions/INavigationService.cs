namespace GameTranslator.Application.Abstractions;

public interface INavigationService
{
    object? CurrentViewModel { get; }

    void NavigateTo<TViewModel>()
        where TViewModel : class;
}
