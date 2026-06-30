# Pipeline Instructions

These instructions apply to `src/GameTranslator.Application/Pipeline/**`.

## Ownership

The pipeline orchestrates capture, OCR, grouping, cache/translation, and overlay snapshot creation. Keep concrete OCR engines, translator providers, credential storage, and UI rendering outside this directory.

## Vertical CJK Contract

- Keep raw OCR output, translation source groups, and mask source blocks distinct.
- `SourceOcrResult` is raw OCR.
- `TranslationSourceResult` is what cache/translation consumes.
- `MaskSourceResult` is what overlay masks consume.
- Do not assume one OCR block equals one translated overlay item.
- For vertical CJK, semantic grouping should prefer right-to-left columns and top-to-bottom order inside each column.
- Reject obvious OCR noise before it can create translation requests or masks.

## Documentation

- Update `docs/design/vertical-cjk-overlay-placement.md` when changing grouping, mask source selection, or overlay anchoring behavior.
- Add or update tests for grouping edge cases, especially noisy OCR blocks, wide horizontal noise, halftone/background false positives, and reading order.
