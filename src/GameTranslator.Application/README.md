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

Web-provider failures additionally distinguish DNS, connection, TLS, network and user-verification-required states. These additive failure kinds do not change provider selection, cache behavior or profiles. UI renders transport failures as warnings and verification/throttling as errors; diagnostics retain categories and safe messages, not response HTML or request URLs.

Single-block translation uses the same OCR text normalization as members of multi-block groups, in every grouping mode and orientation. Compact Japanese/CJK internal spaces are removed; Korean and Latin word boundaries remain. The raw OCR result, member geometry, words and recognition diagnostics are retained. This fixes the single-block early-return omission; it does not change recognition or concatenate separate groups.

Live translation retries DNS, connection, network, timeout and throttling failures on unchanged frames after 5/10/20 seconds (20-second cap), measured from failure completion. Provider RetryAfter and NextRetryAt can extend that delay. Success or source/group replacement resets the backoff. CAPTCHA, TLS and configuration failures require intervention and are not retried automatically. This scheduling is separate from the ADR-032 empty-OCR watchdog and does not probe network availability.

Hebrew translation grouping orders horizontal rows top-to-bottom and blocks/groups within each row right-to-left. Text inside each OCR block remains in logical order, including Latin and digit runs. Raw mask geometry is retained; nearby grouping does not merge distant bubbles. This scoped rule does not change other writing-system profiles or claim general character-level bidi reconstruction.

Arabic (ar/ara, including mapped regional tags) uses the same RTL block/group ordering as Hebrew. Synthetic tests retain connected Arabic text and embedded Latin, European digits and Arabic-Indic digits inside blocks, preserve raw bounds and keep distant bubbles separate. Persian and Urdu are excluded from supported scope by owner decision (2026-09-12); existing catalog/profile compatibility remains, but this Arabic-specific change does not enable them.

BingWeb uncached invocations have a 30-second total budget starting before the candidate translation queue, including credentials and response waiting. Deadline expiry is a Translation-stage Timeout and uses the existing transient retry schedule; caller cancellation stays cancellation. Late adapter results are discarded, and an uncooperative adapter retains its admission slot until it exits. The candidate live trace emits CandidateOcrCompleted on the next refresh independently of translation, including empty OCR. Pending Bing requests expose queue/credentials/response stages; after five seconds total waiting the trace records one warning per request and stage. These signals do not change the ADR-032 empty-OCR watchdog or assert HTTP 429 without a response.

For an exact candidate outer-bound match, internal detector-line edge jitter of at most one pixel is tolerated only when the captured candidate content still matches its source fingerprint and member count is unchanged. This retains the published overlay and avoids another OCR/cache pass when a panel outside the crop toggles. Changed source pixels, member count changes and larger internal edge shifts keep the normal invalidation path. The existing outer-geometry matching policy is unchanged.
