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
