namespace GameTranslator.Application.Abstractions;

public interface IDialogService
{
    Task ShowInformationAsync(string title, string message, CancellationToken cancellationToken = default);
}
