# GameTranslator.UI AGENTS.md

These instructions apply to `src/GameTranslator.UI/**`.

## Purpose

Keep the WPF presentation layer ergonomic, testable, and separated from infrastructure implementation details.

## Ownership

- WPF app startup, windows, views, XAML resources, and code-behind.
- View models, commands, validation helpers, and presentation state.
- WPF services for dialogs, navigation, region picking, overlay rendering, hotkey registration, logging, settings fallback, and debug resource monitoring.
- Composition hosting and external service module loading.

## Local Contracts

- Follow MVVM: keep business rules, pipeline behavior, OCR, translation, cache, profile persistence, and provider logic out of views and code-behind.
- UI may reference Application, but must not directly reference Infrastructure. Use the existing composition module seam for Infrastructure implementations.
- Overlay remains a separate WPF responsibility with mask, translation, OCR/debug layers kept conceptually separate.
- Do not steal focus or block user input unexpectedly. Prefer headless tests and evidence generation; run visible UI only when explicitly needed, and report the process ID for explicit UI runs.
- Do not expose secrets in UI state, dialogs, logs, debug panels, screenshots, or copied text.

## Work Guidance

- Keep code-behind limited to WPF lifecycle, visual interaction glue, and view-specific plumbing.
- Keep view models deterministic and testable with Application-level fakes.
- Preserve click-through, capture exclusion, always-on-top, and overlay positioning behavior when touching overlay code.
- Prefer clear recoverable user states for capture, OCR language packs, providers, and overlay failures.

## Verification

- For view-model behavior changes, run UI smoke/view-model tests.
- For overlay, region picker, hotkey, focus, or WPF service changes, run focused tests and add visual/manual evidence only when the task actually needs UI verification.
- Do not launch the UI for docs-only or pure view-model edits unless explicitly requested.

## Child Instruction Index

No nested `AGENTS.md` files are currently defined below this directory.
