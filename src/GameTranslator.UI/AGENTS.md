# UI Instructions

These instructions apply to `src/GameTranslator.UI/**`.

## Architecture

- UI may depend on Application and Domain, but must not directly reference Infrastructure.
- Keep MVVM boundaries: ViewModels own UI state and commands; Views should stay thin.
- Do not put OCR, translator, cache, or grouping algorithms in UI.

## Diagnostics Export

- Diagnostics exports must not include secrets.
- Prefer structured JSON with explicit raw OCR, translation source, mask source, overlay geometry, timings, and status fields.
- When changing diagnostics, update smoke tests and the relevant design note if the exported contract changes.

## Overlay View

- Preserve overlay layers: OCR/debug, mask, translation, debug metrics.
- Text should not be clipped or unreadable on common DPI/resolution settings.
