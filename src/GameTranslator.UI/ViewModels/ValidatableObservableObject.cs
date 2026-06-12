using System.Collections;
using System.ComponentModel;

namespace GameTranslator.UI.ViewModels;

public abstract class ValidatableObservableObject : ObservableObject, INotifyDataErrorInfo
{
    private readonly Dictionary<string, string[]> errorsByProperty = new(StringComparer.Ordinal);

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public bool HasErrors => errorsByProperty.Count != 0;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return errorsByProperty.Values.SelectMany(errors => errors).Distinct(StringComparer.Ordinal).ToArray();
        }

        return errorsByProperty.TryGetValue(propertyName, out var errors)
            ? errors
            : Array.Empty<string>();
    }

    protected void SetErrors(string propertyName, IEnumerable<string> errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(errors);

        var normalizedErrors = errors
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Select(error => error.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedErrors.Length == 0)
        {
            if (errorsByProperty.Remove(propertyName))
            {
                OnPropertyChanged(nameof(HasErrors));
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            }

            return;
        }

        if (errorsByProperty.TryGetValue(propertyName, out var existingErrors)
            && existingErrors.SequenceEqual(normalizedErrors, StringComparer.Ordinal))
        {
            return;
        }

        errorsByProperty[propertyName] = normalizedErrors;
        OnPropertyChanged(nameof(HasErrors));
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }
}
