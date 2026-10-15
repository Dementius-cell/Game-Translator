using GameTranslator.UI.Localization;
using GameTranslator.Application.Abstractions;
using Microsoft.Win32;
using System.Windows;

namespace GameTranslator.UI.Services;

public sealed class DialogService : IDialogService
{
    public Task<string?> ShowOpenFileDialogAsync(string title, string filter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new OpenFileDialog
        {
            Title = UiText.Translate(title),
            Filter = UiText.Translate(filter),
            CheckFileExists = true,
            Multiselect = false,
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<string?> ShowSaveFileDialogAsync(
        string title,
        string defaultFileName,
        string filter,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new SaveFileDialog
        {
            Title = UiText.Translate(title),
            FileName = defaultFileName,
            Filter = UiText.Translate(filter),
            OverwritePrompt = true,
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<DialogChoice> ShowYesNoCancelDialogAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = MessageBox.Show(
            UiText.Translate(message),
            UiText.Translate(title),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return Task.FromResult(result switch
        {
            MessageBoxResult.Yes => DialogChoice.Yes,
            MessageBoxResult.No => DialogChoice.No,
            _ => DialogChoice.Cancel,
        });
    }

    public Task ShowInformationAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MessageBox.Show(UiText.Translate(message), UiText.Translate(title), MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    }
}
