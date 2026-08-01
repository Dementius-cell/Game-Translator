# GameTranslator.Application AGENTS.md

These instructions apply to `src/GameTranslator.Application/**`.

## Purpose

Keep application use cases and contracts independent from concrete UI, platform, persistence, OCR, and translation implementations.

## Ownership

- Application services and orchestration.
- Ports and abstractions for capture, OCR, translation, cache, credentials, settings, profiles, hotkeys, debug, and updates.
- Translation pipeline behavior, timings, grouping, multi-zone handling, cancellation, and recoverable failures.
- Profile migrations and import/export orchestration.

## Local Contracts

- Application may depend on Domain and lightweight DI abstractions already accepted by the project.
- Do not add WPF, Windows API, SQLite, Tesseract, network SDK implementation, Credential Manager, file dialog, or UI framework dependencies here.
- Cache lookup remains part of normal translation flow before provider calls; the default TTL policy stays 30 days unless a Decision Record changes it.
- Keep OCR contracts engine-neutral. Windows OCR and Tesseract remain mandatory product capabilities implemented outside this layer.
- Keep translator contracts provider-neutral. Credentialed Google, Azure, and Yandex remain the supported provider set unless a Decision Record changes it.
- Preserve raw OCR block geometry for diagnostics and masking; semantic grouping may create translation groups without destroying source geometry.

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
