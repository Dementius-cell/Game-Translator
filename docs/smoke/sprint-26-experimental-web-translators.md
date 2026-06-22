# Sprint 26 Experimental Web Translator Smoke

## Scope

Validate the beta-only `WebAuto`, `GoogleWeb`, `BingWeb`, and `YandexWeb` translator providers without storing credentials.

These providers are explicitly experimental. They use public web translation endpoints and are approved only for Sprint 26 beta hardening so the OCR -> Translate -> Overlay path can be tested when official API credentials are unavailable.

## Constraints

- Do not make any experimental web provider the release default.
- Do not remove or replace the official `Google`, `Azure`, or `Yandex` providers.
- Do not store secrets for experimental web providers.
- Do not export any provider secret in JSON profiles.
- Treat endpoint failures, throttling, HTML/token changes, or HTTP 4xx/5xx responses as beta risks, not release-ready behavior.

## Manual Smoke

1. Start the WPF application.
2. Select or create a profile with at least one OCR zone.
3. Set `Provider` to `WebAuto`.
4. Confirm the credential status says the provider is experimental and does not use stored credentials.
5. Run `Recognize OCR` once and confirm text is detected.
6. Run `Run all zones` once as a manual diagnostic and confirm the provider can return translated text.
7. Click `Start live`.
8. Keep a subtitle or other captured text unchanged for at least 1 second.
9. Confirm the overlay updates with translated text without repeatedly pressing `Recognize OCR`.
10. Click `Stop live`.
11. Repeat with `GoogleWeb`, `BingWeb`, and `YandexWeb` when provider-specific diagnostics are needed.

## Provider Notes

- `WebAuto` is the recommended beta option. It tries `GoogleWeb`, then `BingWeb`, then `YandexWeb`.
- `GoogleWeb` is currently the simplest endpoint and should be the first direct provider to diagnose.
- `BingWeb` uses a short-lived web session token and refreshes it before translation, with one retry.
- `YandexWeb` may return `Session is invalid` if Yandex changes its browser session contract.
- Live translation waits for normalized OCR text to remain unchanged for 1 second before translating, which avoids translating partially typed subtitle lines.
- While live translation is waiting for stable OCR text, it keeps the previous overlay snapshot instead of replacing it with an empty overlay.

## Expected Result

- The full pipeline does not fail with `Translator credentials ... are not stored`.
- The provider returns translated text or a clear provider failure message.
- The overlay is populated only when translation succeeds, and live mode does not require repeated OCR button presses.
- Official providers still require Windows Credential Manager credentials.
