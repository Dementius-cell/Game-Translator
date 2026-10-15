# GameTranslator.UI AGENTS.md

These instructions apply to `src/GameTranslator.UI/**`.

## Purpose

Keep the WPF presentation layer ergonomic, testable, and separated from infrastructure implementation details.

## Ownership

- WPF app startup, windows, views, XAML resources, and code-behind.
- View models, commands, validation helpers, and presentation state.
- WPF services for dialogs, navigation, region picking, overlay rendering, hotkey registration, logging, settings fallback, and debug resource monitoring.
- Composition hosting and external service module loading.
- Local bug/crash report storage and process-session recovery behind the Application `IBugReportService` contract.

## Local Contracts

- Follow MVVM: keep business rules, pipeline behavior, OCR, translation, cache, profile persistence, and provider logic out of views and code-behind.
- UI may reference Application, but must not directly reference Infrastructure. Use the existing composition module seam for Infrastructure implementations.
- Overlay remains a separate WPF responsibility with mask, translation, OCR/debug layers kept conceptually separate.
- Do not steal focus or block user input unexpectedly. Prefer headless tests and evidence generation; run visible UI only when explicitly needed, and report the process ID for explicit UI runs.
- Do not expose secrets in UI state, dialogs, logs, debug panels, screenshots, or copied text.
- Bug/crash reports use the application `Reports` directory with a visible local-app-data fallback. Session leases distinguish active instances from interrupted runs. Keep exception messages, Data, source paths, raw logs, screenshots, and OCR/translation text out of these reports; do not suppress fatal termination after recording it.

## Work Guidance

- Keep code-behind limited to WPF lifecycle, visual interaction glue, and view-specific plumbing.
- Keep view models deterministic and testable with Application-level fakes.
- Keep four profile tabs (zones, recognition, translation, appearance) separate from global settings/diagnostics navigation. Preserve access to advanced zone parameters in the collapsed editor and validate responsive layout headlessly at supported window sizes.
- Editor themes default to the Windows app colour mode; persist explicit Light/Dark/System preference via ISettingsService. ThemeService owns editor-scoped dynamic resources and marshals system notifications to each UI dispatcher. Never apply these dictionaries to Application.Resources or the translation overlay. Verify both themes and dispose static Windows event subscriptions.
- Preserve click-through, capture exclusion, always-on-top, and overlay positioning behavior when touching overlay code.
- Prefer clear recoverable user states for capture, OCR language packs, providers, and overlay failures.

## Verification

- For view-model behavior changes, run UI smoke/view-model tests.
- For overlay, region picker, hotkey, focus, or WPF service changes, run focused tests and add visual/manual evidence only when the task actually needs UI verification.
- Do not launch the UI for docs-only or pure view-model edits unless explicitly requested.

## Child Instruction Index

No nested `AGENTS.md` files are currently defined below this directory.

The zone editor owns the ADR-033 AutoFontSize toggle and its model/duplicate round trip. Physical overlay geometry, font and padding must be converted together for DPI.

Overlay TextFormatter and TextBlock must use matching whole-word wrapping. Expanded rendering may only scale down to contain actual text; report word overflow to the Application sizing policy.

UI labels and application-owned display messages use Localization/Strings.json via loc:Text or LocalizedTextConverter. Select ru for the Windows user UI language ru, otherwise en at startup; do not change CurrentCulture. Keep profile names, zone names, recognized text, credentials and provider identifiers unchanged. Verify both UI languages with both editor themes.
