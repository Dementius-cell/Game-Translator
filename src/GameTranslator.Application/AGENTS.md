# GameTranslator.Application AGENTS.md

These instructions apply to `src/GameTranslator.Application/**`.

## Purpose

Keep application use cases and contracts independent from concrete UI, platform, persistence, OCR, and translation implementations.

## Ownership

- Application services and orchestration.
- Ports and abstractions for capture, OCR, translation, cache, credentials, settings, profiles, hotkeys, debug, and updates.
- Translation pipeline behavior, timings, grouping, multi-zone handling, cancellation, and recoverable failures.
- Opt-in watermark candidate selection and bounded per-session/per-zone page evidence and bounded source-frame and neighboring OCR-line context for fragmented emblems; preserve raw OCR and source/revision authority, exclude only translation/overlay projection.
- Profile migrations and import/export orchestration.

## Local Contracts

- Application may depend on Domain and lightweight DI abstractions already accepted by the project.
- Do not add WPF, Windows API, SQLite, Tesseract, network SDK implementation, Credential Manager, file dialog, or UI framework dependencies here.
- Cache lookup remains part of normal translation flow before provider calls; the default TTL policy stays 30 days unless a Decision Record changes it.
- Keep OCR contracts engine-neutral. Windows OCR and Tesseract remain mandatory product capabilities implemented outside this layer.
- Keep translator contracts provider-neutral. Credentialed Google, Azure, and Yandex remain the supported provider set unless a Decision Record changes it.
- Resolve combined Tesseract model lists by their component writing systems. Bound horizontal candidate stacks by original detector-member heights (maximum 2:1) so large titles cannot absorb small captions through intermediate lines.
- Preserve raw OCR block geometry for diagnostics and masking; semantic grouping may create translation groups without destroying source geometry. Candidate requests may carry immutable crop-relative detector line hints; preprocessing must scale hints with pixels, and line coverage remains bounded numeric evidence rather than proof of OCR accuracy.
- With automatic translation source enabled, preserve compact aligned dialogue lines across script changes; a script change alone is not a translation boundary. Preserve detector-region identity in projected crop sources and never merge different detector regions. Spatially independent fragments can remain separate. Han/Latin remain language-ambiguous; explicit Japanese/Korean hints require substantial kana/Hangul. Split inside a line only with complete, confident word geometry from one recognition pass. Preserve raw masks and reading order; route/cache by each group's effective source language, then restore output order. Manual-source profiles retain their grouping behavior.

## Work Guidance

- Model external effects as interfaces here and implement them in Infrastructure or UI services.
- Prefer explicit request/result types over loosely shaped dictionaries or stringly typed contracts.
- Keep async operations cancellable where the surrounding service already supports cancellation.
- Use deterministic clocks, options, and fakes in tests when cache, timing, expiration, or retry behavior is involved.

## Verification

- For application behavior changes, run focused Application tests and the architecture dependency tests.
- For pipeline, cache, OCR contract, translation contract, profile migration, or multi-zone changes, run the full test suite unless there is a documented reason not to.

## Child Instruction Index

No nested `AGENTS.md` files are currently defined below this directory.

ADR-033 expanded overlay layout preserves source height where unobstructed and optionally fits font size through IOverlayTextMeasurer; the bounded in-memory font-fit cache does not replace translation caching.

Explicit user cache clearing removes all memory and persistent translations with translation stopped; ordinary expiry and the 30-day TTL remain unchanged. Translation and diagnostic reading order honor resolved OCR source orientation before request-level Auto, independently for projected detector regions.

Opt-in OCR preparation applies inversion before Sauvola (31 px window, k=0.2, R=128); Sauvola replaces the fixed threshold and is capped at four million source pixels with cancellation. OcrService adds 10 px white borders after scaling, shifts detector hints and removes padding from blocks, members, words and candidate bounds before returning the original scaled coordinate system. Preserve pass IDs, orientation, confidence and zone metadata. Existing profiles perform the original single OCR pass; no recognizer/provider fallback is introduced.

Three-way preparation selects effective settings before any filtering. Adaptive analyzes original pixels with bounded sampling; it constructs fresh neutral manual settings and never inherits stored filters. It uses crop hints for scale/padding and conservative luminance/color heuristics for inversion/Sauvola. No new OCR retries. Returned OCR requests carry effective settings for diagnostics.
