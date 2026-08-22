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

# ADR-023

## Progressive Per-Region Overlay Delivery and GPU Candidate-Detector Evaluation

Status:
ACCEPTED

Date:
2026-08-01

### Context

The prior frame-oriented flow can delay every overlay until OCR, translation, and layout finish for all detected text on the frame. This fails the product scenario where one simple subtitle or dialogue region disappears after three to four seconds while other concurrent regions have difficult comic geometry.

Track D evidence established that current full-page Tesseract geometry is not reliable enough to act as a universal candidate detector. The bounded 2x experiment improved pre-grouping candidate recall only from 6/10 to 7/10 on S9 and from 1/6 to 3/6 on S10, while substantially increasing outside candidates and latency. PaddleOCR CPU research is not a runtime answer: its full-page Windows CPU latency was measured in seconds, not real-time budgets.

The owner requires progressive delivery after text stabilizes, while preserving manual OCR-zone capture scope. The target benchmark floor is a current Windows system with a CPU of at least six cores and an NVIDIA RTX 3060 with 8 GiB VRAM.

### Options

1. Keep frame-level batch delivery and wait for every region before rendering any overlay.
2. Add more full-frame Tesseract retries and continue to treat the frame as one unit of work.
3. Deliver confirmed source regions independently and research a GPU candidate detector that proposes transient regions inside an existing OCR zone.
4. Integrate PaddleOCR or another third OCR runtime immediately as the production detector.

### Decision

Use option 3.

* After a source region stabilizes, its OCR, cache lookup, translation, and overlay delivery form an independent cancellable work item.
* A completed simple region renders immediately; it must not wait for unresolved regions from the same captured frame.
* Only a region whose source identity changes or disappears is cancelled. Unchanged completed regions remain valid until their own identity changes or the overlay lifecycle removes them.
* Saved OCR zones remain the manual user-selected capture scope. Candidate detection may create transient per-frame source regions only inside that scope and must not change profile persistence.
* Candidate-detector research must benchmark candidate recall, outside-candidate noise, cold and steady latency, CPU/GPU utilization, VRAM, and end-to-end per-region delivery on at least an RTX 3060 8 GiB system with a six-core-or-better CPU.
* Text visible for three to four seconds is the product scenario for end-to-end evidence. Numerical P50/P95 acceptance thresholds are deliberately deferred until the first benchmark establishes a credible baseline.
* Windows OCR and Tesseract remain mandatory product capabilities. This ADR does not approve a third OCR runtime, alter `IOcrEngine`, replace manual OCR zones, or add unconditional full-frame retries.

### Reasons

* Per-region completion directly prevents difficult geometry from hiding an already-ready simple translation.
* Region-scoped cancellation avoids spending work on text that has changed or disappeared without discarding useful completed overlays.
* GPU research is justified by the approved hardware baseline, but the detector is evaluated by measured recall and latency rather than assumed to be faster.
* Keeping the candidate detector separate from text recognition preserves the existing engine-neutral OCR seam and avoids accepting the PaddleOCR CPU benchmark as a product dependency.

### Consequences

Positive:

* A simple subtitle or dialogue block can appear while complex comic regions are still processing.
* Candidate-detector experiments have an explicit hardware floor and measurable acceptance surface.
* Existing manual zones, OCR engines, profile compatibility, and screen-capture-only safety rules remain intact.

Negative:

* Pipeline and overlay lifecycle implementation must become region-aware and require cancellation, ordering, and multi-region regression tests.
* Candidate detections can still be rejected when geometry is insufficient; progressive delivery does not guarantee a translation for every region.
* The latency and packaging cost of any GPU detector remains unknown until benchmarked.

Compatibility:

* No profile migration or public OCR contract change is approved by this ADR.
* Current full-page Tesseract grouping and empty-only fallback behavior remain unchanged until future evidence authorizes a separate change.

### Requires Migration

No.

### Approved

Project owner, explicit chat approval on 2026-08-01.

---

# ADR-024

## GPU Candidate Detector as a Gated Transient-Region Provider for Tesseract OCR

Status:
SUPERSEDED by ADR-025 on 2026-08-02

Date:
2026-08-02

### Context

Full-page Tesseract geometry did not meet the comic OCR quality bar: the owner S9 and S10 pages matched zero source regions before research-only recovery. Research using a GPU PaddleOCR detector followed by the existing Tesseract crop recognizer reached the owner geometry target and met the held-out Japanese crop-recognition evidence threshold. The detector remains a third runtime, so ADR-023 required a separate owner-approved Change Request before any product pilot.

### Decision

Permit a narrowly gated, opt-in production pilot in which a GPU PaddleOCR text detector proposes transient candidate bounds only inside an existing saved OCR zone. The existing Tesseract implementation remains the recognizer for every accepted candidate crop.

The pilot remains disabled by default until release validation. It preserves ADR-023 progressive per-region publication and source-identity-scoped cancellation. When the detector is unavailable, hardware is unsupported, packaging validation fails, or candidate quality checks fail, it publishes no detector-derived overlay. It must not silently broaden into a full-frame retry.

### Boundaries

* Windows OCR and Tesseract remain mandatory capabilities; Tesseract remains the only recognizer in the pilot path.
* Do not change `IOcrEngine`, saved OCR-zone semantics, profile schema, translation-cache contract, or global cancellation behavior.
* Detector candidates are transient, remain inside the saved OCR zone, and never become profile data.
* Do not use the research transitive merge. Any production grouping is bounded, deterministic, and must meet the gates below.
* Do not automatically enable the pilot on unsupported hardware. The minimum pilot hardware is Windows, a CPU with at least six cores, and an NVIDIA RTX 3060 with 8 GiB VRAM.
* No unconditional full-frame 2x retry is permitted.

### Acceptance Thresholds

1. Owner golden geometry: S9 10/10 and S10 6/6 accepted source regions after filtering, with zero retained extras, no overlap, no out-of-page bounds, and no source-mask regression.
2. Held-out Japanese manga corpus: speech-region anchor recall >= 95%, non-empty Tesseract crops >= 98% of mapped regions, and micro CER <= 15%.
3. Vertical dense-text geometry stress set: strict one-to-one recall >= 99%, and no candidate may cover more than one tight reference line after the guard.
4. RTX 3060 8 GiB benchmark: warm detector P95 <= 250 ms; cold initialization plus first detector result P95 <= 2.0 s; end-to-end P95 to a ready simple-region overlay <= 3.0 s for the three-to-four-second product scenario; incremental detector VRAM <= 2.5 GiB.
5. Deterministic cancellation, progressive publication, detector-unavailable behavior, packaging and license validation, headless S9/S10 integration, focused and full tests, Release build, and documentation checks pass.

### Consequences

Positive:

* The product can evaluate materially stronger candidate geometry without replacing its required OCR recognizer.
* The pilot has explicit quality, latency, hardware, and rollback boundaries.

Negative:

* The runtime, model packaging, and GPU resource cost require release evidence before the pilot can be enabled.
* Candidate false positives remain a product risk and must be rejected before overlay publication.

Compatibility:

* No profile migration, public OCR contract change, game-process interaction, or persisted detector state is allowed.
* Existing manual zones remain the capture and compatibility boundary.

### Requires Migration

No.

### Approved

Project owner, explicit chat approval of ADR-024 and Issue #42 thresholds on 2026-08-02.

---

# ADR-025

## Relax the Opt-In GPU Candidate-Detector Cold-Start Gate

Status:
ACCEPTED

Date:
2026-08-02

Supersedes:
ADR-024, only for its cold initialization plus first detector result threshold.

### Context

The ADR-024 research path materially improves full-page comic candidate geometry while retaining Tesseract as the recognizer: the bounded candidate flow reached S9 10/10 and S10 6/6 with no retained extras. On the research RTX 3080 host, the actual cached Python/Paddle worker first detector result took 3.72-3.94 seconds. The previous 2.0-second cold gate therefore rejected a quality-positive opt-in pilot solely because Python/Paddle import and detector initialization occur before the first result.

### Decision

For the opt-in, disabled-by-default pilot only, replace ADR-024 acceptance threshold 4's cold initialization plus first detector result P95 from <= 2.0 seconds to <= 5.0 seconds.

Cold timing starts when the packaged worker process is launched and ends when it returns its first detector result with model files already present. Downloading models is not part of an accepted cold measurement. The warm detector P95 <= 250 ms, the ready simple-region overlay P95 <= 3.0 seconds for the three-to-four-second product scenario, and every other ADR-024 acceptance threshold remain unchanged. The product must not block its UI while the opt-in worker initializes.

### Boundaries

* This decision does not enable the pilot by default, add a UI setting, change `IOcrEngine`, add profile data, or permit a full-frame retry.
* Windows OCR and Tesseract remain mandatory; Tesseract remains the recognizer for every accepted candidate crop.
* The revised timing must still be measured on the minimum RTX 3060 8 GiB hardware floor, with packaging, license, checksum, offline-install, rollback, quality, mask, cancellation, and Release validation evidence.
* Python/Paddle startup optimization remains deferred final-stage work. It is not a reason to weaken the 5.0-second gate or automatically expose the pilot.

### Consequences

Positive:

* The measured 3.72-3.94-second research cold result is eligible for the opt-in pilot's remaining gates instead of being rejected by an unrealistically strict startup budget.

Negative:

* A first detector result can take up to five seconds; the pilot must remain opt-in, asynchronous, and clearly bounded from the normal product path.
* The current RTX 3080 result does not satisfy the separate RTX 3060 evidence requirement.

### Requires Migration

No.

### Approved

Project owner, explicit chat approval on 2026-08-02: "подтверждаю изменение с <=2 с до <=5 приемлемым для opt-in пилота".

---

# ADR-026

## Quality-First Target Architecture: GPU Detector to Tesseract Crop Recognition

Status:
ACCEPTED

Date:
2026-08-10

Related Change Request:
[#43](https://github.com/Dementius-cell/Game-Translator/issues/43)

### Контекст

Legacy full-page Tesseract OCR/grouping не достиг geometry quality для owner comic references: `S9` matched `0/10`, а `S10` matched `0/6` semantic groups. Дальнейшее развитие этого пути как целевой архитектуры не имеет подтверждённого quality basis.

Research-only chain из GPU Paddle detector, bounded grouping и существующего Tesseract crop recognition достигла owner geometry `S9 10/10` и `S10 6/6`. Research Tesseract crop filter оставил zero retained extras. Это является quality evidence для target direction, но не доказательством production enablement.

ADR-024 и ADR-025 уже разрешают только узкий opt-in detector pilot. Они сохраняют Tesseract recognizer, manual saved OCR zone, disabled-by-default state и cold initialization plus first detector result P95 `<= 5.0 s`. Cache-free packaged `r8-pruned` не выдал `ready` ни за `5 s`, ни за diagnostic `10 s`; этот No-Go не разрешает ослабить ADR-025 или изменить default path.

### Варианты

1. Продолжать развивать legacy full-page Tesseract OCR/grouping как основной путь к comic geometry quality.
2. Использовать GPU Paddle runtime как recognizer и заменить Tesseract crop recognition.
3. Сохранить legacy и detector path как равноправные долгосрочные target architectures без quality priority.
4. Сделать quality-first target chain: GPU detector -> bounded grouping -> existing Tesseract crop recognition -> per-region overlay; legacy full-page путь сохраняется только как временный fallback до закрытия gates.

### Решение

Предлагается вариант 4.

Target chain работает только внутри существующей manual saved OCR zone:

```text
saved OCR zone
  -> GPU Paddle detector (transient candidate bounds)
  -> bounded deterministic grouping
  -> existing Tesseract crop recognition
  -> quality/text filter
  -> independent cache -> translation -> per-region overlay
```

* Paddle может предлагать только transient candidate bounds; его output не является финальным OCR text.
* Grouping должен быть bounded и deterministic, без transitive merge; принимаются только in-zone, non-overlapping, in-page candidates.
* Tesseract остаётся обязательным и единственным recognizer для каждого принятого candidate crop. Windows OCR и Tesseract остаются обязательными product capabilities.
* OCR, cache, translation и overlay каждого допустимого candidate остаются независимой region-scoped цепочкой согласно ADR-023.
* Legacy full-page OCR/grouping не развивается как целевой путь. До закрытия всех release gates он остаётся временным legacy fallback; эта запись не меняет текущий runtime selection, default или UI.
* Pilot остаётся disabled by default. Не меняются profile schema, saved-zone semantics, `IOcrEngine`, translation-cache contract, global cancellation или full-frame retry policy.
* Не удалять legacy implementation до отдельного owner decision после закрытия gates.

### Gates до любого enablement

Все ADR-024/ADR-025 gates сохраняются без ослабления, включая:

1. Owner golden geometry: `S9 10/10`, `S10 6/6`, zero retained extras, no overlap, no out-of-page bounds и no source-mask regression.
2. Held-out Japanese: speech-region anchor recall `>= 95%`, non-empty Tesseract crops `>= 98%` mapped regions, micro CER `<= 15%`.
3. Vertical dense-text geometry: strict one-to-one recall `>= 99%`; no candidate covers more than one tight reference line.
4. Benchmark floor: Windows, CPU `>= 6` cores, NVIDIA RTX 3060 `8 GiB`; warm detector P95 `<= 250 ms`, ready simple-region overlay P95 `<= 3.0 s`, incremental detector VRAM `<= 2.5 GiB`.
5. Cold initialization plus first detector result P95 remains `<= 5.0 s`. Cache-free `r8-pruned` is a No-Go and cannot justify a higher threshold or a longer product timeout.
6. Deterministic progressive publication, candidate-scoped cancellation, detector-unavailable behavior, headless S9/S10 integration, packaging/licence/checksum/offline-install/rollback, Release build, focused/full tests and documentation checks all pass.

Persistent worker/prewarm and normal bytecode cache may be evaluated only by separate reproducible evidence. Neither waives the RTX 3060/package evidence nor changes the cold gate, default, UI, profiles or recognizer rule.

### Non-goals

* Default enablement or UI exposure of the detector pilot.
* New persisted profile data or changes to saved OCR zones.
* Any `IOcrEngine` change.
* Unconditional full-frame retry, including full-frame `2x` retry.
* Replacing Tesseract with Paddle as recognizer.
* Removal of Windows OCR, Tesseract, or legacy implementation.
* Modification of accepted ADR-024 or ADR-025.

### Причины

* Research geometry evidence supports the bounded detector-plus-Tesseract crop chain and does not support full-page legacy geometry as a target.
* Keeping Tesseract recognition preserves vertical-text requirements, the existing OCR seam and accepted compatibility boundaries.
* Region-scoped delivery preserves the product benefit that a ready simple translation is not delayed by a difficult neighbour.
* Explicit release gates prevent favourable two-page research evidence or an unproven package optimization from becoming default product behavior.

### Последствия

Positive:

* Future work has one quality-first target path with a clear detector/recognizer boundary.
* Legacy work is constrained to compatibility and fallback safety instead of competing target-algorithm investment.
* The owner can evaluate release readiness against retained, measurable gates.

Negative:

* The product retains a temporary fallback while detector quality, RTX 3060, startup and packaging gates remain open.
* GPU runtime packaging and startup remain material risks; cache-free pruning has already failed.
* Separate evidence is required for persistent worker/prewarm or normal bytecode-cache behavior.

Compatibility:

* No profile migration, persisted state, public OCR-contract, capture-scope or default-behavior change is proposed.
* ADR-024 and ADR-025 remain accepted and unchanged. This ADR does not supersede either one.

### Requires Migration

No.

### Approved

Project owner, explicit chat approval on 2026-08-10: "Подтверждаю ADR-026 как direction-only; production enablement не разрешаю".

---

# ADR-027

## Current RTX 3080 Host as the Project Target Evidence Baseline

Status:
ACCEPTED

Date:
2026-08-10

Supersedes:
ADR-024 and ADR-025 only for their named RTX 3060 8 GiB evidence-baseline requirement.

Related Change Request:
[#44](https://github.com/Dementius-cell/Game-Translator/issues/44)

### Контекст

ADR-024/ADR-025 require GPU candidate-detector evidence on an RTX 3060 with 8 GiB VRAM. The available project host is an AMD Ryzen 7 5700X3D with 8 physical cores, NVIDIA GeForce RTX 3080 with 10,240 MiB VRAM, 47.93 GiB RAM and NVIDIA driver `610.47`.

The project owner explicitly directed on 2026-08-10: «считаем мою машину с RTX 3080 за целевую с RTX 3060 8 GiB».

### Варианты

1. Keep the named RTX 3060 8 GiB evidence requirement and block final evidence until that separate host is available.
2. Treat the current RTX 3080 host as automatically equivalent to every RTX 3060 and publish its results as RTX 3060 performance claims.
3. Make the documented current RTX 3080 host the project target evidence baseline while retaining every numerical gate and clearly limiting evidence claims to that host.

### Решение

Use option 3.

The documented RTX 3080 host is the project target evidence baseline for the GPU candidate-detector pilot. Evidence that passes all retained ADR-024/ADR-025 thresholds on this host can satisfy the project's hardware-baseline gate.

This decision changes no numerical threshold: warm detector P95 remains `<= 250 ms`, cold initialization plus first detector result P95 remains `<= 5.0 s`, ready simple-region overlay P95 remains `<= 3.0 s`, and incremental detector VRAM remains `<= 2.5 GiB`.

This decision does not claim that the same P95, VRAM or enablement result applies to a generic RTX 3060 8 GiB system. It does not change the quality, package, licence, checksum, offline-install, rollback, cancellation, Release, default, UI, profile, `IOcrEngine`, Tesseract-recognizer, legacy-fallback or full-frame-retry boundaries.

### Причины

* The project owner can run reproducible evidence on the available target host now.
* The actual hardware configuration is explicitly recorded instead of being silently substituted for an RTX 3060.
* Retaining every numerical and quality gate prevents a stronger GPU from becoming an implicit production-enablement waiver.

### Последствия

Positive:

* The final-stage benchmark can proceed on the owner-approved target host.
* Reports can state one concrete, reproducible hardware baseline.

Negative:

* Results cannot be represented as performance evidence for every RTX 3060 8 GiB system.
* The selected host may hide constraints of lower-performing hardware; this is an explicit owner baseline choice.

Compatibility:

* No profile migration, persistence, public contract, capture-scope, default or production behavior change occurs.
* Cache-free `r8-pruned` remains a No-Go. Persistent worker/prewarm and normal bytecode-cache evidence remain separate required experiments.

### Requires Migration

No.

### Approved

Project owner, explicit chat approval on 2026-08-10: «считаем мою машину с RTX 3080 за целевую с RTX 3060 8 GiB».

---

---

# ADR-028

## User-visible end-to-end translated-overlay SLO after required prewarm

Status:
ACCEPTED

Date:
2026-08-11

Supersedes:
ADR-025 only for its `<= 5.0 s` cold-worker-initialization-plus-first-detector-result timing gate. All other ADR-025 gates remain binding.

Related Change Request:
[#45](https://github.com/Dementius-cell/Game-Translator/issues/45)

### Context

The fresh normal-bytecode package cannot satisfy ADR-025's cold-start gate: the worker reached ready at 11,479 ms and first result at 12,204 ms in the offline package experiment. In contrast, the prewarmed target chain — GPU Paddle detector, bounded grouping, existing Tesseract crop recognition, direct GoogleWeb translation and per-region overlay — met a cache-miss first-region P95 of 1,111.0 ms across 30 samples on the ADR-027 target host. The project owner accepts up to five seconds from an already-ready captured text region to a translated overlay.

### Decision

For the opt-in detector pilot only, the binding timing SLO is P95 `<= 5.0 s` from a successfully acquired captured frame after the required detector and direct-GoogleWeb provider readiness barrier until publication of the first valid translated `OverlaySnapshot` for a retained region. The run uses cache miss and reports provider failure, timeout and throttling; it never silently replaces them with cache hit, `WebAuto`, a default provider, full-page retry or legacy fallback.

The GPU candidate detector remains transient inside the saved OCR zone. The target chain stays `GPU Paddle detector -> bounded grouping -> existing Tesseract crop recognition -> translation -> per-region overlay`; Tesseract remains mandatory.

Each region may publish as soon as its own direct-GoogleWeb translation is ready. A conditional `MinimumOverlayVisibleDuration` of two seconds starts after publication only while that region's source identity remains current. A changed/disappeared source, capture loss, zone change, feature disable or safety invalidation removes it immediately; an invalidated-epoch result cannot publish on an unrelated later frame.

Future pilot delivery uses direct GoogleWeb per-page bounded concurrency of three, indexed response mapping, cancellation and epoch validation before publication. It retains direct-only provider identity and failure/throttling telemetry.

Cold initialization plus first detector result remains mandatory diagnostic/release telemetry for every package, but is no longer itself the accepted `<= 5.0 s` enablement gate. Physical WPF rendered visibility remains a separate quality gate.

### Consequences

Positive:

* The user-facing performance gate measures a translated overlay in a ready session rather than Python/Paddle import latency before monitoring begins.
* Regions appear progressively, with a bounded readability interval when their source remains current.
* Bounded concurrency improves multi-region completion while protecting first-region responsiveness and provider stability.

Negative:

* Delivery must introduce a deterministic readiness state, cancellation/epoch ownership, source-identity validation, bounded concurrency and tests for late-result suppression.
* Direct GoogleWeb remains an experimental external endpoint, so failures and throttling remain first-class evidence rather than hidden fallback conditions.

Compatibility:

* Pilot/default remains disabled and unchanged pending a separate owner Go/No-Go decision.
* No UI/profile/schema/`IOcrEngine`/translation-cache contract change, full-frame retry, legacy removal, `WebAuto` fallback or direct-GoogleWeb default is authorized.
* ADR-024, ADR-026 and ADR-027 are unchanged; ADR-025 changes only as named above.

### Requires Migration

No.

### Approved

Project owner, explicit chat approval on 2026-08-11: “принимаю”, accepting the proposed ADR-028 after its stated scope, direct-GoogleWeb evidence, progressive per-region publication and conditional two-second readability direction were presented. Pilot/default enablement remains unauthorized.

---

# ADR-029

## Retained-Candidate Quality Baseline for the Disabled Detector Pilot

Status:
ACCEPTED

Date:
2026-08-12

Supersedes:
ADR-026 only for Gate 2's held-out non-empty mapped Tesseract-crop threshold. All other ADR-026, ADR-024, ADR-025, ADR-027 and ADR-028 boundaries remain binding.

Related Change Request:
[#46](https://github.com/Dementius-cell/Game-Translator/issues/46)

### Context

The owner reference geometry remains strong under the current strict CJK retained-candidate filter: S9 `10/10`, S10 `6/6`, zero retained extras and no overlay/mask regressions. On the fixed 25-page OpenMantra Japanese held-out corpus, the same path maps `198/206` speech anchors (`96.12%`), returns `187/198` non-empty mapped Tesseract crops (`94.44%`), has micro CER `11.05%`, and has zero overlay warnings, out-of-page items and overlaps.

ADR-026 predeclared `>=98%` non-empty mapped crops. Disabling the filter reaches `98.99%` but reintroduces four known S10 extras. Calibrated enclosure and local image-classifier probes did not safely improve the trade-off: both suppress genuine speech candidates. The owner accepts the current quality result and directs the work to continue rather than repeat filter tuning.

### Options

1. Keep the `>=98%` threshold and block all remaining pilot evidence until a new retained-candidate policy is found.
2. Disable or loosen the current filter to reach `>=98%`, accepting known owner-reference extras.
3. Accept the current strict filter as the disabled-pilot quality baseline, set the held-out non-empty threshold to `>=94%`, and retain every zero-extra/overlay-safety gate.

### Decision

Use option 3.

For the opt-in, disabled-by-default detector pilot's held-out Japanese quality gate, replace ADR-026 Gate 2's non-empty mapped Tesseract-crop threshold with `>=94%`. The accepted current baseline is `187/198` (`94.44%`). Retain the other held-out criteria: speech-region anchor recall `>=95%`, micro CER `<=15%`, and zero overlay warnings, out-of-page items and text overlaps.

Owner S9/S10 remains unchanged and strict: S9 `10/10`, S10 `6/6`, zero retained extras, no source-mask regression and no overlay geometry regression. The current strict CJK filter is accepted as-is; this ADR does not authorize a configuration, source or runtime-policy change.

### Consequences

Positive:

* Remaining package, readiness and physical-render evidence can proceed on one stable quality baseline.
* The accepted policy preserves owner-verified zero-extra geometry rather than silently optimizing a corpus metric at the expense of known false positives.

Negative:

* The accepted held-out policy can omit a non-empty crop for up to six percent of mapped speech regions.
* This is a documented quality trade-off, not a claim that the filter is optimal or a substitute for future quality work.

Compatibility:

* No profile, saved-zone, UI, default, `IOcrEngine`, recognizer, cache, retry, fallback or legacy-selection behavior changes.
* Tesseract remains mandatory for every accepted crop.
* Pilot/default and production enablement remain unauthorized. This ADR only changes the quality threshold for future disabled-pilot gate evaluation.

### Requires Migration

No.

### Approved

Project owner, explicit chat approval on 2026-08-12: «принимаю текущий quality результат». The same owner direction does not authorize production enablement.

---

# ADR-030

## Default Candidate-Region Pipeline and Retirement of Legacy Full-Page Orchestration

Status:
ACCEPTED

Date:
2026-08-13

Supersedes:
ADR-026 only for its direction-only temporary legacy fallback and its no-production-enablement boundary; ADR-028 and ADR-029 only for their disabled-pilot/default compatibility boundaries. Their target chain, mandatory Tesseract recognition, quality gates, direct-provider behavior, safety semantics and timing limits remain binding.

Related Change Request:
[#47](https://github.com/Dementius-cell/Game-Translator/issues/47)

### Context

The legacy full-page OCR/grouping path has failed the owner geometry reference: S9 `0/10` and S10 `0/6`. The validated target chain — GPU Paddle detector, bounded deterministic grouping, existing Tesseract crop recognition and per-region overlay — has passed S9 `10/10`, S10 `6/6`, zero retained extras, accepted held-out quality, readiness/recovery, package integrity, same-host clean-root offline-install, rollback and resource evidence.

The project owner accepts the available verification record and directs the product away from legacy full-page orchestration to the quality-first target chain.

### Decision

1. The default one-shot and live translation paths use `GPU Paddle detector -> bounded grouping -> existing Tesseract crop recognition -> configured translation provider -> per-region overlay` inside every saved manual OCR zone.
2. Live translation uses the persistent-worker and provider readiness barrier. Candidate regions retain bounded translation concurrency, source-identity validation, per-region progressive publication, cancellation and the conditional post-publication readability interval.
3. Detector/runtime/readiness failure produces a controlled degraded result; it does not silently invoke legacy full-page OCR/grouping, a full-frame retry, `WebAuto`, cached data or another provider as a substitute.
4. Legacy full-page orchestration is removed from normal product entry points. It may be retained temporarily only as an explicit diagnostic/compatibility implementation while a later cleanup verifies safe deletion.
5. Tesseract remains mandatory for every accepted candidate crop. Windows OCR and Tesseract remain mandatory product capabilities. This decision does not require a public `IOcrEngine` change or remove an OCR engine.
6. Existing selected translator-provider behavior remains unchanged. Direct GoogleWeb is accepted as release evidence, but this ADR does not make a web provider a silent application default.

### Consequences

Positive:

* Normal product behavior follows the owner-validated geometry rather than the failed legacy full-page path.
* A detector failure is visible and diagnosable instead of concealing quality loss behind an unvalidated fallback.
* Saved OCR zones remain the user-controlled capture boundary; detector-derived candidates remain transient.

Negative:

* A package without the verified detector runtime cannot translate through the default path and must report a degraded state.
* Legacy implementation remains temporarily in the codebase for diagnostic/compatibility use, so its deletion is a separate cleanup task rather than an unverified destructive edit.

Compatibility:

* This decision authorizes necessary default, UI, profile and composition changes for the target path. The current implementation needs no profile migration or public OCR-contract change.
* The owner-accepted quality gates and the ADR-028 P95 `<= 5.0 s` ready-session translated-overlay SLO remain binding.
* No game-process interaction, secret-storage change, automatic full-frame retry, direct-GoogleWeb default or silent fallback is authorized.

### Requires Migration

No. Existing saved manual OCR zones remain the migration boundary.

### Approved

Project owner, explicit chat approval on 2026-08-13: «снимем запреты на изменение так как нам нужно уйти от легаси логики к текущей обеспечивающий отличный результат».

---

# ADR-031

## Per-Zone Content Layout Mode Policy

Status:
ACCEPTED

Date:
2026-08-22

Related Change Request:
[#56](https://github.com/Dementius-cell/Game-Translator/issues/56)

### Context

ADR-030 made bounded Paddle candidates, writing-system grouping, Tesseract crop recognition and per-region overlay the normal product path. The saved OCR-zone model still contains historical full-page translation-grouping fields, while live timing and overlay behavior are configured through separate mechanisms. This does not provide one product-level place to describe how a particular zone should group detected blocks, lay out translations and schedule work when future content types such as static menus or books are approved.

Writing-system grouping and content layout are different dimensions. Language and orientation determine whether a zone needs spaced LTR, CJK horizontal, CJK vertical, complex South-East Asian, Brahmic or RTL grouping details. Content layout determines the higher-level behavior expected from the zone, including grouping strategy, overlay policy and live refresh cadence.

### Options

1. Keep independent grouping, overlay and timing controls without a shared content policy.
2. Add a global profile mode that applies to every OCR zone.
3. Add an explicit per-zone `ContentLayoutMode` resolved by an Application policy, while keeping writing-system selection automatic inside that policy.

### Decision

Use option 3.

* Every saved OCR zone has a `ContentLayoutMode`. The initial and default value is `DialogComic`.
* `DialogComic` preserves the accepted ADR-030 behavior: bounded writing-system candidate grouping, Tesseract recognition of accepted crops, centered per-region translation layout and participation in every currently scheduled live refresh.
* The Application policy for a mode owns three explicit dimensions: candidate grouping strategy, candidate overlay layout policy and minimum per-zone live refresh interval. Compatible mode-specific capabilities may be added to the same policy when an accepted mode needs them.
* Writing-system grouping remains an automatic nested resolution from OCR language and orientation. It is not exposed as a manual language-by-language selector.
* The setting belongs to an OCR zone, not the whole profile, so different areas can eventually use different policies without breaking multi-zone independence.
* `Add zone` and `Pick screen` create `DialogComic` zones. The selected-zone UI exposes the saved content-layout value.
* Existing profiles that omit the additive field load as `DialogComic`; JSON remains the persistence format and `schemaVersion` remains `1.0` while this defaulting behavior is verified.
* Historical `TranslationGroupingMode` and `TextGrouping` fields remain readable for compatibility and explicit `LegacyFullPage` diagnostics. They do not select or replace the normal candidate grouping path.
* A new mode such as `Book` or `StaticMenu`, a non-zero cadence, or a change to the `DialogComic` default requires its own product decision, measurable regressions and applicable performance/overlay gates. This ADR does not authorize hidden book heuristics.
* This policy cannot select legacy full-page OCR, full-frame retry, another provider, cache fallback or a different provider default. ADR-030 remains binding.

### Reasons

* A zone is the correct ownership boundary for content-specific capture and overlay behavior.
* One policy prevents future features from scattering coupled grouping, layout and timing switches across unrelated services.
* Automatic writing-system resolution preserves the tested language cohorts without forcing users to understand OCR geometry rules.
* Starting with one behavior-preserving mode creates the extension seam without inventing unmeasured Book or StaticMenu values.

### Consequences

Positive:

* Future content types can add coherent policies instead of branching independently in OCR, overlay and live orchestration.
* Existing profiles and current live behavior remain compatible.
* Multi-zone profiles can later mix content policies safely.

Negative:

* The profile gains an additive per-zone setting even though only one value is initially available.
* Pipeline state keys, diagnostics, UI editing and profile tests must include the mode.
* Future non-zero cadence policies need deterministic clock-aware tests and performance evidence before acceptance.

Compatibility:

* Missing `contentLayoutMode` defaults to `DialogComic`.
* No OCR engine, translator provider, cache, secret storage or legacy-selection default changes.
* No automatic fallback is added.

### Requires Migration

No destructive migration. Additive defaulting and import/export compatibility tests are required.

### Approved

Project owner, explicit chat approval on 2026-08-22: «согласен приступай. сделай Content layout mode» with grouping, overlay policy, per-area translation frequency and future mode-specific behavior in scope.

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
