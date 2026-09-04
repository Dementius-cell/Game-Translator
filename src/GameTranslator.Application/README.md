# GameTranslator.Application

Developer README для слоя вариантов использования и контрактов. Модуль координирует продуктовый сценарий, но не содержит WPF, Windows API, SQLite, Tesseract или сетевых реализаций.

## Ответственность

- Порты для capture, OCR, candidate detector, translation, cache, credentials, settings, profiles, hotkeys, diagnostics и updates.
- Сервисы профилей, import/export и совместимых миграций.
- Штатный pipeline: capture → Paddle candidate bounds через порт → bounded writing-system grouping → Tesseract crop OCR через `IOcrEngine` → cache/provider → per-region overlay snapshot.
- Multi-zone scheduling, readiness, stability, cancellation, source/revision authority, failure states и privacy-bounded diagnostics.
- Overlay positioning policy и translation grouping при сохранении raw OCR geometry.

## Граница зависимостей

`Application` ссылается только на `Domain` и уже принятые лёгкие DI abstractions. Конкретные адаптеры реализует `Infrastructure`, а отображение — `UI`. Контракты должны оставаться engine/provider-neutral.

Штатный route определяется `TranslationPipelineRunOptions.Default`. Legacy full-page route допустим только как явная diagnostic/compatibility option; detector/provider failure не включает скрытый fallback.

## Как изменять

Сохраняйте async cancellation, многозонную независимость, cache-first flow с TTL 30 дней и raw bounds для маски/диагностики. Изменение default, fallback, TTL, публичного контракта или product semantics требует проверки governance/ADR.

## Проверка

Запускайте focused Application и architecture tests. Для pipeline, cache, OCR/translator contracts, migration или multi-zone changes нужен полный suite, если нет документированной причины сузить gate. См. [AGENTS.md](AGENTS.md) и [ADR-030](../../docs/adr/README.md#adr-030).
