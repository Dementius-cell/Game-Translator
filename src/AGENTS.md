# src/AGENTS.md

These instructions apply to production source code under `src/**`.

## Purpose

Keep production code aligned with the accepted C#/.NET 9, WPF, MVVM, and Clean Architecture design.

## Ownership

- `GameTranslator.Domain` owns framework-independent domain models, profile value objects, and validation rules.
- `GameTranslator.Application` owns use cases, orchestration, pipeline behavior, ports, and cross-layer abstractions.
- `GameTranslator.Infrastructure` owns adapters for Windows APIs, OCR engines, SQLite, JSON persistence, Credential Manager, translation providers, and update mechanisms.
- `GameTranslator.UI` owns WPF presentation, view models, WPF services, region picking, overlay windows, and the composition host.

## Local Contracts

- Preserve dependency direction: UI and Infrastructure may depend on Application; Application may depend on Domain; Domain must not depend on UI, Infrastructure, or Application.
- Do not introduce direct UI-to-Infrastructure references. The existing UI composition module loading pattern is the boundary for Infrastructure implementations.
- External systems, side effects, platform APIs, persistence, credentials, OCR engines, capture, translation providers, and hotkeys cross layers through explicit Application contracts.
- Do not add process injection, game memory access, hooks, drivers, kernel code, anti-cheat bypass, or game reverse engineering. Game interaction remains screen capture plus OCR.
- Secrets stay behind Windows Credential Manager or approved protected storage and must not appear in JSON, SQLite, logs, debug artifacts, or user-facing messages.

## Work Guidance

- Keep changes close to the affected layer and module.
- Add abstractions when crossing a layer or external side-effect boundary; avoid interfaces for local implementation detail that does not cross a boundary.
- Keep pipeline, OCR, overlay, cache, profile, and translation behavior deterministic enough for impact-based tests.
- Preserve multi-zone independence unless an accepted ADR or explicit issue changes that behavior.

## Verification

- For production code changes, run `dotnet build GameTranslator.sln -c Release --no-restore` unless dependency restoration or a different configuration is explicitly required.
- Run `dotnet test GameTranslator.sln -c Release --no-build` for behavior changes, shared contracts, cross-layer changes, or fixes with test coverage.
- For docs-only changes under this subtree, run the applicable Markdown checks instead of code gates.

## Child Instruction Index

- `GameTranslator.Domain/AGENTS.md` covers domain models and validation.
- `GameTranslator.Application/AGENTS.md` covers use cases, ports, services, pipeline, cache, OCR contracts, translation contracts, profiles, settings, hotkeys, debug, and updates.
- `GameTranslator.Infrastructure/AGENTS.md` covers concrete adapters and external system implementations.
- `GameTranslator.UI/AGENTS.md` covers WPF presentation, view models, UI services, region picker, overlay, and composition hosting.
