using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using GameTranslator.UI.ViewModels;

namespace GameTranslator.UI.Localization;

[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.Get(key);
}

public sealed class LocalizedTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is KeyValuePair<GameTranslator.Domain.Profiles.WatermarkFilterMode, string> watermark)
            return UiText.Translate(watermark.Value);
        if (value is LanguageOption language)
            return string.IsNullOrWhiteSpace(language.Code) ? UiText.Translate(language.Name) : $"{language.Code} {UiText.Translate(language.Name)}";
        var displayName = value?.GetType().GetProperty("DisplayName")?.GetValue(value) as string;
        return UiText.Translate(displayName ?? value?.ToString());
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
