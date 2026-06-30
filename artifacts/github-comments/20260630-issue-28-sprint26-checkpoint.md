Sprint 26 working-tree checkpoint, 2026-06-30.

This update is not a pushed commit yet. It records the current local checkpoint after the CJK/vertical overlay investigation and documentation pass.

Implemented / updated locally:

- Brought repository guidance closer to the current Sprint 26 state:
  - root `AGENTS.md` now names Sprint 26 / #28 and #32 as the active work;
  - added DOX-style local `AGENTS.md` files for pipeline, overlay, OCR infrastructure, UI, and tests;
  - updated the docs index and Sprint 26-related source-of-truth docs;
  - added `docs/design/vertical-cjk-overlay-placement.md` as the active design note for manga-style vertical Chinese/Japanese overlay placement.
- Split the vertical CJK problem into three explicit responsibilities:
  - raw text detection: what OCR blocks are considered real text candidates;
  - semantic grouping: which OCR blocks are joined for translation;
  - overlay placement/masking: where translated text and raw source masks are drawn.
- Added pipeline support for keeping translation source groups and mask source blocks distinct:
  - `SourceOcrResult` remains raw OCR;
  - `TranslationSourceOcrResult` is used for cache/translation;
  - `MaskSourceOcrResult` is used for overlay masks.
- Added vertical CJK grouping hardening for `NearbyBlocks`:
  - right-to-left column order with top-to-bottom ordering inside a column;
  - conservative handling for wide horizontal OCR noise so it cannot bridge unrelated vertical columns;
  - light bubble/label background gating to reduce masks over halftone/body/art texture;
  - tests for noise filtering, vertical order, wide noise, halftone false positives, and mask/translation source separation.
- Expanded diagnostics export so future manual captures can show the actual grouping/overlay decision path:
  - `sourceOcr`;
  - `translationSourceOcr`;
  - `maskSourceOcr`;
  - `overlayGeometry.semanticGroups` with group id, grouped text, frame/screen bounds, mask source indexes, raw source indexes, text item index, translated bounds, and overlay anchor.

Validation on the current local checkpoint:

- `dotnet build GameTranslator.sln -c Release` passed with 0 warnings / 0 errors.
- `dotnet test GameTranslator.sln -c Release --no-build` passed: `315/315`.
- `git diff --check` passed with only expected CRLF/LF normalization warnings.

Current status:

- #28 remains open.
- #32 remains the active visual/manual validation target.
- #34 remains code-side ready and still needs real-provider smoke validation.
- Do not start #29/#30 until Sprint 26 is explicitly closed or accepted by the project owner.

Next recommended manual step:

Run the current Release build, reproduce the manga/vertical CJK case, and export a fresh diagnostics package. The next export should include `maskSourceOcr` and `overlayGeometry.semanticGroups`, which should make it possible to see whether a bad result is caused by OCR noise selection, semantic grouping, mask source selection, or overlay anchor placement.
