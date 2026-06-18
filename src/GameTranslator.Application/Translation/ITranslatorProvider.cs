namespace GameTranslator.Application.Translation;

/// <summary>
/// Translates text through a provider without exposing provider-specific SDK types to the application layer.
/// </summary>
public interface ITranslatorProvider
{
    string ProviderId { get; }

    Task<TranslateResponse> TranslateAsync(
        TranslateRequest request,
        CancellationToken cancellationToken = default);
}
