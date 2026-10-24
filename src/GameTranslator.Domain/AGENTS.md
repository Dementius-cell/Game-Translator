# GameTranslator.Domain AGENTS.md

These instructions apply to `src/GameTranslator.Domain/**`.

## Purpose

Keep the domain layer small, deterministic, and independent from frameworks and external systems.

## Ownership

- Game profile models and value objects.
- OCR zone geometry and text style settings.
- Overlay/profile settings that are plain domain data.
- Profile validation rules and validation error contracts.

## Local Contracts

- Do not add dependencies on Application, Infrastructure, UI, WPF, Windows APIs, persistence libraries, logging frameworks, DI frameworks, OCR engines, or translation providers.
- Keep `GameTranslator.Domain.csproj` framework-independent unless an accepted decision explicitly changes the layer contract.
- Preserve profile compatibility expectations: schema-related domain changes require matching migration, import/export, and validation coverage outside this subtree.
- Preserve the independent OCR and translation Auto settings. Missing `ExperimentalMultilingual` means false; missing `MultilingualLanguages` retains the default model list. Enabled multilingual OCR requires Tesseract and a nonempty selection. Keep model-code lookup and installed-package checks outside Domain.
- `AutoDetectSourceLanguage` is nullable for backward compatibility: null is distinct from explicit false. Application/UI resolve legacy null using the profile's multilingual OCR flag; do not collapse null to false in serialization. Explicit true resolves the translation source to auto without overwriting the stored manual `SourceLanguage`. Preserve the current supported-provider validation and test old-profile, explicit true/false and selected-model round trips.
- Keep absolute and relative geometry semantics explicit and compatible with multi-zone profiles.

## Work Guidance

- ContentLayoutMode.StaticMenu is additive under ADR-035; omitted contentLayoutMode remains DialogComic in schema 1.0. Keep mode semantics in Application. KeepWithinSourceBounds is the menu overlay policy; do not renumber existing enum values.

- Prefer simple immutable or validation-friendly domain shapes where practical.
- Put pure validation in Domain; put IO, serialization, repository behavior, and migration orchestration outside Domain.
- Keep error codes stable when external tests or documentation may depend on them.

## Verification

- For domain behavior changes, run domain/profile tests plus architecture dependency tests.
- For profile shape or validation changes, include compatibility, import/export, and migration tests where applicable.

## Child Instruction Index

No nested `AGENTS.md` files are currently defined below this directory.

Overlay text style includes optional AutoFontSize (default false); absent values preserve schema-1 fixed-font behavior under ADR-033.

Optional schema-1 preprocessing flags InvertColors, AdaptiveThresholding and AddWhiteBorder default to false when absent. The finite manual scale range is 0.5–3; retain legacy JSON import/export behavior.

Preprocessing Mode is nullable for legacy profiles: null maps IsEnabled to Manual/None. Explicit None/Manual/Adaptive takes precedence. Reject unknown enum values; retain manual parameters when selecting Adaptive.
