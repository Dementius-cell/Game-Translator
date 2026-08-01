# ARCHITECTURE_DECISION_RECORDS.md

# Architecture Decision Records (ADR)

Проект: Game Translator

Версия: 1.0

Статус: Active

---

# Назначение документа

Данный документ фиксирует архитектурные решения проекта.

Каждое решение содержит:

* контекст;
* проблему;
* принятое решение;
* последствия;
* статус.

AI-агент обязан проверять ADR перед предложением архитектурных изменений.

Если решение помечено как ACCEPTED, менять его запрещено без явного согласования владельца проекта.

---

# ADR-001

## Использование C# как основного языка

Статус:
ACCEPTED

Дата:
2026-06-07

### Контекст

Проект ориентирован исключительно на Windows 11.

Требуется:

* работа с Windows API;
* overlay поверх игр;
* низкие задержки;
* долгосрочная поддержка.

### Решение

Использовать C# и .NET 9.

### Причины

* нативная интеграция с Windows;
* высокая производительность;
* удобная работа с WPF;
* развитая экосистема.

### Последствия

Python допускается только для исследований и прототипов.

Основной продукт всегда реализуется на C#.

---

# ADR-002

## Использование WPF

Статус:
ACCEPTED

Дата:
2026-06-07

### Контекст

Требуется стабильный desktop UI.

### Решение

Использовать WPF.

### Причины

* зрелость технологии;
* стабильность;
* совместимость с overlay;
* большое количество библиотек.

### Запрещено заменять на

* WinUI 3
* Avalonia
* MAUI
* Electron

без отдельного согласования.

---

# ADR-003

## Clean Architecture + MVVM

Статус:
ACCEPTED

### Решение

Использовать:

* Clean Architecture
* MVVM

### Причины

* независимость модулей;
* простота тестирования;
* расширяемость.

### Последствия

Нельзя создавать прямые зависимости между UI и Infrastructure.

---

# ADR-004

## Захват экрана через Windows Graphics Capture

Статус:
ACCEPTED

### Решение

Использовать Windows Graphics Capture.

### Причины

* официальное API Microsoft;
* высокая производительность;
* поддержка оконного режима;
* поддержка borderless fullscreen.

### Не использовать

* GDI Capture
* BitBlt
* DLL Hooking
* DirectX Injection

---

# ADR-005

## Двухдвижковая OCR архитектура

Статус:
ACCEPTED

### Решение

Использовать:

1. Windows OCR
2. Tesseract OCR

### Причины

Windows OCR:

* быстрее;
* хорошо работает с горизонтальным текстом.

Tesseract:

* поддерживает вертикальный текст;
* поддерживает японский и китайский.

### Последствия

Система не должна зависеть только от одного OCR движка.

---

# ADR-006

## Вертикальный японский и китайский текст

Статус:
ACCEPTED

### Решение

Использовать исключительно Tesseract OCR.

### Причины

Windows OCR нестабилен для вертикального текста.

### Последствия

Любые предложения использовать Windows OCR для вертикального текста отклоняются.

---

# ADR-007

## Архитектура переводчиков

Статус:
ACCEPTED

### Решение

Использовать Provider Pattern.

Интерфейс:

ITranslatorProvider

### Поддерживаемые провайдеры

* Google
* Azure
* Yandex

### Последствия

Добавление нового переводчика не должно требовать изменения ядра.

---

# ADR-008

## SQLite для кэша переводов

Статус:
ACCEPTED

### Решение

Использовать SQLite.

### Причины

* отсутствие сервера;
* надёжность;
* простота резервирования;
* высокая скорость.

### Не использовать

* PostgreSQL
* MySQL
* MongoDB

для MVP.

---

# ADR-009

## Хранение API-ключей

Статус:
ACCEPTED

### Решение

Использовать Windows Credential Manager.

Резерв:

DPAPI.

### Запрещено

* JSON
* SQLite
* XML
* INI
* Логи

### Причина

Предотвращение утечки ключей.

---

# ADR-010

## Overlay как отдельный модуль

Статус:
ACCEPTED

### Решение

Overlay выделяется в отдельную подсистему.

### Причины

* независимость рендеринга;
* упрощение поддержки;
* масштабируемость.

### Последствия

OCR и переводчики не должны содержать код отображения.

---

# ADR-011

## Многозонная архитектура

Статус:
ACCEPTED

### Решение

Все компоненты проектируются под множество OCR-зон.

### Причины

Однозонная архитектура потребует переписывания системы.

### Последствия

Новые функции обязаны работать с коллекцией зон.

---

# ADR-012

## Запрет внедрения в игровые процессы

Статус:
ACCEPTED

### Решение

Приложение не взаимодействует с памятью игры.

### Запрещено

* DLL Injection
* Memory Reading
* Memory Injection
* Process Hooking
* Driver Injection

### Причины

* совместимость;
* безопасность;
* отсутствие конфликтов с античитами.

### Последствия

Получение текста возможно только через Screen Capture + OCR.

---

# ADR-013

## Архитектура кэша

Статус:
ACCEPTED

### Решение

Двухуровневый кэш:

Level 1:
Memory Cache

Level 2:
SQLite Cache

TTL:
30 дней

### Причины

Снижение нагрузки на переводчики.

### Последствия

Перед вызовом API всегда выполняется проверка кэша.

---

# ADR-014

## Формат профилей

Статус:
ACCEPTED

### Решение

Использовать JSON.

Обязательное поле:

schemaVersion

### Причины

* простота обмена;
* читаемость;
* миграции версий.

### Последствия

Любое изменение структуры требует механизма миграции.

---

# ADR-015

## Порядок реализации проекта

Статус:
SUPERSEDED

### Решение

Разработка выполняется строго по Roadmap.

### Последствия

Запрещено начинать:

* вертикальный текст;
* Tesseract интеграцию;
* оптимизацию;

до появления рабочей цепочки:

Capture
→ OCR
→ Translate
→ Overlay

### Superseded By

ADR-017. Исторический порядок MVP сохранён, но текущие зависимости ведутся в GitHub Issues и не образуют абсолютную линейную блокировку.

---

# ADR-016

## Vertical OCR Translation Overlay Layout

Status:
SUPERSEDED

Date:
2026-07-04

### Superseded By

ADR-018. The vertical-height-first policy is replaced with centered symmetric expansion before font reduction.

### Context

Vertical Japanese and Chinese OCR blocks are usually narrow columns. Russian translations are often much longer than the original vertical text. The current fixed-bounds or center-expanded horizontal layout can either make the translated text unreadable or let it grow into a long horizontal strip that overlaps neighboring columns and comic bubbles.

Real no-UI GoogleWeb overlay smoke runs on 2026-07-04 showed this clearly on Japanese two-column and three-column vertical fixtures. OCR grouping was correct, but the translated overlay needed a vertical-source-specific placement policy.

### Options

1. Keep strict source bounds and reduce font size until the text fits.
2. Let translated text expand freely from the source center.
3. Use a vertical-source wrap box: start with a controlled width based on the original column width, wrap translated text inside it, then grow vertically around the original block center before reducing font size.
4. Use a bubble-wide comic layout solver that places translations inside the whole detected speech bubble or group.
5. Use caption mode outside the original text area.

### Decision

Use option 3 as the default vertical OCR translation layout.

For vertical OCR sources:

* keep mask bounds tied to the original OCR semantic bounds;
* separate mask bounds from translation text bounds;
* initialize translation text width from the source column width, defaulting to approximately two source widths;
* clamp translation width between a minimum readable width and the available OCR zone bounds;
* wrap translated text inside that box;
* grow the translation box upward and downward around the original source center before reducing font size;
* reduce font size only after width and vertical growth limits are reached;
* if text still cannot fit, keep deterministic clipping or ellipsis and surface the case as a debug/quality warning.

Comic bubble-wide placement remains a future enhancement, not the default. It may be implemented later as a higher-level layout solver for `NearbyBlocks` groups.

### Reasons

* It keeps original text masking precise and conservative.
* It avoids unreadably tiny text for long Russian translations.
* It prevents translated vertical text from becoming unbounded horizontal strips.
* It preserves the existing overlay layering model: OCR layer, mask layer, translation layer, debug layer.
* It can be implemented inside the overlay positioning service without changing OCR or translator interfaces.
* It is backward-compatible with existing profiles because it refines layout behavior for vertical sources instead of changing profile schema.

### Consequences

Positive:

* Vertical CJK translations become more readable by default.
* Mask placement remains stable and does not over-cover neighboring UI.
* Translation text can use nearby whitespace without pretending that the mask area is also the text layout area.

Negative:

* Translation text bounds may be wider and taller than the original OCR block.
* Dense comic pages may still need collision avoidance between neighboring translated blocks.
* Very long translations can still require font reduction or quality warnings.

Compatibility:

* No profile migration is required.
* Existing horizontal text behavior is unchanged.
* Existing overlay debug output should show both mask bounds and translation text bounds when they differ.

### Requires Migration

No.

### Approved

Project owner, explicit chat approval on 2026-07-04.

---

# ADR-017

## Governance Levels and Dependency-Based Delivery

Status:
ACCEPTED

Date:
2026-07-29

### Context

Ранние правила проекта одинаково блокировали опасные изменения, продуктовые решения и небольшие исправления. Это создало противоречия с semantic text grouping, ADR-016 и текущими Sprint 26 follow-up задачами: обычная реализация внутри принятого решения требовала повторного одобрения, а линейный roadmap блокировал независимую подготовительную работу.

### Decision

Использовать три уровня управления изменениями:

1. Hard Stop для безопасности игры, секретов, разрушительной потери данных и нарушения направлений архитектурных зависимостей.
2. Decision Record для breaking contract, platform replacement, schema/data migration, KPI/default-semantic change и новой продуктовой политики.
3. Normal Delivery для bug fix, тестов, additive-compatible изменений и реализации внутри scope принятого ADR.

Принятый ADR является разрешением на normal implementation, тестирование и исправления внутри его stated scope. Новое решение владельца требуется только при расширении scope.

Roadmap задаёт направление, а GitHub Issues являются текущим источником зависимостей. Независимая работа допускается при закрытых либо явно отложенных собственных зависимостях.

Quality Gates выбираются по impact; build, relevant tests, архитектурные границы и секреты обязательны всегда, а неприменимые gates фиксируются с причиной.

### Consequences

Positive:

* Сохраняются запреты на небезопасное взаимодействие с играми и утечки секретов.
* Реализация ADR-016 и другие bounded fixes не требуют повторного согласования каждой детали.
* Решения с большим blast radius остаются reviewable через ADR и Change Request.
* GitHub Issues становятся живой картой зависимостей вместо исторической нумерации документа.

Negative:

* Агент обязан аккуратно классифицировать изменение и фиксировать evidence.
* Команда должна поддерживать issue dependencies и актуальные acceptance criteria.

Compatibility:

* Нет изменения production-кода, формата профилей или runtime behavior.
* ADR-015 superseded только для правила строгой линейной последовательности; его историческая информация сохраняется.

### Requires Migration

No.

### Approved

Project owner, explicit chat approval on 2026-07-29.

---

# ADR-018

## Centered Translation Fit Before Font Reduction

Status:
ACCEPTED

Date:
2026-07-29

### Context

ADR-016 established separate mask and translation bounds for vertical OCR. Evidence from a real comic page showed that its vertical-growth-first policy and a horizontal right-overflow dampening heuristic could make translation bounds wider than needed and shift them left of the original text. The project owner clarified the target behavior: Google Translate-style translation bounds should begin from the source text, remain centered, and grow only when measured content requires it.

### Options

1. Keep the ADR-016 vertical-height-first policy and the horizontal dampening heuristic.
2. Reduce font size before expanding translation bounds.
3. Start from a source-based frame, expand the translation bounds symmetrically around the source center while keeping the preferred font size, and reduce the font only after expansion reaches its available limit.
4. Treat manually annotated bubble bounds as persisted per-zone profile fields.

### Decision

Use option 3 for all expanded translation layouts.

* Keep semantic OCR/source bounds as the mask anchor; mask geometry and translation geometry remain separate.
* Start translation bounds from a minimally padded source-based frame. For vertical sources, start width is the source width multiplied by the session multiplier, whose default is `2.0`.
* Use actual WPF text measurement and wrapping to determine whether the current frame fits at the preferred font size.
* When content does not fit, expand the translation frame symmetrically around the source center while space permits. At a capture boundary or collision, use the remaining valid space before reducing the font size.
* Reduce font size only after centered expansion cannot provide a fitting frame. Surface clipping or unresolved collision as a deterministic debug/quality warning.
* Remove horizontal semantic-bounds right-overflow dampening. Bounds must not shift left merely because their measured translation width exceeds the source bounds.
* Expose the vertical width multiplier as a session-only debugging control. It must not be persisted in profiles or other user settings.
* Treat hand-drawn blue evidence frames as calibration guidance for the initial padded frame, not as saved hard constraints or a new profile schema.

### Reasons

* Centered growth avoids the visible left drift found on real comic dialogue.
* Source-based initial frames keep short translations compact instead of allocating a fixed wide strip.
* Preserving the preferred font size improves legibility for Russian translations of CJK text.
* The session-only multiplier enables practical evidence tuning without a compatibility or persistence change.

### Consequences

Positive:

* Horizontal and vertical translations use the same fit priority: measured fit, centered expansion, then font reduction.
* Translation bounds remain anchored to the source center unless a real boundary or collision makes that impossible.
* The owner can tune vertical starting width during debug runs without changing saved profiles.

Negative:

* Placement tests and evidence must cover the new fit order and centered expansion behavior.
* Dense scenes can still reach collision or capture-region limits and emit fit warnings.

Compatibility:

* No profile migration or persisted-setting migration is required.
* OCR and translation contracts are unchanged.
* ADR-016 remains historical context but is superseded for overlay fit policy.

### Requires Migration

No.

### Approved

Project owner, explicit chat approval on 2026-07-29.

---

# ADR-019

## Optional OCR Word Geometry and Quality Metadata

Status:
ACCEPTED

Date:
2026-07-30

### Context

The approved overlay evidence shows that accurate source geometry is required before translation bounds can reproduce the expected centered, compact placement. The current OCR contract exposes only already-grouped line blocks, so downstream diagnostics and future layout-aware grouping cannot distinguish individual recognition quality or geometry. Issue #38 tracks this gap.

### Options

1. Keep line-level OCR blocks only and infer word geometry from text or line rectangles.
2. Replace the existing OCR result contract with a new mandatory geometry model.
3. Add optional word-level metadata to the existing OCR result while preserving current blocks and callers.
4. Persist confidence thresholds, word geometry, or preprocessing decisions in profiles before validating their value.

### Decision

Use option 3.

* Add an additive, optional representation of recognized words: source text, source bounds, nullable confidence, and recognition-pass identity.
* Preserve existing `OcrTextBlock`, `OcrResult`, and `IOcrEngine` behavior for callers that do not consume the metadata.
* Populate this metadata from Tesseract when the engine returns it. Engines that cannot supply a field must return an empty collection or a nullable value rather than synthesize data.
* Treat Tesseract confidence as engine-local diagnostic data, not a cross-engine quality score or an automatic rejection policy.
* Do not introduce default suppression, retries, preprocessing fallback, profile fields, or persistence changes in this decision. Those remain separate B2/B3 work.

### Reasons

* Word bounds make it possible to validate and improve semantic grouping against the approved real-image evidence.
* An optional additive model avoids a migration and keeps Windows OCR and existing tests valid when metadata is unavailable.
* Deferring policy decisions prevents a diagnostic signal from silently changing OCR output quality or latency.

### Consequences

Positive:

* Future layout-aware OCR can group actual recognized words instead of inferring geometry from line boxes.
* Evidence tooling can display confidence and source geometry without changing overlay placement contracts.
* Existing engines and consumers continue to work with no metadata.

Negative:

* Consumers must treat metadata as optional and engine-specific.
* The implementation needs focused contract and Tesseract iterator tests.

Compatibility:

* No profile, persistence, public overlay, or default OCR behavior migration is required.
* The current line-level OCR blocks remain the compatibility surface.

### Requires Migration

No.

### Approved

Project owner, explicit chat approval on 2026-07-30.

---

# ADR-020

## Derive Tesseract Layout Passes from Existing Translation Grouping Mode

Status:
ACCEPTED

Date:
2026-07-30

### Context

The owner-approved target requires OCR to respect zone layout: sparse menu text, a dialog block, and comic text that is detected before line-level recognition. Tesseract currently uses one single-block mode for every zone. Adding a persisted layout field would alter profile schema and require migration, while existing profiles already express the intended translation grouping shape.

### Options

1. Keep one single-block Tesseract mode for all zones.
2. Add a persisted OCR layout-mode field to every profile zone.
3. Derive a runtime layout mode from the existing translation grouping mode and keep a direct OCR request on its compatible automatic behavior unless the pipeline supplies the derived mode.
4. Run every available Tesseract pass for every frame and choose a result automatically.

### Decision

Use option 3.

* `BlockByBlock` maps to `Menu`: Tesseract sparse-text detection (`PSM 11`).
* `WholeZone` maps to `Dialog`: the existing horizontal/vertical single-block recognition (`PSM 6` or its vertical counterpart).
* `NearbyBlocks` maps to `Comic`: sparse detection followed by recognition of each detected text-line crop.
* The OCR request gains an additive runtime layout field whose direct-call default remains `Auto`, preserving existing callers that do not originate in the pipeline.
* Tesseract reports the actual pass identity in word metadata. Comic results preserve detection and line-refinement metadata for diagnostics while emitting refined line blocks in reading order.
* Comic source blocks are constructed from line-refinement word bounds whose Tesseract confidence is at least `50`. All lower-confidence words remain in `OcrResult.Words` diagnostics but cannot expand a semantic source/mask rectangle. When a detected crop has no qualifying word, it produces no semantic block rather than falling back to a broad line rectangle.
* This decision does not add profile persistence, retry/fallback preprocessing, or a `tessdata_best` policy. Those remain later work.

### Reasons

* Existing grouping modes provide a compatible user-facing signal without migration or a new configuration control.
* Sparse detection prevents a full comic page or menu from being forced into a false uniform text block.
* Per-line refinement gives the OCR engine a smaller, better-defined recognition target while retaining source-relative geometry.
* A conservative confidence floor prevents decorative strokes and line-layout noise from expanding a mask over a large part of the scene while keeping the raw diagnostic signal available.
* Explicit pass identifiers make evidence reviewable and prevent hidden multi-pass behavior.

### Consequences

Positive:

* Pipeline OCR chooses a layout-aware Tesseract pass without a profile schema change.
* Comic output can preserve detection and refinement geometry for B2 diagnostics and future grouping improvements.
* Direct Application callers preserve current automatic behavior until they opt into a layout mode.

Negative:

* Existing `TranslationGroupingMode` now affects Tesseract segmentation as well as translation grouping.
* Comic recognition can take additional CPU time because it runs a refinement pass for every detected line.

Compatibility:

* No profile migration or UI-setting persistence change is required.
* `IOcrEngine`, line-level OCR blocks, and Windows OCR remain supported.

### Requires Migration

No.

### Approved

Project owner, explicit chat approval on 2026-07-30.

---

# ADR-021

## Tesseract Fallback Only for Empty Comic Semantic Output

Status:
ACCEPTED

Date:
2026-07-30

### Context

ADR-020 makes comic source geometry conservative: only refinement words with adequate Tesseract confidence form semantic blocks. Real Chinese evidence correctly suppresses broad low-quality masks but leaves some genuine dialogue crops with no output because sparse detection cannot provide a reliable line to refine. The prior vertical single-block pass can still recover text in some such isolated zones.

### Options

1. Keep an empty comic result whenever sparse refinement has no reliable semantic blocks.
2. Run the old whole-zone single-block pass for every comic request and choose a result by an opaque score.
3. Run one orientation-aware whole-zone single-block fallback only when comic sparse/refinement produces no reliable semantic block at all.
4. Add unconditional preprocessing and `tessdata_best` retries for every CJK request.

### Decision

Use option 3.

* Keep sparse detection and line refinement as the normal comic path.
* Only when that path emits zero semantic blocks, run one Tesseract single-block pass using the resolved orientation.
* Form fallback geometry with the same reliable-word rule and append its pass-labelled raw word metadata for diagnostics.
* Do not run a fallback after any reliable comic semantic block, and do not introduce profile persistence, inversion, adaptive binarization, upscale, or `tessdata_best` in this increment.

### Reasons

* Empty semantic output is a clear, observable low-quality signal that avoids hidden per-frame score selection.
* The fallback can recover a missed isolated subtitle or bubble while preserving the safer comic-first default.
* Reusing the existing word-confidence geometry rule prevents a fallback from restoring the broad-mask failure.

### Consequences

Positive:

* CJK comic zones that sparse detection completely misses have a bounded recovery path.
* Evidence shows the fallback identity and all raw word metadata.

Negative:

* A failed comic zone can incur one additional OCR pass.
* Partial but wrong high-confidence comic output is intentionally not retried; preprocessing and model-quality policies remain later B3 increments.

Compatibility:

* No profile migration, persistence, or `IOcrEngine` change is required.
* Windows OCR behavior is unchanged.

### Requires Migration

No.

### Approved

Project owner, explicit chat approval on 2026-07-30.

---

# ADR-022

## Empty-Only Quality Upscale Fallback for CJK/Thai OCR

Status:
ACCEPTED

Date:
2026-07-30

### Context

B3 evidence compared bounded preprocessing candidates for Thai and vertical Chinese comic crops after ADR-019 word metadata, ADR-020 layout-aware Tesseract passes, and ADR-021 empty comic fallback were implemented. Thai calibration crops already recognized reliably with the existing Tesseract `tha` path, so unconditional preprocessing would add latency without observed benefit. The Chinese S8/C6 crop still produced no reliable semantic group until the frame was upscaled before comic sparse detection and line refinement.

Tesseract documentation supports choosing segmentation mode by layout and notes that image preprocessing can help difficult inputs, while higher-accuracy `tessdata_best` models are slower than `tessdata_fast`.

### Options

1. Keep ADR-021 only and leave CJK/Thai empty comic crops unresolved.
2. Enable profile or default preprocessing for all CJK/Thai OCR requests.
3. Add one quality upscale retry only after the existing path emits no semantic OCR blocks.
4. Add adaptive thresholding, inversion, deskew, and `tessdata_best` selection in one combined production change.

### Decision

Use option 3.

* Keep normal OCR unchanged for successful requests.
* Only for Tesseract CJK/Thai languages, and only when the current recognition path emits zero semantic text blocks, run one `2x` bilinear quality-upscale retry.
* For comic layout, run the quality-upscale retry through sparse detection and line refinement, then map all word and semantic bounds back to the original frame.
* For non-comic layouts, run one quality-upscaled retry with the already selected page segmentation mode and map bounds back to the original frame.
* Keep pass identifiers explicit: quality-upscaled comic detection and refinement must be visible in OCR word diagnostics.
* Do not add profile fields, persisted defaults, inversion, adaptive Otsu/Sauvola, deskew, or `tessdata_best` selection in this decision.

### Reasons

* Empty semantic output is a clear and bounded low-quality signal.
* The retry reproduces the only preprocessing candidate that improved the Chinese C6 evidence without making every OCR request slower.
* Bounds mapping keeps overlay source geometry in the original capture coordinate space.
* Deferring thresholding, inversion, deskew, and model selection avoids stacking multiple hard-to-debug OCR policies in one increment.

### Consequences

Positive:

* CJK/Thai empty OCR cases have a bounded recovery path with visible diagnostics.
* The Chinese S8 baseline improves from `5/6` to `6/6` non-empty semantic crop results in local evidence.

Negative:

* A failed CJK/Thai request can incur additional OCR work.
* Partial but wrong non-empty output is intentionally not retried by this policy.

Compatibility:

* No profile, persistence, or public contract migration is required.
* Windows OCR behavior is unchanged.

### Requires Migration

No.

### Approved

Project owner, explicit chat approval on 2026-07-30.

---

# ADR TEMPLATE

Использовать для новых решений.

---

# ADR-XXX

Название решения

Статус:
PROPOSED | ACCEPTED | REJECTED | SUPERSEDED

Дата:
YYYY-MM-DD

## Контекст

Описание проблемы.

## Варианты

Вариант 1

Вариант 2

Вариант 3

## Решение

Выбранное решение.

## Причины

Почему оно выбрано.

## Последствия

Плюсы.

Минусы.

Влияние на совместимость.

## Требуется миграция

Да / Нет

## Одобрено

Имя владельца проекта
