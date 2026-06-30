# Overlay Instructions

These instructions apply to `src/GameTranslator.Application/Overlay/**`.

## Ownership

Overlay application services map OCR-frame geometry into screen-space overlay snapshots. They do not call OCR engines, translator providers, credential storage, or WPF controls.

## Placement Rules

- Preserve the distinction between translated text items and mask items.
- Text items may be positioned from semantic translation groups.
- Mask items should cover accepted raw source text, not unrelated art.
- For vertical CJK, translated text should be readable horizontal text expanded from the source group center unless a future approved design says otherwise.
- Keep placement deterministic and testable with frame-relative geometry.

## Diagnostics

When changing placement behavior, ensure diagnostics can explain:

- source bounds;
- text bounds;
- mask bounds;
- text/mask intersections;
- any group or anchor metadata introduced by the change.
