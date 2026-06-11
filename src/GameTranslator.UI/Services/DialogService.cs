using GameTranslator.Application.Abstractions;

namespace GameTranslator.UI.Services;

public sealed class DialogService : IDialogService
{
    public Task ShowInformationAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
