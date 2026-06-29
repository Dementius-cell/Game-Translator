namespace GameTranslator.Application.Translation;

public enum TranslatorProviderFailureKind
{
    Unknown,
    Configuration,
    Http,
    Throttled,
    EmptyResponse,
    Parse,
    UnsupportedResponse,
    ProviderCode,
    AllProvidersFailed,
    Unexpected,
}
