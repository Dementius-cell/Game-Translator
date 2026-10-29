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

- StaticMenu is a per-zone content mode. Its overlay bypasses minimum-preview-size expansion and renders within the source frame at every DPI without Viewbox scaling. Preserve the selected physical font size (including the minimum after auto fitting); use ellipsis for overflow instead of silently shrinking text. Translate once shares the existing one-shot command and configurable Ctrl+Shift+F7 action; reject it while busy/live without stopping the user's session.

- Keep code-behind limited to WPF lifecycle, visual interaction glue, and view-specific plumbing.
- Keep view models deterministic and testable with Application-level fakes.
- The New profile button immediately persists and selects a fresh default profile named `new` (then `new (2)`, etc. on name collisions). Keep draft initialization separate: startup and editor reset must not create profiles.
- Keep four profile tabs (zones, recognition, translation, appearance) separate from global settings/diagnostics navigation. Preserve access to advanced zone parameters in the collapsed editor and validate responsive layout headlessly at supported window sizes.
- Auto OCR model search filters the available list only; it must not clear hidden selections. Show the language selector and its advice only while Auto OCR is enabled; hiding them must preserve selections. Keep the selected list independent of search filtering and synchronize it with the model-code selection, saved profile and draft settings. Preserve selected models across search, profile reload and independent OCR/translation Auto toggles; reject empty or unsupported selections before starting work.
- Keep multilingual OCR and automatic translation-source controls independent, retaining the manual source language. Loading legacy profiles may resolve the nullable translation Auto setting from the OCR flag; when the user enables Auto OCR with two or more selected models, or changes the enabled selection to two or more models, enable translation Auto while retaining the manual source language. Loading profiles/drafts must preserve their saved translation choice; reducing the list or disabling OCR Auto must not switch translation Auto off. Keep provider validation and never silently change providers. Installation of all catalog packs is an explicit cancellable action with progress and failure counts, not a side effect of search or selection.
- Editor themes default to the Windows app colour mode; persist explicit Light/Dark/System preference via ISettingsService. ThemeService owns editor-scoped dynamic resources and marshals system notifications to each UI dispatcher. Never apply these dictionaries to Application.Resources or the translation overlay. Verify both themes and dispose static Windows event subscriptions.
- Preserve click-through, capture exclusion, always-on-top, and overlay positioning behavior when touching overlay code.
- Prefer clear recoverable user states for capture, OCR language packs, providers, and overlay failures.

## Verification

- Candidate readiness diagnostics distinguish per-zone detector degradation from session activity. Label Generation/RestartCount as session counters in exported debug information; do not present them as the Python worker's lifecycle counters.

Language-selection advice is localized, informational and selection-dependent. Do not turn it into forbidden language pairs, automatic deselection, reordering or profile validation without separate evidence and scope.

Keep the selected OCR language collection stable and apply item-level notifications when checkboxes change. Exercise both bound lists with WPF controls; model-only toggles do not cover UI collection updates during clicks.

- For view-model behavior changes, run UI smoke/view-model tests.
- For OCR model selection changes, cover filtering without selection loss, the independent selected list, save/reload and draft persistence, invalid selections, separate Auto controls and cancellable package installation as applicable.
- For overlay, region picker, hotkey, focus, or WPF service changes, run focused tests and add visual/manual evidence only when the task actually needs UI verification.
- Do not launch the UI for docs-only or pure view-model edits unless explicitly requested.

## Child Instruction Index

No nested `AGENTS.md` files are currently defined below this directory.

The zone editor owns the ADR-033 AutoFontSize toggle and its model/duplicate round trip. Physical overlay geometry, font and padding must be converted together for DPI.

Overlay TextFormatter and TextBlock must use matching whole-word wrapping. Expanded rendering may only scale down to contain actual text; report word overflow to the Application sizing policy.

UI labels and application-owned display messages use Localization/Strings.json via loc:Text or LocalizedTextConverter. Select ru for the Windows user UI language ru, otherwise en at startup; do not change CurrentCulture. Keep profile names, zone names, recognized text, credentials and provider identifiers unchanged. Verify both UI languages with both editor themes.

Additional OCR preparation controls persist in profiles and drafts and participate in preset matching. They require preprocessing enabled; show that Sauvola supersedes the fixed threshold. Support manual scale 0.5–3. New options remain off by default and are independent from Auto OCR language selection.

The preparation selector owns None/Manual/Adaptive. Manual filter controls are disabled outside Manual but their values remain saved. Keep both mode and preset selectors enabled in all three modes: choosing the Off preset must never disable the control needed to select another preset. Loading legacy profiles resolves the old enabled flag; persist explicit mode in profiles and drafts. Auto OCR languages remain a separate setting.

When Auto OCR is enabled with a manual reading direction, retain the orientation selector but highlight its border with ThemeDanger and recommend Auto in localized text. Remove the warning when orientation is Auto or OCR Auto is disabled; do not force a direction change.
