namespace GameTranslator.Application.Abstractions;

public interface IDialogService
{
    Task<string?> ShowOpenFileDialogAsync(string title, string filter, CancellationToken cancellationToken = default);

    Task<string?> ShowSaveFileDialogAsync(
        string title,
        string defaultFileName,
        string filter,
        CancellationToken cancellationToken = default);

    Task ShowInformationAsync(string title, string message, CancellationToken cancellationToken = default);
}
