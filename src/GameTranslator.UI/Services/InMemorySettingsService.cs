using System.Collections.Concurrent;
using GameTranslator.Application.Abstractions;

namespace GameTranslator.UI.Services;

public sealed class InMemorySettingsService : ISettingsService
{
    private readonly ConcurrentDictionary<string, object?> values = new(StringComparer.Ordinal);

    public TValue? GetValue<TValue>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return values.TryGetValue(key, out var value)
            ? (TValue?)value
            : default;
    }

    public void SetValue<TValue>(string key, TValue? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        values[key] = value;
    }
}
