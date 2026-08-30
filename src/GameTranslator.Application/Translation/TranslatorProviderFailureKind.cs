namespace GameTranslator.Application.Translation;

public enum TranslatorProviderFailureKind
{
    Unknown,
    Configuration,
    Http,
    Throttled,
    Timeout,
    EmptyResponse,
    Parse,
    UnsupportedResponse,
    ProviderCode,
    AllProvidersFailed,
    Unexpected,
}
