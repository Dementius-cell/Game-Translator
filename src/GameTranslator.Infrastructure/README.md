# GameTranslator.Infrastructure

Developer README для concrete external adapters. Модуль реализует Application-порты и изолирует Windows, persistence, OCR runtimes и translation services.

## Ответственность

- Windows Graphics Capture и Direct3D helpers.
- Windows OCR, Tesseract и управление OCR language packs.
- GPU Paddle text detector через упакованный CPython worker `Ocr/paddle_text_detector_worker.py`.
- SQLite translation cache, JSON profiles/settings и Windows Credential Manager.
- Официальные `Google`, `Azure`, `Yandex`; отдельно выбираемые diagnostic `GoogleWeb`, `BingWeb`, `YandexWeb`.
- Squirrel update adapter.

Paddle worker — внутренний packaged adapter этого модуля, а не отдельный product module и не recognizer. Каждый принятый crop распознаёт Tesseract; Windows OCR остаётся самостоятельной поддерживаемой возможностью.

## Граница зависимостей

`Infrastructure` ссылается на `Application` и `Domain`, но не на `UI`. Регистрация выполняется через `InfrastructureServiceModule`, который UI загружает по composition seam без прямого project reference.

Credentials сохраняются только в Windows Credential Manager. Web providers не используют сохранённые secrets, не становятся default и не образуют fallback chain.

## Локальные данные

- profiles/settings/cache paths задаются UI composition root под **%LOCALAPPDATA%\GameTranslator**;
- Tesseract ищет `tessdata` рядом с executable;
- Paddle worker/runtime/model находятся в `candidate-detector` packaged output.

## Проверка

Запускайте focused adapter tests и architecture tests. Для capture/OCR/provider/cache/profile/credential changes добавляйте проход через соответствующий Application contract; live network calls и локальные language packs не должны становиться обязательными CI-зависимостями. См. [AGENTS.md](AGENTS.md) и [руководство пользователя](../../docs/user-guide.md).

Для подходящих корейских горизонтальных candidate lines Tesseract использует RawLine отдельно на каждом detector member, сохраняя raw OCR bounds. Геометрические ограничения выбирают режим распознавания и не отбрасывают остальные crops. Числовая line-coverage диагностика отражает пропуски, но не гарантирует правильность распознанных букв.

## Web translator availability

WebTranslatorAvailability classifies actual GoogleWeb, YandexWeb and BingWeb HTTP traffic without ping, background probes or provider fallback. DNS, connection, TLS, generic network failure, timeout, HTTP 429, explicit challenge-page markers/redirects, and unexpected HTML are distinguished. Transport exception messages and provider error bodies are omitted from user-facing exceptions. Response inspection is bounded to a 64 KiB prefix for challenge markers; existing HTTP buffering is unchanged. JSON text mentioning CAPTCHA is not a challenge.

Bing keeps its existing cooldown policy. Google/Yandex report Retry-After on HTTP 429 without a new cooldown policy. Caller cancellation remains cancellation. Successful subsequent requests use the normal path; browser verification is not assumed to authorize the application. This is request-based availability evidence, not proof of global internet connectivity or a background health monitor. Challenge recognition is heuristic and has synthetic coverage; a real Yandex challenge has not been validated.

Explicit local diagnostics: GAME_TRANSLATOR_TEST_FAULT=timeout, 429 or captcha wraps only the three web providers. A 45-second window starts at their first uncached invocation, sends no HTTP and creates no synthetic successful response. Then the original provider is used. Cancellation remains cancellation; official providers are untouched. Invalid/unset values disable injection. Restart the process to reset the window; cache hits can bypass invocation. This validates application handling, not actual HTTP timeout, CAPTCHA or service throttling.

BingWeb service-token bootstrap is shared per endpoint and owned by the provider lifetime. Candidate cancellation stops only that caller's wait; the bounded HTTP operation can complete and warm the token cache for later candidates. Failed/cancelled/expired bootstrap tasks are replaced on the next eligible invocation, respecting existing provider cooldown. Disposing the provider cancels bootstrap; the diagnostic wrapper forwards disposal. Shared credentials HTTP evidence is attributed once to a waiter observing completion, so a cancelled candidate does not falsely cancel the shared network attempt. If all waiters leave and none later observe that flight, its final HTTP evidence is not attached to a candidate report; active waiters now expose service-initialization progress independently of final HTTP evidence. No translated text is produced for a cancelled candidate.

Bing uses a dedicated singleton HTTP client with a 5-second connection-establishment timeout. A shared bootstrap GET gets at most one retry after a transport timeout (nested TimeoutException), subject to the existing two-timeout cooldown. Each HTTP attempt retains its 15-second bound and independent diagnostic record; the Application 30-second request budget remains authoritative. HTTP errors, CAPTCHA, DNS/TLS failures, caller cancellation, ordinary request timeouts and translation POSTs are not retried inside the adapter. Other providers retain their existing client configuration.
