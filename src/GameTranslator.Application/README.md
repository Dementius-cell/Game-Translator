# GameTranslator.Application

Developer README для слоя вариантов использования и контрактов. Модуль координирует продуктовый сценарий, но не содержит WPF, Windows API, SQLite, Tesseract или сетевых реализаций.

## Ответственность

- Порты для capture, OCR, candidate detector, translation, cache, credentials, settings, profiles, hotkeys, diagnostics и updates.
- Сервисы профилей, import/export и совместимых миграций.
- Штатный pipeline: capture → Paddle candidate bounds через порт → bounded writing-system grouping → Tesseract crop OCR через `IOcrEngine` → cache/provider → per-region overlay snapshot.
- Multi-zone scheduling, readiness, stability, cancellation, source/revision authority, failure states и privacy-bounded diagnostics.
- Live watchdog повторяет только устойчивый candidate crop с завершённым пустым OCR через `5`, `10`, затем максимум `20` секунд; непустой OCR сбрасывает backoff, а с третьего пустого результата grouping подтверждается заново.
- Overlay positioning policy и translation grouping при сохранении raw OCR geometry.

## Граница зависимостей

`Application` ссылается только на `Domain` и уже принятые лёгкие DI abstractions. Конкретные адаптеры реализует `Infrastructure`, а отображение — `UI`. Контракты должны оставаться engine/provider-neutral.

Штатный route определяется `TranslationPipelineRunOptions.Default`. Legacy full-page route допустим только как явная diagnostic/compatibility option; detector/provider failure не включает скрытый fallback.

## Как изменять

Сохраняйте async cancellation, многозонную независимость, cache-first flow с TTL 30 дней и raw bounds для маски/диагностики. Изменение default, fallback, TTL, публичного контракта или product semantics требует проверки governance/ADR.

## Проверка

Запускайте focused Application и architecture tests. Для pipeline, cache, OCR/translator contracts, migration или multi-zone changes нужен полный suite, если нет документированной причины сузить gate. См. [AGENTS.md](AGENTS.md) и [ADR-030](../../docs/adr/README.md#adr-030).

Korean horizontal candidate OCR carries immutable crop-relative detector line hints through preprocessing. Infrastructure may report bounded line-coverage diagnostics; Application retains candidate source/revision and cancellation authority. Korean whitespace is preserved in comparison and translation-cache keys; existing stored cache data is not rewritten.

### Punctuation-only OCR filtering

Before translation grouping and cache lookup, standalone blocks containing only whitespace, Unicode punctuation, vertical bars or tildes are excluded from the translation projection. Raw OCR and its geometry remain available in diagnostics; these nonempty results do not activate the ADR-032 empty-OCR watchdog. Numbers, letters, and other symbols are retained. This also suppresses standalone punctuation dialogue (for example an ellipsis), but preserves punctuation attached to words. It is not a watermark classifier: letter/digit watermark noise remains an open quality issue.
