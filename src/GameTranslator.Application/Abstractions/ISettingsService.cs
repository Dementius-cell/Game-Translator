namespace GameTranslator.Application.Abstractions;

public interface ISettingsService
{
    TValue? GetValue<TValue>(string key);

    void SetValue<TValue>(string key, TValue? value);
}
