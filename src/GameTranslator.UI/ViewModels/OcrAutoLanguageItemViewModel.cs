namespace GameTranslator.UI.ViewModels;

public sealed class OcrAutoLanguageItemViewModel(string code, string name, bool selected, Action changed) : ObservableObject
{
    private bool isSelected = selected;
    public string Code { get; } = code;
    public string DisplayName { get; } = $"{name} ({code})";
    public bool Matches(string? query)
    {
        var text = query?.Trim() ?? string.Empty;
        var aliases = Code switch
        {
            "eng" => "английский", "jpn" => "японский", "kor" => "корейский",
            "chi_sim" => "китайский упрощённый упрощенный", "chi_tra" => "китайский традиционный",
            "rus" => "русский", "deu" => "немецкий", "fra" => "французский",
            "spa" => "испанский", "ukr" => "украинский", _ => string.Empty
        };
        return DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || aliases.Contains(text, StringComparison.OrdinalIgnoreCase);
    }
    public bool IsSelected
    {
        get => isSelected;
        set { if (SetProperty(ref isSelected, value)) changed(); }
    }
    internal void SetSelected(bool value) => SetProperty(ref isSelected, value, nameof(IsSelected));
}
