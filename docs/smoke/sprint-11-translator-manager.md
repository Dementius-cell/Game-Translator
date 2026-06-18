# Sprint 11 Translator Manager Smoke

## Goal

Confirm that Azure and Yandex providers are available through the translation provider seam and that the translator manager selects the provider configured by a profile.

## Steps

1. Build the solution in Release configuration.
2. Run the automated test suite.
3. Confirm that Azure, Yandex, and Google provider tests use fake HTTP responses instead of real credentials.
4. Confirm that translator manager tests select providers from `TranslatorSettings.Provider`.
5. Confirm that provider failures do not expose access tokens, API keys, IAM tokens, or subscription keys in exception text.

## Expected Result

- Azure and Yandex providers are registered as `ITranslatorProvider` implementations by Infrastructure composition.
- `TranslatorManager` selects the configured provider by provider id case-insensitively.
- Provider success, response mismatch, provider selection, unknown provider, and secret-redaction behavior are covered by tests.
- No real API key, access token, IAM token, or subscription key is persisted or required for Sprint 11 automated verification.
