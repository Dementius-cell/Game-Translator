# Sprint 26 Experimental Web Translator Smoke

## Scope

Validate the beta-only `GoogleWeb`, `BingWeb`, and `YandexWeb` translator providers without storing credentials.

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
3. Set `Provider` to one of `GoogleWeb`, `BingWeb`, or `YandexWeb`.
4. Confirm the credential status says the provider is experimental and does not use stored credentials.
5. Run `Recognize OCR` once and confirm text is detected.
6. Run `Run all zones` once as a manual diagnostic and confirm the provider can return translated text.
7. Click `Start live`.
8. Keep a subtitle or other captured text unchanged for at least 1 second.
9. Confirm the overlay updates with translated text without repeatedly pressing `Recognize OCR`.
10. Click `Stop live`.
11. Repeat with the other two web providers when provider-specific diagnostics are needed.

## Provider Notes

- Every web provider is selected directly. No automatic provider fallback is available.
- `GoogleWeb` uses the direct Google GTX endpoint and remains subject to throttling or endpoint changes.
- `BingWeb` obtains the current translator page token and performs one direct translation request without retrying through another provider. Each Bing HTTP request has a provider-local 15-second timeout; the shared application `HttpClient` timeout is unchanged.
- The first consecutive Bing timeout is a yellow warning. The second opens a 60-second pause and is shown as an error. HTTP 429 opens the pause immediately and honors a valid `Retry-After` value; a successful translation resets the timeout sequence.
- Bing timeout/throttle handling does not switch providers. When a transient empty candidate snapshot would otherwise clear an already visible overlay, the previous snapshot remains visible until a replacement is available or the candidate disappears.
- `YandexWeb` uses the direct Android-form translation request without retrying through another provider.
- Live translation waits for normalized OCR text to remain unchanged for 1 second before translating, which avoids translating partially typed subtitle lines.
- While live translation is waiting for stable OCR text, it keeps the previous overlay snapshot instead of replacing it with an empty overlay.

## Expected Result

- The full pipeline does not fail with `Translator credentials ... are not stored`.
- The provider returns translated text or a clear provider failure message.
- Bing timeouts and throttling become visible without waiting for the shared HTTP timeout, and diagnostics contain provider/status/timing state without raw provider response text.
- The overlay is populated only when translation succeeds, and live mode does not require repeated OCR button presses.
- Official providers still require Windows Credential Manager credentials.
