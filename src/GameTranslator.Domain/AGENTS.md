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
- Keep absolute and relative geometry semantics explicit and compatible with multi-zone profiles.

## Work Guidance

- Prefer simple immutable or validation-friendly domain shapes where practical.
- Put pure validation in Domain; put IO, serialization, repository behavior, and migration orchestration outside Domain.
- Keep error codes stable when external tests or documentation may depend on them.

## Verification

- For domain behavior changes, run domain/profile tests plus architecture dependency tests.
- For profile shape or validation changes, include compatibility, import/export, and migration tests where applicable.

## Child Instruction Index

No nested `AGENTS.md` files are currently defined below this directory.
